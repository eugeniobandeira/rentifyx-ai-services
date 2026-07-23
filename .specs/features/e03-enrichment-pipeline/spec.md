# E-03 Enrichment Pipeline (Bedrock) Specification

## Problem Statement

Once an asset image is moderation-approved, `asset-registry-api` still needs a human to write the listing description and pick tags/categories by hand. This is slow and inconsistent across owners. Enrichment should auto-generate a structured description + tags from the approved image via Bedrock, published as an event `asset-registry-api` can apply (or a human can override), without introducing synchronous coupling between the two services.

## Cross-Repo / Cross-Contract Reality Check (2026-07-23)

Verified against this repo's own `src/Shared` before writing this spec:

- `AssetMediaModerated` (the event Enrichment consumes) currently carries only `AssetId, Verdict, Labels, TopConfidence, Timestamp, SchemaVersion` — **no S3 bucket/key**. `ModerationService.ProcessAsync` already has `bucket`/`key` in scope at the point it builds this event (from the triggering S3 record), so exposing them is free. Decision: extend `AssetMediaModerated` additively (`SchemaVersion` 1 → 2, new `Bucket`/`Key` fields) rather than have Enrichment resolve `AssetId` → S3 key via a synchronous call to `asset-registry-api` (would violate this repo's event-only architecture principle, CLAUDE.md). This is now part of this feature's scope (P1, see below) since Enrichment cannot function without it.
- No Bedrock model ID, `AWSSDK.BedrockRuntime` package version, or `InvokeModel` request/response shape has been confirmed yet — per this skill's Knowledge Verification Chain, none of that is asserted as fact in this spec. It's Design-phase research (Context7 / NuGet flat-container API / AWS docs), not guessed here.
- `asset-registry-api`'s own consumer for `AssetEnrichmentSuggested` doesn't exist yet either (mirrors E-02's situation with `AssetMediaModerated` at the time that spec was written) — this feature ships the publisher side; the consumer is that repo's own backlog item, out of scope here.

## Goals

- [ ] Every Moderation-approved asset image gets a Bedrock-generated description + structured tags, published as a versioned Kafka event
- [ ] Bedrock cost stays bounded and predictable (capped output tokens, no re-invocation on duplicate delivery)
- [ ] Prompt is hardened against injection from untrusted image content (treated as data, never as instructions)
- [ ] Failures (throttling, missing S3 object, malformed model output) degrade to a DLQ, never a silent drop or a bad publish

## Out of Scope

| Feature | Reason |
|---|---|
| Enriching `Rejected`/`PendingReview` assets | No point generating a listing description for content that isn't going live; only `Approved` verdicts trigger Enrichment |
| Multi-image aggregation per listing | v1 processes one image event at a time; combining multiple photos into one listing description is a later iteration |
| `asset-registry-api`'s consumer for `AssetEnrichmentSuggested` | Owned by that repo; this feature only publishes the event |
| Human override / edit UI for suggested description+tags | `asset-registry-api`'s concern, not this service's |
| Real Bedrock model selection lock-in | Confirmed during Design phase research, not guessed here |
| Duplicate/fraud detection | DEF-AI-001, separate deferred feature |

---

## User Stories

### P1: Kafka-triggered enrichment of approved assets ⭐ MVP

**User Story**: As `asset-registry-api`, I want a structured description + tags suggestion for every moderation-approved asset image, so listings can be created with less manual data entry.

**Why P1**: This is the entire point of E-03 — without it nothing downstream has meaning.

**Acceptance Criteria**:

1. WHEN the Enrichment Lambda consumes an `AssetMediaModerated` event with `Verdict = Approved` THEN it SHALL fetch the referenced S3 object (via the event's `Bucket`/`Key`, added in this feature) and invoke Bedrock with a structured prompt built from that image.
2. WHEN the Enrichment Lambda consumes an `AssetMediaModerated` event with `Verdict = Rejected` or `Verdict = PendingReview` THEN it SHALL skip processing (no Bedrock call, no cost).
3. WHEN Bedrock returns a successful structured response THEN the Lambda SHALL publish `AssetEnrichmentSuggested(AssetId, Description, Tags, Timestamp, SchemaVersion)` to Kafka.
4. WHEN the same `AssetMediaModerated` event is delivered more than once (at-least-once Kafka delivery) THEN the Lambda SHALL NOT invoke Bedrock a second time for the same `AssetId` (idempotency, same `DynamoDbIdempotencyStore` pattern as Moderation).

**Independent Test**: Publish an `AssetMediaModerated(Verdict=Approved)` event to a LocalStack/Testcontainers Kafka topic with a real image in a LocalStack S3 bucket; confirm the Lambda invokes Bedrock once and an `AssetEnrichmentSuggested` event lands on the output topic. Publish the same event again; confirm no second Bedrock call.

---

### P1: Graceful failure handling ⭐ MVP

**User Story**: As an operator, I want Bedrock failures and bad inputs to degrade to a DLQ instead of crashing the Lambda or publishing garbage, so I can see what needs attention without losing events.

**Why P1**: Async pipelines that silently drop or crash on bad input are worse than not having the feature — same reasoning as Moderation's failure DLQ (MOD-04).

**Acceptance Criteria**:

1. WHEN Bedrock throttles (rate-limited) THEN the Lambda SHALL retry with exponential backoff (same shape as `RekognitionModerationClient`'s retry loop), and SHALL route to a failure DLQ after retries are exhausted.
2. WHEN the S3 object referenced by the event no longer exists (deleted between Moderation and Enrichment) THEN the Lambda SHALL route to the failure DLQ with a clear reason, not throw an unhandled exception.
3. WHEN Bedrock's response doesn't parse into the expected structured schema (description + tags) THEN the Lambda SHALL treat it as a failure (DLQ), never publish malformed/partial data downstream.

**Independent Test**: Stub Bedrock to throttle N times then succeed — confirm retry + eventual success. Stub Bedrock to return malformed JSON — confirm DLQ routing, no `AssetEnrichmentSuggested` publish.

---

### P2: Bounded cost

**User Story**: As the person who pays the AWS bill, I want enrichment invocations bounded in size and frequency, so Bedrock cost stays predictable as asset volume grows.

**Why P2**: Not needed for a correctness demo, but CLAUDE.md already flags Bedrock cost as a first-class design constraint — this should ship alongside P1, not be deferred indefinitely.

**Acceptance Criteria**:

1. WHEN building the Bedrock request THEN the Lambda SHALL cap `max_tokens` (or equivalent) to a fixed, configurable budget.
2. WHEN an asset has already been successfully enriched (idempotency key claimed) THEN the Lambda SHALL NOT re-invoke Bedrock for it, even under replay.

**Independent Test**: Assert the Bedrock request payload always carries a token cap; assert a claimed idempotency key short-circuits before any Bedrock call.

---

### P3: Prompt-injection hardening

**User Story**: As a security-conscious operator, I want the enrichment prompt structured so a malicious image (e.g. text overlay saying "ignore previous instructions") can't hijack the model or leak system prompt content into the published event.

**Why P3**: Real risk given the image is user-uploaded and untrusted, but lower urgency than shipping the core pipeline — tracked explicitly so it doesn't get silently dropped.

**Acceptance Criteria**:

1. WHEN constructing the Bedrock request THEN the Lambda SHALL keep the system/instruction prompt separate from the image content (role separation), never concatenate untrusted image-derived text into the instruction prompt.
2. WHEN the model output contains content that doesn't fit the expected description/tags schema (e.g. it echoes instructions, produces free-form prose where tags are expected) THEN the Lambda SHALL reject it via the same schema-validation failure path as P1's AC3, not publish it.

---

## Edge Cases

- WHEN the Bedrock model ARN/region isn't configured at cold start THEN the Lambda SHALL fail fast, same posture as `ModerationHandler`'s required env vars.
- WHEN the image is not visually decodable by the model (corrupt file, unsupported format) THEN the Lambda SHALL DLQ with a clear failure reason, not retry indefinitely.
- WHEN `Tags` comes back empty but `Description` is valid (or vice versa) THEN the Lambda SHALL still publish — partial-but-valid output is better than an all-or-nothing DLQ (needs Design-phase confirmation: what counts as "valid enough" to publish).

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
|---|---|---|---|
| ENR-01 | P1: Kafka-triggered enrichment | Design | Pending |
| ENR-02 | P1: Kafka-triggered enrichment (skip non-Approved) | Design | Pending |
| ENR-03 | P1: Kafka-triggered enrichment (publish AssetEnrichmentSuggested) | Design | Pending |
| ENR-04 | P1: Kafka-triggered enrichment (idempotency) | Design | Pending |
| ENR-05 | P1: Graceful failure handling (retry/backoff + DLQ) | Design | Pending |
| ENR-06 | P1: Graceful failure handling (missing S3 object) | Design | Pending |
| ENR-07 | P1: Graceful failure handling (schema validation) | Design | Pending |
| ENR-08 | P2: Bounded cost (token cap) | Design | Pending |
| ENR-09 | P2: Bounded cost (no re-invocation) | Design | Pending |
| ENR-10 | P3: Prompt-injection hardening (role separation) | Design | Pending |
| ENR-11 | P3: Prompt-injection hardening (schema rejection) | Design | Pending |
| ENR-12 | Contract change: `AssetMediaModerated` v2 (`Bucket`/`Key`) | Design | Pending |

**ID format:** `ENR-NN`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 12 total, 0 mapped to tasks yet, 12 unmapped ⚠️ (expected at end of Specify — Tasks phase maps them)

---

## Success Criteria

- [ ] `dotnet test` green for a new `RentifyxAiServices.Enrichment.Tests` suite covering ENR-01 through ENR-11
- [ ] LocalStack/Testcontainers integration test (mirroring `ModerationPipelineTests.cs`) verified against real Docker: Approved event in → `AssetEnrichmentSuggested` out, duplicate delivery does not double-invoke Bedrock
- [ ] `AssetMediaModerated` v2 change doesn't break existing Moderation tests (additive-only, `SchemaVersion` bump per CLAUDE.md convention)
- [ ] `docs/adr/ADR-AI-005` (Bedrock model + prompt strategy) and `ADR-AI-006` (cost/safety guardrails) written and accepted, mirroring E-02's ADR-AI-003/004 pattern
