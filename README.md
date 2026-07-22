# RentifyX AI Services

Event-driven AI service for moderation and enrichment workloads in the RentifyX platform.

## Current status

Foundation (E-01) and the Moderation pipeline (E-02) are implemented and tested. Enrichment and Dedupe remain scaffolded but unimplemented.

## Repository shape

- `src` — runnable code and shared libraries
- `tests` — unit and integration coverage
- `iac` — Terraform and deployment assets
- `docs` — ADRs and repository planning material
- `.specs` — project and feature traceability

## Current scaffold

- `src/Functions/Moderation` — implemented: S3-triggered Rekognition moderation pipeline (idempotent, threshold-based verdicts, Kafka + SQS review-queue publishing). See ADR-AI-003/004.
- `src/Functions/Enrichment` — project skeleton only, not yet implemented.
- `src/Functions/Dedupe` — deferred Phase 2 scaffold.
- `src/Shared` — event contracts (`Events/`), idempotency store, generic Kafka event publisher.
- `iac` — `iam-roles` (per-Lambda IAM), `review-queue` (SQS + DLQ + CloudWatch alarm) implemented; `lambda-moderation`, `lambda-enrichment`, `s3-trigger`, `kafka-event-source-mapping` still empty.

## Verification

A real build was executed and succeeded:

```bash
dotnet build RentifyxAiServices.slnx
```

This confirms the current bootstrapped solution is in a buildable state.

## Decisions (ADRs)

- [ADR-AI-001](docs/adr/ADR-AI-001-independent-deploy-native-aot.md) — independent deploy per Lambda, managed runtime default
- [ADR-AI-002](docs/adr/ADR-AI-002-iam-isolation.md) — one IAM role per Lambda, no shared execution role
- [ADR-AI-003](docs/adr/ADR-AI-003-moderation-thresholds.md) — moderation confidence thresholds (60%/90%)
- [ADR-AI-004](docs/adr/ADR-AI-004-hybrid-moderation-strategy.md) — hybrid auto-moderation + manual review strategy

## Notes

- The service is event-only; it never exposes synchronous HTTP endpoints.
- The scaffold respects the repo plan and the conventions observed in the neighboring RentifyX services.
