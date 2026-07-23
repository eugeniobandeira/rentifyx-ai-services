# State

## Decisions

- Use a multi-Lambda repo with shared event contracts and isolated IAM roles.
- Keep the repo event-only and avoid synchronous API exposure.
- Treat duplicate/fraud detection as deferred and explicitly scaffolded.
- Follow the same repo convention as the existing RentifyX services: use `src`, `tests`, `iac`, `docs`, and `.specs`.

## Current Status

- Foundation scaffold created for the repository.
- Solution and initial .NET projects were created and wired into the solution.
- Infrastructure folder was normalized to `iac/` to match the existing service convention.
- Verified with a real build: `dotnet build RentifyxAiServices.slnx` completed successfully.

- Fixed naming drift: `tests/RentifyX.AiServices.*` renamed to `RentifyxAiServices.*.Tests` to match `src` and sibling repos (`RentifyxIdentity`, `RentifyxCommunications`).
- Fixed structural drift: `Models/`, `Prompts/`, and `Shared/{Aws,Events,Kafka,Idempotency,Observability}` moved inside their owning `.csproj` project folders (were sitting one level above, outside the project).
- E-01 closed via `.specs/features/e01-foundation/` (spec.md + tasks.md), tlc-spec-driven workflow:
  - 4 xUnit test projects scaffolded and wired into `RentifyxAiServices.slnx` (Moderation, Enrichment, Shared, Integration), one placeholder test each.
  - `tests/Directory.Build.props` added — suppresses CA1707/CA1716/CA1859/CA1305/CA1001 for test conventions (mirrors `rentifyx-identity-api` pattern).
  - `.github/workflows/ci.yml` added: restore → build → test on push/PR to `main`. Gate is pass/fail only, no coverage threshold (user decision 2026-07-22, overrides original plan T-008).
  - `iac/modules/iam-roles` written: one role + scoped policy per Lambda (moderation, enrichment, dedupe), no shared execution role. `terraform fmt -check` passes; `terraform validate` blocked in this sandbox by provider-download failure (insufficient system resources for `terraform init`), not an HCL error — needs re-validation in an environment that can reach the Terraform registry.
  - `docs/adr/ADR-AI-001-independent-deploy-native-aot.md` and `docs/adr/ADR-AI-002-iam-isolation.md` written and accepted.
- Gate check: `dotnet test RentifyxAiServices.slnx` → 4/4 projects pass, 0 failures.
- E-02 (Moderation Pipeline) implemented via `.specs/features/e02-moderation-pipeline/` (spec.md + design.md + tasks.md), tlc-spec-driven workflow, 2026-07-22:
  - Full handler stack built: `AssetKeyConventionFilter`, `DynamoDbIdempotencyStore` (Shared), `RekognitionModerationClient` (retry/backoff on throttling), `ThresholdEvaluator` (60/90 boundaries), `KafkaEventPublisher<T>` (Shared, generic — reusable by Enrichment), `KafkaModerationEventPublisher`, `ModerationService` orchestrator, `ModerationHandler` Lambda entrypoint. Replaces the old placeholder `Class1.cs`.
  - `iac/modules/iam-roles`'s `moderation` policy extended with DynamoDB PutItem, MSK IAM-auth publish, SQS SendMessage — gap found during design (role previously only had Rekognition + S3 read). New `iac/modules/review-queue` module: SQS review queue + its DLQ, separate Rekognition-failure DLQ, CloudWatch depth alarm.
  - `docs/adr/ADR-AI-003-moderation-thresholds.md` and `ADR-AI-004-hybrid-moderation-strategy.md` written and accepted.
  - Several NuGet package pins in `Directory.Packages.props` were stale (removed from NuGet.org) and had to be bumped to real current versions during implementation: `AWSSDK.Rekognition` 4.0.14.0→4.0.100.5, `Amazon.Lambda.Core` 2.7.0→3.1.1, `Amazon.Lambda.S3Events` 2.2.0→4.0.0, `Amazon.Lambda.Serialization.SystemTextJson` 2.6.0→3.0.0, `Amazon.Lambda.TestUtilities` 2.3.0→3.0.0. New pins added: `AWSSDK.SQS`, `AWSSDK.S3`. All versions confirmed against the live NuGet.org flat-container API before pinning, not guessed.
  - `Amazon.Lambda.S3Events` 4.0.0 flattened its old nested `S3EventNotification` wrapper class — the real type is `Amazon.Lambda.S3Events.S3Event.S3EventNotificationRecord` (nested under `S3Event`), confirmed via the package's XML doc comments, not the (misleading) older API shape assumed in the original task breakdown.
  - Gate check: `dotnet test` across Shared.Tests (4), Moderation.Tests (24), Enrichment.Tests (1), IntegrationTests (1 placeholder) → 30/30 pass. Full solution `dotnet build --configuration Release` clean, 0 warnings/errors.
  - `tests/RentifyxAiServices.IntegrationTests/ModerationPipelineTests.cs` (LocalStack S3/DynamoDB + real Kafka via Testcontainers) verified 2026-07-22 with Docker running → 3/3 pass. Fixed three real bugs surfaced only by a live run: `KafkaEventPublisher<T>` was serializing `Verdict` as its numeric ordinal instead of its name (added `JsonStringEnumConverter` — cross-repo Kafka consumers would otherwise have had to hardcode enum ordinals); the test's S3/DynamoDB clients had both `ServiceURL` and `RegionEndpoint` set, which routed requests to real AWS instead of LocalStack (fixed via `AuthenticationRegion`); Kafka consumers subscribed before the topic existed (fixed by pre-creating topics via `AdminClient`).

