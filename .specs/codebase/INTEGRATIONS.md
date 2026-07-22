# Integrations

## External

- AWS S3
- AWS Rekognition
- AWS Bedrock Runtime
- Kafka — self-hosted (EC2, KRaft, PLAINTEXT), provisioned by the sibling `rentifyx-platform` repo's `module.kafka`, not Amazon MSK. MSK Serverless was evaluated there and replaced (`rentifyx-platform` ADR-002, `.specs/features/self-hosted-kafka/`). Bootstrap address resolved via `terraform_remote_state` + SSM parameter at deploy time (same pattern `rentifyx-identity-api` uses), not an IAM `kafka-cluster:*` permission — reachability is VPC/security-group based (broker SG allows any client inside the shared VPC's CIDR on port 9092).
- DynamoDB for idempotency
- OpenTelemetry-backed observability

## Internal

- `asset-registry-api` publishes `AssetCreated` and consumes `AssetMediaModerated` / `AssetEnrichmentSuggested`.
- Moderation (E-02) does **not** consume `AssetCreated`/`AssetMediaUploaded` from Kafka — it triggers directly off the S3 `ObjectCreated` event, confirmed against `asset-registry-api`'s source (media isn't attached to Kafka events, only a bare S3 key). See ADR-AI-004 and `.specs/features/e02-moderation-pipeline/spec.md`'s Reality Check.
- **Unconfirmed cross-repo assumption**: the S3 key convention (`assets/{ownerId}/{assetId}/{filename}`) that `AssetKeyConventionFilter` depends on. `asset-registry-api`'s `S3MediaStorageService` (the component that would fix the real format) doesn't exist yet — re-confirm before wiring `iac/modules/s3-trigger` to a real bucket.
