# E-02 Moderation Pipeline (Rekognition) Specification

## Problem Statement

Every image uploaded for an asset must be screened before the asset can go live, so the marketplace never displays illegal or policy-violating content. This has to happen without slowing down the upload flow or coupling `rentifyx-ai-services`'s release cadence to `asset-registry-api`'s.

## Cross-Repo Reality Check (2026-07-22)

Verified directly against `rentifyx-asset-registry-api` source before writing this spec:

- `AssetCreated` (`Domain/Events/Asset/AssetCreated.cs`) carries `AssetId, OwnerId, CategoryId, OccurredAt` only — **no media, no S3 key**. Media is attached in a separate step (`AssetEntity.AttachMedia` → `AssetMediaUploaded(AssetId, S3Key, OccurredAt)`), after the asset already exists in `Draft` status. This confirms the plan's original design is correct: moderation must trigger off the **raw S3 `ObjectCreated` event**, not off a Kafka `AssetCreated`/`AssetMediaUploaded` message — the Lambda would otherwise have to wait on Kafka delivery for no reason, and `rentifyx-ai-services` has no consumer wired for that topic anyway (event-only, ADR-AI-007, one-directional here: publish only).
- `IMediaStorageService.GeneratePresignedUploadUrlAsync(ownerId, assetId, mimeType, sizeBytes)` returns `PresignedUploadUrl(Url, S3Key)` — confirms the S3 key is scoped by owner+asset, but **the concrete implementation (`S3MediaStorageService`) does not exist yet** — it's an interface only, real S3 key format is not fixed anywhere in code. `asset-registry-api`'s M4 (S3 bucket, DynamoDB, Kafka outbox) is still PLANNED, not built.
- **Consequence:** there is no real media bucket to attach an S3 trigger to yet. This spec proceeds on an assumed key convention (below) that must be confirmed with the `asset-registry-api` team before the S3 trigger (T6 in tasks) is wired against a real bucket. Everything else (Rekognition call, thresholds, event publish, DLQ) is independently buildable and testable against LocalStack now.

**Assumed S3 key convention (needs cross-repo confirmation):** `assets/{ownerId}/{assetId}/{filename}` — chosen because the owner+asset scoping is confirmed by the interface signature, but the exact prefix/delimiter is not.

## Goals

- [ ] Every image landing in the media bucket is scanned by Rekognition without manual intervention
- [ ] Confidence thresholds route obvious violations to auto-reject, ambiguous cases to manual review, clean images to auto-approve
- [ ] Verdict is published as a versioned Kafka event — `asset-registry-api` never polls for moderation status
- [ ] Failures (throttling, malformed image) degrade gracefully to a DLQ, never a silent drop

## Out of Scope

| Feature | Reason |
|---|---|
| Consuming `AssetCreated`/`AssetMediaUploaded` from Kafka | Not needed — moderation triggers off the S3 event directly (see Reality Check above) |
| Real S3 bucket / Terraform `s3-trigger` module wiring against production | `asset-registry-api` M4 (bucket) not built yet; this spec delivers the Lambda + LocalStack-testable pipeline, wiring to the real bucket happens once the bucket exists |
| Manual review UI / admin endpoint to act on `AssetPendingManualReview` | Owned by `asset-registry-api`'s future `AdminReviewAsset` (its M3 Moderation Workflow, PLANNED) |
| Duplicate/fraud detection | DEF-AI-001, Phase 2 |

---

## User Stories

### P1: S3-triggered scan ⭐ MVP

**User Story**: As a dev, I want new media uploads scanned automatically so no image goes live unmoderated.

**Why P1**: This is the entire point of E-02 — without it nothing downstream has meaning.

**Acceptance Criteria**:

1. WHEN an object is created in the media bucket under the asset image prefix THEN the moderation Lambda SHALL invoke `rekognition:DetectModerationLabels` against it.
2. WHEN the same S3 object (same ETag) triggers the Lambda more than once THEN the Lambda SHALL skip re-scanning (idempotency via DynamoDB conditional write).
3. WHEN the S3 event payload is malformed or references a non-image key (e.g. a thumbnail/derivative outside the configured prefix/suffix filter) THEN the Lambda SHALL skip processing without throwing an unhandled exception.