- **Cross-repo infra correction (2026-07-22, post-E-02):** discovered the sibling `rentifyx-platform` repo (`C:\Users\Eugenio\Projects\study\rentifyx-platform`) owns shared infra — VPC, and a **self-hosted Kafka broker** (EC2, KRaft, PLAINTEXT, port 9092), not Amazon MSK. E-02's design/tasks assumed MSK IAM auth (not knowing this repo existed at the time); confirmed against `rentifyx-identity-api`'s already-working Terraform that the real pattern is: read the bootstrap address once via `terraform_remote_state` + `aws_ssm_parameter` at deploy time, inject as an env var — no runtime IAM `kafka-cluster:*` permission, no SASL/IAM auth. Removed the incorrect `KafkaPublishVerdict` IAM statement (and its now-meaningless `moderation_kafka_cluster_arn`/`moderation_kafka_topic_arn` variables) from `iac/modules/iam-roles/main.tf`; `terraform validate` re-run clean. Docs corrected: `CLAUDE.md` (new "Cross-repo infra" section), `.specs/codebase/INTEGRATIONS.md`, `.specs/codebase/STRUCTURE.md`, and a correction note added atop E-02's `design.md` (left the rest as historical record rather than rewritten).
- `terraform validate` for `iac/modules/iam-roles` and `iac/modules/review-queue` re-run 2026-07-22 with registry access available — both pass clean (previously only `fmt -check` had been verified).

## Open Items

- Confirm the final .NET 10 SDK pin to use in CI and local development — `global.json` pins `10.0.100-preview.7` (rollForward `latestFeature`), but every build this session actually resolved `10.0.400-preview.0.26322.102` (a locally-installed higher feature band); need to decide whether to pin that exact version explicitly (and confirm it's installable via `actions/setup-dotnet` on `ubuntu-latest`, not just present locally) or wait for a stable 10.0 GA release. Product/deploy decision, not something to resolve unilaterally.
- S3 key convention (`assets/{ownerId}/{assetId}/{filename}`) still needs confirmation from the `asset-registry-api` team — `iac/modules/s3-trigger` is now built (notification + `aws_lambda_permission` wiring, `bucket_id`/`bucket_arn`/`lambda_function_arn`/`lambda_function_name` taken as inputs), but deliberately takes `filter_prefix`/`filter_suffix` as no-default variables rather than hardcoding the convention, since it's still tracked unresolved on both sides (this repo's `AssetKeyConventionFilter`; `asset-registry-api`'s `STATE.md` gap `G-001`) and unconfirmed in real code on either side. Root-level composition (wiring the real bucket, the real prefix/suffix, and `lambda-moderation`'s output ARN together) is still pending.
- ADR-AI-005 through 007 still not written — tied to E-03/E-04, written when those land.
- `iac/modules/{lambda-moderation,lambda-enrichment,s3-trigger,kafka-event-source-mapping}` still empty — `lambda-moderation` now additionally needs VPC attachment + a `terraform_remote_state` read of `rentifyx-platform`'s `kafka_ssm_parameter_path` output (see CLAUDE.md's "Cross-repo infra"), not just S3/Rekognition/DynamoDB wiring.
- `KAFKA_BOOTSTRAP_SERVERS`, `REVIEW_QUEUE_URL`, `FAILURE_DLQ_URL`, `IDEMPOTENCY_TABLE_NAME` env vars in `ModerationHandler` have no real values yet — set once `iac/modules/lambda-moderation` wires real infra.
