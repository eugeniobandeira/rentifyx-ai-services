# Architecture

This repo is designed around isolated Lambda functions backed by event contracts:

- `Moderation` (implemented, E-02) consumes S3 upload events and calls Rekognition. Flow: `ModerationHandler` (entrypoint, iterates `S3Event.Records`) → `ModerationService.ProcessAsync` per record → `AssetKeyConventionFilter.Matches` (skip non-asset keys) → `DynamoDbIdempotencyStore.TryMarkProcessedAsync` (skip duplicate `{bucket}/{key}#{ETag}`) → `RekognitionModerationClient.ScanAsync` (retry/backoff on throttling, immediate fail on malformed image, non-2xx paths route to the failure DLQ) → `ThresholdEvaluator.Evaluate` (confidence → `Verdict`, ADR-AI-003) → `KafkaModerationEventPublisher` (Approved/Rejected → Kafka only; PendingReview → Kafka + SQS review queue, ADR-AI-004).
- `Enrichment` consumes asset events and invokes Bedrock through a structured prompt flow. Not yet implemented (E-03/E-04).
- `Dedupe` is reserved as a deferred Phase 2 scaffold.
- `Shared` hosts the event contract layer (`Events/`: `Verdict`, `ModerationLabel`, `AssetMediaModerated`, `AssetPendingManualReview`), the idempotency store (`Idempotency/DynamoDbIdempotencyStore`), and a generic Kafka publisher (`Kafka/IEventPublisher<T>` + `KafkaEventPublisher<T>`) intended for reuse by Enrichment.

## Data flow (Moderation)

```
S3 ObjectCreated -> ModerationHandler -> ModerationService
  -> [key filter] -> [idempotency check] -> [Rekognition scan]
  -> [threshold evaluation] -> [Kafka publish] (+ SQS enqueue if PendingReview)
  -> failure DLQ if Rekognition scan fails after retries
```

## Known open seam

The S3 key convention (`assets/{ownerId}/{assetId}/{filename}`) that `AssetKeyConventionFilter` and `ModerationService.ExtractAssetId` depend on is an assumption, not a confirmed cross-repo contract — `asset-registry-api`'s `S3MediaStorageService` (the component that would fix the real format) doesn't exist yet as of E-02. Deliberately isolated in `AssetKeyConventionFilter` as the one place to patch if the real format differs.