**Independent Test**: Drop a file into a LocalStack S3 bucket, confirm the handler invokes Rekognition exactly once and skips a duplicate ETag on replay.

---

### P1: Confidence-based verdict ⭐ MVP

**User Story**: As a dev, I want clear confidence thresholds so obvious violations are auto-rejected without a human in the loop.

**Why P1**: Manual review of every image doesn't scale; thresholds are what make this async pipeline viable.

**Acceptance Criteria**:

1. WHEN Rekognition returns moderation labels with confidence ≥ 90% THEN the verdict SHALL be `Rejected`.
2. WHEN confidence is between 60% and 90% (inclusive of the boundaries per ADR-AI-003, to be written alongside this task) THEN the verdict SHALL be `PendingReview`.
3. WHEN confidence is < 60% THEN the verdict SHALL be `Approved`.
4. WHEN Rekognition returns no labels at all THEN the verdict SHALL be `Approved` (nothing detected).

**Independent Test**: Unit tests feeding synthetic Rekognition responses at 59%, 60%, 90%, 90.1%, and no-labels — assert the exact verdict at each boundary.

---

### P1: Verdict published as event ⭐ MVP

**User Story**: As `asset-registry-api`, I want the moderation verdict delivered as a Kafka event so I never poll for status.

**Why P1**: Event-only integration is this repo's entire reason for existing (ADR-AI-007).

**Acceptance Criteria**:

1. WHEN a verdict is `Approved` or `Rejected` THEN the Lambda SHALL publish `AssetMediaModerated` (assetId, verdict, labels, confidence, timestamp) to Kafka.
2. WHEN a verdict is `PendingReview` THEN the Lambda SHALL additionally publish `AssetPendingManualReview` and enqueue the item to the SQS review queue with its metadata.
3. WHEN the Rekognition call fails after 3 retries (throttling, malformed image) THEN the message SHALL land in a DLQ instead of an event being published.

**Independent Test**: LocalStack S3 + a stubbed Rekognition response → assert the exact event schema and payload landed in the test Kafka topic.

---

## Edge Cases

- WHEN the S3 key doesn't match the assumed `assets/{ownerId}/{assetId}/{filename}` convention THEN the Lambda SHALL log and skip rather than crash — this convention is unconfirmed cross-repo (see Reality Check) and may need to change once `asset-registry-api` ships `S3MediaStorageService`.
- WHEN the review queue depth exceeds a threshold for over an hour THEN a CloudWatch alarm SHALL fire (SLA risk, per plan's US-007).
- WHEN Rekognition throttles (rate limit) THEN the Lambda SHALL retry with backoff before falling to the DLQ, not fail immediately.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
|---|---|---|---|
| MOD-01 | P1: S3-triggered scan | Done | T5, T9, T10 (unit-verified); T11 e2e code-complete, unverified — no Docker in this environment |
| MOD-02 | P1: Confidence-based verdict | Done | T4 (unit-verified, exact boundary cases) |
| MOD-03 | P1: Verdict published as event | Done | T1, T8, T9 (unit-verified); T11 e2e code-complete, unverified |
| MOD-04 | P1: Manual review queue + alarm | Done | T7 (terraform fmt-verified), T6 |

**Coverage:** 4 total, 4 mapped to tasks, 0 unmapped

---

## Success Criteria

- [ ] `dotnet test` — all Moderation unit tests pass (handler logic, threshold boundaries)
- [ ] LocalStack integration test: S3 object → Rekognition (stubbed) → Kafka event, verified end-to-end without AWS credentials
- [ ] ADR-AI-003 (thresholds) and ADR-AI-004 (hybrid moderation strategy) written and accepted
- [ ] S3 key convention assumption flagged to `asset-registry-api` team before the real `s3-trigger` Terraform module is wired to a production bucket
