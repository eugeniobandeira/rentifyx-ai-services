# Structure

The repository follows the same high-level pattern used by the neighboring RentifyX services:

- `src` for runnable code and shared libraries
- `tests` for unit and integration coverage
- `iac` for Terraform and deployment assets
- `docs` for ADRs and design notes
- `.specs` for workflow traceability

## Current state

- `src/Functions/Moderation/RentifyxAiServices.Moderation/` — implemented (E-02). `ModerationHandler.cs` (entrypoint) and `ModerationService.cs` (orchestrator) at root; each collaborator grouped with its interface in its own subfolder, mirroring `Shared`'s `Events/`/`Idempotency/`/`Kafka/` pattern: `KeyConvention/` (`IKeyConventionFilter`, `AssetKeyConventionFilter`), `Rekognition/` (`IRekognitionModerationClient`, `RekognitionModerationClient`, `ModerationScanResult`), `Threshold/` (`IThresholdEvaluator`, `ThresholdEvaluator`), `Publishing/` (`IModerationEventPublisher`, `KafkaModerationEventPublisher`).
- `src/Functions/Enrichment/RentifyxAiServices.Enrichment/` — placeholder `Class1.cs` only.
- `src/Functions/Dedupe/RentifyxAiServices.Dedupe/` — placeholder `Class1.cs` only.
- `src/Shared/RentifyxAiServices.Shared/` — `Events/` (event contracts), `Idempotency/` (DynamoDB idempotency store), `Kafka/` (generic event publisher).
- `tests/RentifyxAiServices.Moderation.Tests/` — 24 unit tests covering every Moderation component.
- `tests/RentifyxAiServices.Shared.Tests/` — 4 unit tests covering the idempotency store.
- `tests/RentifyxAiServices.IntegrationTests/` — `ModerationPipelineTests.cs`, a LocalStack + Kafka (Testcontainers) end-to-end test; requires a running Docker daemon to execute.
- `iac/modules/iam-roles/` — per-Lambda IAM roles; `moderation` policy covers Rekognition, S3 read, DynamoDB write, SQS send. No Kafka IAM statement — the broker is self-hosted PLAINTEXT (`rentifyx-platform`'s `module.kafka`), reachable via VPC/security group, not IAM.
- `iac/modules/review-queue/` — SQS review queue + DLQ + Rekognition-failure DLQ + CloudWatch depth alarm.
- `iac/modules/{lambda-moderation,lambda-enrichment,s3-trigger,kafka-event-source-mapping}/` — not yet created; real infra wiring is a follow-on once `asset-registry-api`'s media bucket exists.
- `docs/adr/` — ADR-AI-001 through ADR-AI-004 accepted.
