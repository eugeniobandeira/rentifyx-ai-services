# RentifyX AI Services

Event-driven AI service for moderation and enrichment workloads in the RentifyX platform.

## Current status

The repository foundation has been created and verified. The solution file and initial project skeletons are in place, and the repo now follows the same top-level organization used by the neighboring RentifyX services.

## Repository shape

- `src` — runnable code and shared libraries
- `tests` — unit and integration coverage
- `iac` — Terraform and deployment assets
- `docs` — ADRs and repository planning material
- `.specs` — project and feature traceability

## Current scaffold

- `src/Functions/Moderation` — moderation Lambda project skeleton
- `src/Functions/Enrichment` — enrichment Lambda project skeleton
- `src/Functions/Dedupe` — deferred Phase 2 scaffold
- `src/Shared` — shared library project skeleton
- `iac` — infrastructure directory aligned with the existing repo convention

## Verification

A real build was executed and succeeded:

```bash
dotnet build RentifyxAiServices.slnx
```

This confirms the current bootstrapped solution is in a buildable state.

## Decisions (ADRs)

- [ADR-AI-001](docs/adr/ADR-AI-001-independent-deploy-native-aot.md) — independent deploy per Lambda, managed runtime default
- [ADR-AI-002](docs/adr/ADR-AI-002-iam-isolation.md) — one IAM role per Lambda, no shared execution role

## Notes

- The service is event-only; it never exposes synchronous HTTP endpoints.
- The scaffold respects the repo plan and the conventions observed in the neighboring RentifyX services.
