# Structure

The repository follows the same high-level pattern used by the neighboring RentifyX services:

- `src` for runnable code and shared libraries
- `tests` for unit and integration coverage
- `iac` for Terraform and deployment assets
- `docs` for ADRs and design notes
- `.specs` for workflow traceability

## Current state

- `src/Functions/Moderation/RentifyxAiServices.Moderation/` — implemented (E-02). `ModerationHandler.cs` (entrypoint) and `ModerationService.cs` (orchestrator) at root; each collaborator grouped with its interface in its own subfolder, mirroring `Shared`'s `Events/`/`Idempotency/`/`Kafka/` pattern: `KeyConvention/` (`IKeyConventionFilter`, `AssetKeyConventionFilter`), `Rekognition/` (`IRekognitionModerationClient`, `RekognitionModerationClient`, `ModerationScanResult`), `Threshold/` (`IThresholdEvaluator`, `ThresholdEvaluator`), `Publishing/` (`IModerationEventPublisher`, `KafkaModerationEventPublisher`).
- `src/Functions/Enrichment/RentifyxAiServices.Enrichment/` — implemented (E-03). `EnrichmentHandler.cs` (entrypoint) and `EnrichmentService.cs` (orchestrator) at root; `Bedrock/` (`IBedrockEnrichmentClient`, `BedrockEnrichmentClient`, `EnrichmentResult`), `Publishing/` (`IEnrichmentEventPublisher`, `KafkaEnrichmentEventPublisher`) — same per-concern subfolder pattern as Moderation.
- `src/Functions/Dedupe/RentifyxAiServices.Dedupe/` — placeholder `Class1.cs` only.
- `src/Shared/RentifyxAiServices.Shared/` — `Events/` (event contracts, including `AssetEnrichmentSuggested`), `Idempotency/` (DynamoDB idempotency store), `Kafka/` (generic event publisher). C# namespace `RentifyxAiServices.SharedKernel`.
- `tests/RentifyxAiServices.Moderation.Tests/` — 24 unit tests covering every Moderation component.
- `tests/RentifyxAiServices.Enrichment.Tests/` — 15 unit tests covering every Enrichment component.
- `tests/RentifyxAiServices.Shared.Tests/` — 4 unit tests covering the idempotency store.
- `tests/RentifyxAiServices.IntegrationTests/` — `ModerationPipelineTests.cs` and `EnrichmentPipelineTests.cs`, LocalStack + Kafka (Testcontainers) end-to-end tests; requires a running Docker daemon to execute.
- `iac/modules/iam-roles/` — per-Lambda IAM roles; `moderation` policy covers Rekognition, S3 read, DynamoDB write, SQS send. No Kafka IAM statement — the broker is self-hosted PLAINTEXT (`rentifyx-platform`'s `module.kafka`), reachable via VPC/security group, not IAM. `enrichment` policy currently only has `bedrock:InvokeModel` — needs S3/DynamoDB/SQS added, tracked as a follow-up.
- `iac/modules/review-queue/` — SQS review queue + DLQ + Rekognition-failure DLQ + CloudWatch depth alarm.
- `iac/modules/{lambda-moderation,s3-trigger}/` — built (2026-07-23), composed together in `iac/terraform/` root config; `terraform validate` clean, `apply` still blocked on the media bucket/deployment package/idempotency table not existing anywhere.
- `iac/modules/{lambda-enrichment,kafka-event-source-mapping}/` — not yet created; Enrichment's own Terraform wiring (including a new enrichment idempotency table and failure-DLQ module) is a follow-up, same posture Moderation's IaC took relative to its own code.
- `docs/adr/` — ADR-AI-001 through ADR-AI-006 accepted.
