# RentifyX AI Services

Event-driven AI service for moderation and enrichment workloads in the RentifyX platform.

## Repository shape

This repository is intentionally organized as a multi-Lambda solution with isolated source, shared contracts, infrastructure, and test boundaries.

## Planned structure

- `src/Functions/Moderation` — image moderation Lambda
- `src/Functions/Enrichment` — enrichment Lambda
- `src/Functions/Dedupe` — deferred Phase 2 scaffold
- `src/Shared` — shared AWS clients, event contracts, Kafka publisher, idempotency, observability
- `tests` — unit, integration, and shared contract tests
- `infra` — Terraform modules and root deployment files
- `docs` — ADRs and repository planning material

## Notes

- The service is event-only; it never exposes synchronous HTTP endpoints.
- The scaffold respects the repo plan and the conventions observed in the neighboring RentifyX services.
