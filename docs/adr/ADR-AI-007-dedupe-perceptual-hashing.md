# ADR-AI-007: Dedupe via Average-Hash Perceptual Hashing

- **Date:** 2026-07-27
- **Status:** Accepted

## Context

`Dedupe` (DEF-AI-001) was scaffolded with an IAM role pre-scoped for `rekognition:CompareFaces`, but that action compares two photos of the *same face* — asset photos are of rental equipment/objects, never faces, so `CompareFaces` cannot detect duplicate listings and was never actually usable. A real approach was needed to detect when a user (accidentally or to game listing limits) re-uploads the same or a near-identical photo across multiple assets.

## Options Considered

- **A — Bedrock multimodal embeddings + vector similarity.** Most robust to genuine visual near-duplicates (recolored, cropped, watermarked), but adds a second Bedrock dependency (cost, latency, another ADR-AI-006-style safety surface) and a vector index/search component this repo has no infrastructure for yet.
- **B — Perceptual hash (average hash / aHash) + exact-match lookup.** Cheap, local, no external API call per image. Resize to 8x8 grayscale, threshold each pixel against the mean, pack into a 64-bit fingerprint. Visually identical or near-identical (same crop/compression) images collide on the same hash; a DynamoDB point lookup on the hash is the entire "search."
- **C — Document the gap, no implementation.** Leaves `DEF-AI-001` open indefinitely and the stale `CompareFaces` IAM grant uncorrected.

User was presented with A vs. B vs. "just document it" and chose **B** explicitly (simplicity over Bedrock-embedding sophistication).

## Decision

Option B. `AverageHashCalculator` computes a 64-bit aHash (`SixLabors.ImageSharp` 3.1.12 — pinned below 4.0.0, which requires a paid commercial license) and returns it as a 16-character hex string. `DedupeService` mirrors `ModerationService`'s orchestration shape: key-convention filter → idempotency check (S3 object/ETag, 7-day TTL) → S3 `GetObject` → compute hash → `DynamoDbImageHashStore.FindExistingAssetIdAsync`.

- If no existing mapping for that hash exists, or the mapping already points at *this same* asset (re-upload of the same asset's own photo), the hash is recorded (365-day TTL) and nothing is published — this is the first-seen case.
- If the mapping points at a *different* asset, `AssetDuplicateDetected` (`AssetId`, `DuplicateOfAssetId`, `ImageHash`, `Timestamp`) is published to Kafka and the hash record is left untouched (first-seen-wins; a later duplicate never overwrites the original owner).
- `DynamoDbImageHashStore.RecordAsync` uses `ConditionExpression = "attribute_not_exists(ImageHash)"` so a race between two concurrent first uploads of the same hash can't clobber each other — the loser's write fails silently (caught, not treated as an error) and the winner's mapping stands.
- S3 read failures or unparseable images (`AmazonS3Exception`, `SixLabors.ImageSharp.UnknownImageFormatException`) route to the dedupe failure DLQ, same pattern as Moderation/Enrichment.

The stale `rekognition:CompareFaces` IAM statement is removed. `iac/modules/iam-roles`'s `dedupe` policy document now grants: S3 `GetObject` on the media bucket, DynamoDB `GetItem`/`PutItem` on the new `dedupe-image-hashes` table and `PutItem` on `dedupe-idempotency`, and SQS `SendMessage` on a new `dedupe-failure-dlq` (added to `iac/modules/review-queue`, same shape as the moderation/enrichment failure DLQs already there). Both new DynamoDB tables reuse the existing generic `iac/modules/dynamodb-table` module — the idempotency table takes its default `IdempotencyKey` hash key, the hash table overrides it to `ImageHash`.

`iac/modules/lambda-dedupe` (the Lambda function resource + S3 trigger wiring) is intentionally not built yet — same "code before IaC" order Moderation and Enrichment both took.

## Consequences

- **Exact/near-exact match only.** aHash collides on identical or lightly-recompressed/cropped images, but will *not* catch a duplicate that's been resized to very different aspect ratios, rotated, or has substantially different content composited in. This is a known limitation of the chosen approach vs. Option A (embeddings), accepted explicitly by the user for simplicity.
- **No similarity threshold, only exact hash equality.** A more tolerant "near dupe" detector (Hamming distance between hashes below some threshold) is possible future work on top of the same 64-bit hash — not built now, since it would require a table scan or a specialized index instead of a single `GetItem`.
- **First-seen-wins is permanent per hash with no expiry override.** The 365-day TTL means a hash mapping naturally ages out and could theoretically be "re-claimed" by a different asset a year later — acceptable since a hash collision that far apart in time is very unlikely to be the same fraud pattern the feature exists to catch.
- Reusing `AssetKeyConventionFilter` across both Moderation and Dedupe required moving it out of the `Moderation` project into `Shared` (namespace `RentifyxAiServices.SharedKernel.KeyConvention`) — this does not violate ADR-AI-001/002 (independent deploy, IAM isolation are about the Lambda packages and roles, not about sharing a pure, side-effect-free filter class via the existing Shared library both functions already depend on).
