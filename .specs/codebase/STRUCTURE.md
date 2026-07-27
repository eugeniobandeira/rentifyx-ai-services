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
- `src/Functions/Dedupe/RentifyxAiServices.Dedupe/` — implemented (DEF-AI-001, ADR-AI-007). `DedupeHandler.cs` (entrypoint) and `DedupeService.cs` (orchestrator) at root; `Hashing/` (`IPerceptualHashCalculator`, `AverageHashCalculator`), `Storage/` (`IImageHashStore`, `DynamoDbImageHashStore`), `Publishing/` (`IDedupeEventPublisher`, `KafkaDedupeEventPublisher`) — same per-concern subfolder pattern as Moderation/Enrichment. Reuses `Shared`'s `KeyConvention/` filter and `Idempotency/DynamoDbIdempotencyStore`.
- `src/Shared/RentifyxAiServices.Shared/` — `Events/` (event contracts, including `AssetEnrichmentSuggested`, `AssetDuplicateDetected`), `Idempotency/` (DynamoDB idempotency store), `Kafka/` (generic event publisher), `KeyConvention/` (`IKeyConventionFilter`, `AssetKeyConventionFilter` — moved here from Moderation once Dedupe needed the identical filter). C# namespace `RentifyxAiServices.SharedKernel`.
- `tests/RentifyxAiServices.Moderation.Tests/` — 20 unit tests covering every Moderation component.
- `tests/RentifyxAiServices.Enrichment.Tests/` — 15 unit tests covering every Enrichment component.
- `tests/RentifyxAiServices.Dedupe.Tests/` — 17 unit tests covering every Dedupe component.
- `tests/RentifyxAiServices.Shared.Tests/` — 8 unit tests covering the idempotency store and the key-convention filter.
- `tests/RentifyxAiServices.IntegrationTests/` — `ModerationPipelineTests.cs` and `EnrichmentPipelineTests.cs`, LocalStack + Kafka (Testcontainers) end-to-end tests; requires a running Docker daemon to execute.
- `iac/modules/iam-roles/` — per-Lambda IAM roles, zero permission overlap (ADR-AI-002). `moderation` covers Rekognition, S3 read, DynamoDB write, SQS send. No Kafka IAM statement anywhere — the broker is self-hosted PLAINTEXT (`rentifyx-platform`'s `module.kafka`), reachable via VPC/security group, not IAM. `enrichment` covers Bedrock InvokeModel, S3 read, DynamoDB write, SQS send. `dedupe` covers S3 read, DynamoDB GetItem/PutItem (two tables), SQS send — replaces the original `rekognition:CompareFaces` placeholder, which never applied (asset photos aren't faces).
- `iac/modules/review-queue/` — SQS review queue + DLQ + Rekognition-failure DLQ + Enrichment failure DLQ + Dedupe failure DLQ + CloudWatch depth alarm.
- `iac/modules/dynamodb-table/` — generic single-partition-key table module, reused by Moderation's, Enrichment's, and both of Dedupe's tables.
- `iac/modules/{lambda-moderation,s3-trigger,lambda-enrichment,kafka-event-source-mapping}/` — all built, composed together in `iac/terraform/` root config; `terraform validate` clean, `apply` still blocked on `rentifyx-platform` never being applied and a local credential-plumbing issue (see `.specs/project/STATE.md`).
- `iac/modules/lambda-dedupe/` — not yet created; Dedupe's own Terraform wiring (Lambda function resource + S3 trigger) is a follow-up, same posture Moderation's and Enrichment's IaC took relative to their own code.
- `docs/adr/` — ADR-AI-001 through ADR-AI-007 accepted.
