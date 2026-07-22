# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

Solution builds and CI is green. `Moderation` has a real, tested handler stack (E-02, shipped 2026-07-22) — Enrichment and Dedupe are still placeholder `Class1.cs` files. Check `.specs/project/STATE.md` and `.specs/project/ROADMAP.md` for what's actually been implemented before assuming a component exists.

`tests/RentifyxAiServices.IntegrationTests/ModerationPipelineTests.cs` (LocalStack + Kafka via Testcontainers) is verified green — requires a running Docker daemon to execute.

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
- `src/Functions/Enrichment` — consumes asset events, invokes Bedrock (`InvokeModel`) through a structured prompt flow. Still placeholder — not yet implemented (E-03/E-04).
- `src/Functions/Dedupe` — deferred Phase 2 scaffold (DEF-AI-001); role is pre-scoped in Terraform (`rekognition:CompareFaces`) but no implementation yet.
- `src/Shared` — event contract layer (`Events/`: `Verdict`, `ModerationLabel`, `AssetMediaModerated`, `AssetPendingManualReview`), AWS abstractions, `Idempotency/DynamoDbIdempotencyStore`, `Kafka/KafkaEventPublisher<T>` (generic — reused by Moderation, intended for Enrichment too). Shared contracts must stay versioned and additive-only.
- `iac/modules/iam-roles` — one dedicated IAM role + policy per Lambda, zero permission overlap between functions (ADR-AI-002). Moderation's policy covers Rekognition, S3 read, DynamoDB PutItem, MSK IAM-auth publish, SQS SendMessage.
- `iac/modules/review-queue` — SQS manual-review queue + its DLQ, a separate Rekognition-failure DLQ, and a CloudWatch alarm on review-queue depth (MOD-04).

The service is **event-only** — it never exposes synchronous HTTP endpoints. No synchronous coupling should leak in.

Internal integration: `asset-registry-api` publishes `AssetCreated`; this service consumes it and publishes `AssetMediaModerated` / `AssetEnrichmentSuggested` back.

External dependencies: S3, Rekognition, Bedrock Runtime, Amazon MSK/Kafka, DynamoDB (idempotency), OpenTelemetry.

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
- Treat Bedrock cost and prompt safety as first-class design constraints when touching Enrichment.
- Verify NuGet package versions against the live NuGet.org flat-container API (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) before pinning in `Directory.Packages.props` — several original pins had already been pulled from NuGet.org by the time E-02 was implemented. Never guess a version number.
- `Amazon.Lambda.S3Events` 4.0.0+ nests its record types under `S3Event` (e.g. `S3Event.S3EventNotificationRecord`, `S3Event.S3Entity`) — the older flat `S3EventNotification.*` shape from 2.x/3.x no longer exists. Confirm actual type shapes via the package's XML doc file (`<nuget-cache>/<package>/<version>/lib/<tfm>/*.xml`, grep for `name="T:`) when a compiler error suggests an assumed API doesn't match, rather than guessing alternate names.

## Traceability

`.specs/` holds structured project/feature docs — check these before larger changes:
- `.specs/project/STATE.md`, `ROADMAP.md`, `PROJECT.md` — current progress and plan
- `.specs/features/*/spec.md`, `tasks.md` — per-feature specs and task breakdown
- `.specs/codebase/*.md` — architecture/stack/conventions summaries (this file is derived in part from them; update both if they drift)
- `docs/adr/` — accepted architecture decisions (ADR-AI-001, ADR-AI-002 so far)
