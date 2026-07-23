# Architecture

This repo is designed around isolated Lambda functions backed by event contracts:

- `Moderation` (implemented, E-02) consumes S3 upload events and calls Rekognition. Flow: `ModerationHandler` (entrypoint, iterates `S3Event.Records`) → `ModerationService.ProcessAsync` per record → `AssetKeyConventionFilter.Matches` (skip non-asset keys) → `DynamoDbIdempotencyStore.TryMarkProcessedAsync` (skip duplicate `{bucket}/{key}#{ETag}`) → `RekognitionModerationClient.ScanAsync` (retry/backoff on throttling, immediate fail on malformed image, non-2xx paths route to the failure DLQ) → `ThresholdEvaluator.Evaluate` (confidence → `Verdict`, ADR-AI-003) → `KafkaModerationEventPublisher` (Approved/Rejected → Kafka only; PendingReview → Kafka + SQS review queue, ADR-AI-004).
- `Enrichment` (implemented, E-03) is Kafka-triggered — consumes `AssetMediaModerated` (v2) via AWS Lambda's Kafka event source mapping for self-managed Kafka, not an S3 event. Flow: `EnrichmentHandler` (entrypoint, iterates `KafkaEvent.Records`) → `EnrichmentService.ProcessAsync` per record → skip if `Verdict != Approved` → `DynamoDbIdempotencyStore.TryMarkProcessedAsync` (keyed `enrichment:{assetId}`, separate table from Moderation's) → S3 `GetObjectAsync` (bucket/key from the event) → `BedrockEnrichmentClient.GenerateAsync` (Bedrock Converse API, Claude Sonnet 5, tool-forced structured output — ADR-AI-005/006) → `KafkaEnrichmentEventPublisher` publishes `AssetEnrichmentSuggested`, or failure (missing S3 object, Bedrock failure/schema mismatch) routes to a dedicated Enrichment failure DLQ.
- `Dedupe` is reserved as a deferred Phase 2 scaffold.
- `Shared` hosts the event contract layer (`Events/`: `Verdict`, `ModerationLabel`, `AssetMediaModerated` v2, `AssetPendingManualReview`, `AssetEnrichmentSuggested`), the idempotency store (`Idempotency/DynamoDbIdempotencyStore`), and a generic Kafka publisher (`Kafka/IEventPublisher<T>` + `KafkaEventPublisher<T>`) reused by both Moderation and Enrichment. C# namespace is `RentifyxAiServices.SharedKernel` (not `.Shared` — avoids CA1716, the physical folder/project name is still `Shared`).

## Data flow (Moderation)

```
S3 ObjectCreated -> ModerationHandler -> ModerationService
  -> [key filter] -> [idempotency check] -> [Rekognition scan]
  -> [threshold evaluation] -> [Kafka publish] (+ SQS enqueue if PendingReview)
  -> failure DLQ if Rekognition scan fails after retries
```

## Data flow (Enrichment)

```
Kafka AssetMediaModerated -> EnrichmentHandler -> EnrichmentService
  -> [verdict filter: Approved only] -> [idempotency check] -> [S3 fetch]
  -> [Bedrock Converse, tool-forced structured output] -> [Kafka publish AssetEnrichmentSuggested]
  -> failure DLQ if S3 object missing, Bedrock fails, or output fails schema validation
```

## Known open seam

The S3 key convention (`assets/{ownerId}/{assetId}/{filename}`) that `AssetKeyConventionFilter` and `ModerationService.ExtractAssetId` depend on is an assumption, not a confirmed cross-repo contract — `asset-registry-api`'s `S3MediaStorageService` (the component that would fix the real format) doesn't exist yet as of E-02. Deliberately isolated in `AssetKeyConventionFilter` as the one place to patch if the real format differs.
