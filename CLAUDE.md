# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

Solution builds and CI is green. `Moderation` (E-02, shipped 2026-07-22) and `Enrichment` (E-03, shipped 2026-07-23) both have real, tested handler stacks — `Dedupe` is still a placeholder `Class1.cs`. Check `.specs/project/STATE.md` and `.specs/project/ROADMAP.md` for what's actually been implemented before assuming a component exists.

`tests/RentifyxAiServices.IntegrationTests/` (`ModerationPipelineTests.cs`, `EnrichmentPipelineTests.cs`; LocalStack + Kafka via Testcontainers) is verified green — requires a running Docker daemon to execute.

Both Lambdas' Terraform IaC exists (`iac/modules/{iam-roles,review-queue,lambda-moderation,s3-trigger}`, composed in `iac/terraform/`) except Enrichment's own module set (`lambda-enrichment`, `kafka-event-source-mapping`, an idempotency table, a failure DLQ) — code shipped ahead of its IaC, same order Moderation took.

## Commands

```bash
dotnet restore RentifyxAiServices.slnx
dotnet build RentifyxAiServices.slnx --configuration Release
dotnet test RentifyxAiServices.slnx --configuration Release
```

Run a single test project:
```bash
dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj
```

Run a single test by name (xunit filter):
```bash
dotnet test --filter "FullyQualifiedName~MethodOrClassName"
```

CI (`.github/workflows/ci.yml`) runs restore, build, and test on push/PR to `main` — same commands as above, on `ubuntu-latest`.

## Architecture

Event-driven AI service for RentifyX: three independent AWS Lambda functions, each deployed and IAM-scoped separately (never as a shared package or shared role — see ADR-AI-001, ADR-AI-002).

