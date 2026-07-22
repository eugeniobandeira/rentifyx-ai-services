# Concerns

- Keep cold-start and deployment strategy evaluated per Lambda function.
- Ensure no synchronous coupling leaks into the repo.
- Avoid over-scoping the first version with substantial deferred features.
- Treat Bedrock cost and prompt safety as first-class design constraints.
- `tests/RentifyxAiServices.IntegrationTests/ModerationPipelineTests.cs` requires a running Docker daemon (Testcontainers.LocalStack + Testcontainers.Kafka) — not guaranteed to be available in every dev/CI sandbox. Compiles clean but is unverified where Docker isn't running; check CI's runner supports it before assuming this gate is green everywhere.
- S3 key convention `assets/{ownerId}/{assetId}/{filename}` (Moderation's `AssetKeyConventionFilter`, asset ID extraction) is an unconfirmed assumption, not a contract confirmed with `asset-registry-api` — flagged as a blocker before real S3 trigger wiring (`iac/modules/s3-trigger`, not yet built).
- Moderation confidence thresholds (60%/90%, ADR-AI-003) are the spec's stated starting point, not tuned against real Rekognition confidence distributions — revisit once production traffic data exists.
- NuGet package pins drift from what's actually published on NuGet.org faster than expected (several E-01 pins were already gone by E-02) — always re-verify against the NuGet.org flat-container API before trusting an existing pin as current.
