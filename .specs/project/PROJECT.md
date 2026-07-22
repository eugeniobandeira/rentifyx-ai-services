# RentifyX AI Services

## Vision

Create an event-driven AI service that handles moderation and enrichment for RentifyX assets without exposing synchronous HTTP endpoints.

## Goals

- Keep each Lambda independently deployable and auditable.
- Prefer structured event contracts over direct service-to-service coupling.
- Preserve least-privilege AWS permissions and clear observability.
- Ship the first version with moderation and enrichment, while keeping dedupe explicitly deferred.