- `src/Functions/Moderation` — consumes S3 upload events, calls Rekognition (`DetectModerationLabels`). Implemented (E-02): `ModerationHandler` (entrypoint) → `ModerationService` (orchestrator) → `AssetKeyConventionFilter` → `DynamoDbIdempotencyStore` → `RekognitionModerationClient` (retry/backoff) → `ThresholdEvaluator` (60%/90% boundaries, ADR-AI-003) → `KafkaModerationEventPublisher` (publishes `AssetMediaModerated`/`AssetPendingManualReview`, enqueues SQS review queue on `PendingReview`, ADR-AI-004).
- `src/Functions/Enrichment` — Kafka-triggered (AWS Lambda Kafka event source mapping for self-managed Kafka, `Amazon.Lambda.KafkaEvents`), not S3. Implemented (E-03): `EnrichmentHandler` (entrypoint) → `EnrichmentService` (orchestrator, skips non-`Approved` verdicts) → `DynamoDbIdempotencyStore` (separate table from Moderation's, keyed `enrichment:{assetId}`) → S3 `GetObjectAsync` (bucket/key from the triggering `AssetMediaModerated` v2 event) → `BedrockEnrichmentClient` (Bedrock Converse API, Claude Sonnet 5, tool-forced structured output, retry/backoff — ADR-AI-005/006) → `KafkaEnrichmentEventPublisher` (publishes `AssetEnrichmentSuggested`, or DLQ on any failure).
- `src/Functions/Dedupe` — deferred Phase 2 scaffold (DEF-AI-001); role is pre-scoped in Terraform (`rekognition:CompareFaces`) but no implementation yet.
- `src/Shared` — event contract layer (`Events/`: `Verdict`, `ModerationLabel`, `AssetMediaModerated` v2 with `Bucket`/`Key`, `AssetPendingManualReview`, `AssetEnrichmentSuggested`), AWS abstractions, `Idempotency/DynamoDbIdempotencyStore`, `Kafka/KafkaEventPublisher<T>` (generic — reused by both Moderation and Enrichment). Shared contracts must stay versioned and additive-only. C# namespace is `RentifyxAiServices.SharedKernel` — the folder/csproj name stays `Shared`, only the namespace differs (renamed 2026-07-23 to avoid CA1716, "Shared" collides with a reserved keyword in other .NET languages).
- `iac/modules/iam-roles` — one dedicated IAM role + policy per Lambda, zero permission overlap between functions (ADR-AI-002). Moderation's policy covers Rekognition, S3 read, DynamoDB PutItem, SQS SendMessage. Enrichment's policy currently only has `bedrock:InvokeModel` (covers Converse too) — needs S3/DynamoDB/SQS added, tracked as a follow-up.
- `iac/modules/review-queue` — SQS manual-review queue + its DLQ, a separate Rekognition-failure DLQ, and a CloudWatch alarm on review-queue depth (MOD-04).
- `iac/modules/{lambda-moderation,s3-trigger}` — built, composed together with `iam-roles`/`review-queue` in `iac/terraform/` root config (`terraform validate` clean; `apply` still blocked on the media bucket/deployment package/idempotency table not existing anywhere). `iac/modules/{lambda-enrichment,kafka-event-source-mapping}` — not yet built.

The service is **event-only** — it never exposes synchronous HTTP endpoints. No synchronous coupling should leak in.

Internal integration: `asset-registry-api` publishes `AssetCreated`; this service consumes it and publishes `AssetMediaModerated` / `AssetEnrichmentSuggested` back.

External dependencies: S3, Rekognition, Bedrock Runtime, Kafka (self-hosted EC2/KRaft, PLAINTEXT, provisioned by the sibling `rentifyx-platform` repo — not Amazon MSK; bootstrap address comes from `terraform_remote_state` + SSM, injected as a Lambda env var at deploy time, no runtime IAM Kafka permission), DynamoDB (idempotency), OpenTelemetry.

### Cross-repo infra

- `rentifyx-platform` (sibling repo, `C:\Users\Eugenio\Projects\study\rentifyx-platform` locally) owns the shared VPC and the self-hosted Kafka broker — this repo's Lambdas must be VPC-attached to reach it, and read the bootstrap address via that repo's `terraform_remote_state` output (`kafka_ssm_parameter_path`), same pattern as `rentifyx-identity-api`'s `iac/terraform/main.tf`. `iac/modules/lambda-moderation` does this itself internally (reads `terraform_remote_state` directly, not via the root config) — `iac/modules/lambda-enrichment` (unbuilt) will need the same for its own Kafka event source mapping.
- `rentifyx-asset-registry-api` (sibling repo) is the consumer of this repo's `AssetMediaModerated`/`AssetEnrichmentSuggested` Kafka events — see `docs/adr/ADR-AI-003/004` here and its own `ADR-AR-008` for the cross-repo contract, and `.specs/project/STATE.md` there for its `G-001` (S3 key convention still unconfirmed) tracking. Its own consumers for these events don't exist yet as of E-03.

### Deploy and runtime model (ADR-AI-001)

- Each Lambda deploys independently with its own `aws-lambda-tools-defaults.json` and CI matrix entry — a broken Enrichment build must never block a Moderation deploy.
- Default runtime is the managed .NET runtime zip, not Native AOT. Cold start matters less here since these are async S3/Kafka-triggered functions, not request-path APIs. Native AOT is an open per-function optimization (most likely Moderation first) once real traffic data exists — don't preemptively AOT-proof code.

### IAM model (ADR-AI-002)

- No shared execution role, ever. Each function's Terraform module attaches only its own role.
- `rekognition:DetectModerationLabels` and `rekognition:CompareFaces` use `resources = ["*"]` by AWS API design (no resource-level scoping support) — this is not a least-privilege shortcut, keep it commented inline if touched.

## Conventions

- Thin Lambda entrypoints, testable handler classes — logic belongs in classes the entrypoint delegates to, not inline in the handler.
- `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on repo-wide (`Directory.Build.props`) — a warning fails the build.
- Central package management via `Directory.Packages.props` — add package versions there, not per-csproj.
- Prefer actual behavior over mock-first coverage; integration tests should exercise S3/Rekognition/Bedrock/Kafka via LocalStack-compatible fixtures (`Testcontainers.LocalStack`, `Testcontainers.Kafka`) where practical rather than mocking everything.
- Treat Bedrock cost and prompt safety as first-class design constraints when touching Enrichment — see ADR-AI-006 (`MaxTokens` cap, tool-forced structured output, system/image role separation).
- Verify NuGet package versions against the live NuGet.org flat-container API (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) before pinning in `Directory.Packages.props` — several original pins had already been pulled from NuGet.org by the time E-02 was implemented (recurred with `AWSSDK.BedrockRuntime` in E-03: pinned `4.0.14.0`, real latest was `4.0.100.6`). Never guess a version number.
- `Amazon.Lambda.S3Events` 4.0.0+ nests its record types under `S3Event` (e.g. `S3Event.S3EventNotificationRecord`, `S3Event.S3Entity`) — the older flat `S3EventNotification.*` shape from 2.x/3.x no longer exists. `Amazon.Lambda.KafkaEvents` follows the same nesting shape (`KafkaEvent.KafkaEventRecord`); `KafkaEvent.Records` is `Dictionary<string, IList<KafkaEventRecord>>` keyed by topic-partition, and `KafkaEventRecord.Value`/`.Key` are already-decoded `MemoryStream`, not base64 `string` (confirmed by compiler, not assumed). Confirm actual type shapes via the package's XML doc file (`<nuget-cache>/<package>/<version>/lib/<tfm>/*.xml`, grep for `name="T:`) when a compiler error suggests an assumed API doesn't match, rather than guessing alternate names — when the XML doc itself doesn't include exact member types (common for AWS SDK service model types like `Amazon.BedrockRuntime.Model.*`), fall back to just writing the code with your best-confirmed guess and letting the compiler's error message tell you the real type, iterating fast rather than fighting reflection tooling.
- The Shared project's C# namespace is `RentifyxAiServices.SharedKernel`, not `RentifyxAiServices.Shared` — "Shared" alone collides with a reserved keyword in other .NET languages (CA1716). Never reintroduce the bare `RentifyxAiServices.Shared` namespace in new code; the folder/csproj name staying `Shared` is intentional and unrelated.

## Traceability

`.specs/` holds structured project/feature docs — check these before larger changes:
- `.specs/project/STATE.md`, `ROADMAP.md`, `PROJECT.md` — current progress and plan
- `.specs/features/*/spec.md`, `tasks.md` — per-feature specs and task breakdown
- `.specs/codebase/*.md` — architecture/stack/conventions summaries (this file is derived in part from them; update both if they drift)
- `docs/adr/` — accepted architecture decisions (ADR-AI-001 through ADR-AI-006 so far)
