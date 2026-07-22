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
  - **Blocker**: `tests/RentifyxAiServices.IntegrationTests/ModerationPipelineTests.cs` (LocalStack S3/DynamoDB + real Kafka via Testcontainers) compiles clean but has never run green — this sandbox has no running Docker daemon (`DockerUnavailableException`). Needs a real run with Docker available to verify.

## Open Items

- Confirm the final .NET 10 SDK pin to use in CI and local development.
- Align the eventual Lambda packaging and Terraform deployment strategy for production.
- `terraform validate` for `iac/modules/iam-roles` and `iac/modules/review-queue` still needs to run in an environment with registry access — only `fmt -check` was verified here.
- `tests/RentifyxAiServices.IntegrationTests/ModerationPipelineTests.cs` needs to actually run against a live Docker daemon before E-02 can be called fully verified — see blocker above.
- S3 key convention (`assets/{ownerId}/{assetId}/{filename}`) still needs confirmation from the `asset-registry-api` team before `iac/modules/s3-trigger` (still unbuilt) is wired to a real bucket — re-verified still true as of E-02's design pass, no code in that repo confirms or denies it.
- ADR-AI-005 through 007 still not written — tied to E-03/E-04, written when those land.
- `iac/modules/{lambda-moderation,lambda-enrichment,s3-trigger,kafka-event-source-mapping}` still empty — scoped to E-02 real-bucket wiring/E-03.
- `KAFKA_BOOTSTRAP_SERVERS`, `REVIEW_QUEUE_URL`, `FAILURE_DLQ_URL`, `IDEMPOTENCY_TABLE_NAME` env vars in `ModerationHandler` have no real values yet — set once `iac/modules/lambda-moderation` wires real infra.
