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

## Open Items

- Confirm the final .NET 10 SDK pin to use in CI and local development.
- Align the eventual Lambda packaging and Terraform deployment strategy for production.
- Continue the implementation of the first real function handlers and shared contracts (E-02 next).
- `terraform validate` for `iac/modules/iam-roles` still needs to run in an environment with registry access — only `fmt -check` was verified here.
- ADR-AI-003 through 007 still not written — tied to E-02/E-03/E-04, written when those land.
- `iac/modules/{lambda-moderation,lambda-enrichment,s3-trigger,kafka-event-source-mapping}` still empty — scoped to E-02/E-03.
