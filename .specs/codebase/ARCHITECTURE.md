# Architecture

This repo is designed around isolated Lambda functions backed by event contracts:

- `Moderation` (implemented, E-02) consumes S3 upload events and calls Rekognition. Flow: `ModerationHandler` (entrypoint, iterates `S3Event.Records`) → `ModerationService.ProcessAsync` per record → `AssetKeyConventionFilter.Matches` (skip non-asset keys) → `DynamoDbIdempotencyStore.TryMarkProcessedAsync` (skip duplicate `{bucket}/{key}#{ETag}`) → `RekognitionModerationClient.ScanAsync` (retry/backoff on throttling, immediate fail on malformed image, non-2xx paths route to the failure DLQ) → `ThresholdEvaluator.Evaluate` (confidence → `Verdict`, ADR-AI-003) → `KafkaModerationEventPublisher` (Approved/Rejected → Kafka only; PendingReview → Kafka + SQS review queue, ADR-AI-004).
- `Enrichment` (implemented, E-03) is Kafka-triggered — consumes `AssetMediaModerated` (v2) via AWS Lambda's Kafka event source mapping for self-managed Kafka, not an S3 event. Flow: `EnrichmentHandler` (entrypoint, iterates `KafkaEvent.Records`) → `EnrichmentService.ProcessAsync` per record → skip if `Verdict != Approved` → `DynamoDbIdempotencyStore.TryMarkProcessedAsync` (keyed `enrichment:{assetId}`, separate table from Moderation's) → S3 `GetObjectAsync` (bucket/key from the event) → `BedrockEnrichmentClient.GenerateAsync` (Bedrock Converse API, Claude Sonnet 5, tool-forced structured output — ADR-AI-005/006) → `KafkaEnrichmentEventPublisher` publishes `AssetEnrichmentSuggested`, or failure (missing S3 object, Bedrock failure/schema mismatch) routes to a dedicated Enrichment failure DLQ.
- `Dedupe` (implemented, DEF-AI-001, ADR-AI-007) is S3-triggered, same shape as Moderation. Flow: `DedupeHandler` (entrypoint) → `DedupeService.ProcessAsync` per record → `AssetKeyConventionFilter.Matches` (shared with Moderation) → `DynamoDbIdempotencyStore.TryMarkProcessedAsync` (own table, `dedupe-idempotency`) → S3 `GetObjectAsync` → `AverageHashCalculator.ComputeHash` (perceptual hash / aHash) → `DynamoDbImageHashStore.FindExistingAssetIdAsync` (own table, `dedupe-image-hashes`) → if the hash matches a *different* asset, `KafkaDedupeEventPublisher` publishes `AssetDuplicateDetected`; otherwise `RecordAsync` (first-seen-wins via `attribute_not_exists`) with no publish. S3/image-decode failures route to a dedicated Dedupe failure DLQ.
- `Shared` hosts the event contract layer (`Events/`: `Verdict`, `ModerationLabel`, `AssetMediaModerated` v2, `AssetPendingManualReview`, `AssetEnrichmentSuggested`, `AssetDuplicateDetected`), the idempotency store (`Idempotency/DynamoDbIdempotencyStore`), a generic Kafka publisher (`Kafka/IEventPublisher<T>` + `KafkaEventPublisher<T>`), and the S3 key-convention filter (`KeyConvention/IKeyConventionFilter` + `AssetKeyConventionFilter`, moved here from Moderation once Dedupe needed the identical filter) — all reused across Moderation, Enrichment, and Dedupe. C# namespace is `RentifyxAiServices.SharedKernel` (not `.Shared` — avoids CA1716, the physical folder/project name is still `Shared`).

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

## Data flow (Dedupe)

```
S3 ObjectCreated -> DedupeHandler -> DedupeService
  -> [key filter] -> [idempotency check] -> [S3 fetch] -> [perceptual hash]
  -> [hash lookup] -> different asset? Kafka publish AssetDuplicateDetected
                    : record hash (first-seen-wins), no publish
  -> failure DLQ if S3 fetch fails or the image can't be decoded
```

## Known open seam (resolved)

~~The S3 key convention (`assets/{ownerId}/{assetId}/{filename}`) that `AssetKeyConventionFilter` and `ModerationService.ExtractAssetId` depend on is an assumption, not a confirmed cross-repo contract~~ — confirmed cross-repo 2026-07-27 (G-001, closed, see `.specs/project/STATE.md`): `asset-registry-api`'s real `S3MediaStorageService.GeneratePresignedUploadUrlAsync` generates exactly this key shape. `Dedupe.ExtractAssetId` reuses the same convention (`key.Split('/')[2]`).
