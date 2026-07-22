# Architecture

This repo is designed around isolated Lambda functions backed by event contracts:

- `Moderation` consumes S3 upload events and calls Rekognition.
- `Enrichment` consumes asset events and invokes Bedrock through a structured prompt flow.
- `Dedupe` is reserved as a deferred Phase 2 scaffold.
- `Shared` hosts the event contract layer, AWS abstractions, observability, and idempotency helpers.
