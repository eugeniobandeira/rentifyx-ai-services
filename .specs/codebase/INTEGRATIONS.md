# Integrations

## External

- AWS S3
- AWS Rekognition
- AWS Bedrock Runtime — Converse API (`IAmazonBedrockRuntime.ConverseAsync`), not raw `InvokeModel`; targets Claude Sonnet 5 via cross-region inference profile `us.anthropic.claude-sonnet-5` (ADR-AI-005). `bedrock:InvokeModel` IAM action covers Converse too (per the SDK's own XML doc note on `ConverseRequest`).
- Kafka — self-hosted (EC2, KRaft, PLAINTEXT), provisioned by the sibling `rentifyx-platform` repo's `module.kafka`, not Amazon MSK. MSK Serverless was evaluated there and replaced (`rentifyx-platform` ADR-002, `.specs/features/self-hosted-kafka/`). Bootstrap address resolved via `terraform_remote_state` + SSM parameter at deploy time (same pattern `rentifyx-identity-api` uses), not an IAM `kafka-cluster:*` permission — reachability is VPC/security-group based (broker SG allows any client inside the shared VPC's CIDR on port 9092).
- DynamoDB for idempotency
- OpenTelemetry-backed observability

## Internal

- `asset-registry-api` publishes `AssetCreated` and consumes `AssetMediaModerated` / `AssetEnrichmentSuggested`.
- Moderation (E-02) does **not** consume `AssetCreated`/`AssetMediaUploaded` from Kafka — it triggers directly off the S3 `ObjectCreated` event, confirmed against `asset-registry-api`'s source (media isn't attached to Kafka events, only a bare S3 key). See ADR-AI-004 and `.specs/features/e02-moderation-pipeline/spec.md`'s Reality Check.
- **Unconfirmed cross-repo assumption**: the S3 key convention (`assets/{ownerId}/{assetId}/{filename}`) that `AssetKeyConventionFilter` depends on. `asset-registry-api`'s `S3MediaStorageService` (the component that would fix the real format) doesn't exist yet — re-confirm before wiring `iac/modules/s3-trigger` to a real bucket.
- Enrichment (E-03) **does** consume Kafka — it's triggered by `AssetMediaModerated` (published by Moderation on the same topic Moderation writes to), via AWS Lambda's Kafka event source mapping for self-managed Kafka (`Amazon.Lambda.KafkaEvents`), not by S3 directly. This is the inverse of Moderation's trigger and is what `iac/modules/kafka-event-source-mapping` (still unbuilt) exists for.
- `AssetMediaModerated` is now v2 (`SchemaVersion = 2`, additive) — carries `Bucket`/`Key` so Enrichment can fetch the same S3 object Moderation scanned without a synchronous cross-service lookup. `asset-registry-api`'s own consumer for this event (if any exists yet) should tolerate the new optional-in-practice fields per the additive-contract convention.
- Enrichment publishes `AssetEnrichmentSuggested(AssetId, Description, Tags, Timestamp, SchemaVersion)` — `asset-registry-api`'s consumer for it doesn't exist yet (same situation `AssetMediaModerated` was in when E-02 shipped it).
