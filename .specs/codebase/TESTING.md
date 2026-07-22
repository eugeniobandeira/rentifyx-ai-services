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

## Current coverage (as of E-02)

- `RentifyxAiServices.Shared.Tests` — 4 tests (idempotency store: first-seen, duplicate-key, null-key validation, placeholder).
- `RentifyxAiServices.Moderation.Tests` — 24 tests (Rekognition client retry/failure paths, threshold boundaries, key convention filter, event publisher routing, orchestrator branch coverage, handler malformed-event handling).
- `RentifyxAiServices.IntegrationTests` — `ModerationPipelineTests` (3 tests: clean-image approve, duplicate-ETag skip, violating-image reject) plus a placeholder. **Requires a running Docker daemon** (Testcontainers.LocalStack + Testcontainers.Kafka) — compiles clean but has not been verified green in every environment; confirm with a real Docker-available run before trusting this gate.

## Known gap

No formal Test Coverage Matrix / Parallelism Assessment table exists yet (greenfield gap noted during E-02's Tasks phase, not a violation) — task-level parallel-safety was judged from file/dependency isolation alone. Worth formalizing once a second feature (E-03) adds more test surface.
