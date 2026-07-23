# Testing

- Unit tests should cover handler logic and threshold boundary behavior.
- Integration tests should exercise S3/Rekognition/Bedrock and Kafka interactions with LocalStack-compatible fixtures where practical.
- Prefer actual behavior over mock-first coverage.

## Gate check commands

| Gate | Command |
|---|---|
| quick (Shared) | `dotnet test tests/RentifyxAiServices.Shared.Tests/RentifyxAiServices.Shared.Tests.csproj` |
| quick (Moderation) | `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj` |
| full | `dotnet test RentifyxAiServices.slnx --configuration Release` |
| terraform | `terraform fmt -check` (recursive) — `terraform validate` needs registry access, not available in every sandbox |

## Current coverage (as of E-03)

- `RentifyxAiServices.Shared.Tests` — 4 tests (idempotency store: first-seen, duplicate-key, null-key validation, placeholder).
- `RentifyxAiServices.Moderation.Tests` — 24 tests (Rekognition client retry/failure paths, threshold boundaries, key convention filter, event publisher routing, orchestrator branch coverage, handler malformed-event handling).
- `RentifyxAiServices.Enrichment.Tests` — 15 tests (Bedrock client success/throttle-retry/throttle-exhausted/schema-mismatch, event publisher delegation, orchestrator branch coverage — verdict filter, idempotency, S3-missing, Bedrock-failure, success path — handler malformed-event handling and record delegation).
- `RentifyxAiServices.IntegrationTests` — `ModerationPipelineTests` (3 tests) and `EnrichmentPipelineTests` (3 tests: approved-publishes, duplicate-skips-Bedrock, not-approved-publishes-nothing) plus a placeholder. Verified green 2026-07-23 against a real Docker daemon (Testcontainers.LocalStack + Testcontainers.Kafka) — requires Docker to run. Bedrock is stubbed in the integration test (LocalStack community doesn't support it), same posture Rekognition would need if LocalStack ever dropped support for it.

A formal Test Coverage Matrix now exists per-feature in each `.specs/features/*/tasks.md` (E-03's Tasks phase formalized this, closing the gap E-02 left noted) rather than centralized here — check the relevant feature's `tasks.md` for the matrix and Parallelism Assessment behind any given task's `[P]` flag.
