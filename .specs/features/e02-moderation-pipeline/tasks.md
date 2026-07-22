# E-02 Moderation Pipeline Tasks

**Design**: `.specs/features/e02-moderation-pipeline/design.md`
**Status**: Done — all 12 tasks verified green (T11 verified 2026-07-22 with Docker running)

---

## Gate Check Commands (inferred from CLAUDE.md — no formal Gate Commands table in TESTING.md)

| Gate | Command |
|---|---|
| quick (Shared) | `dotnet test tests/RentifyxAiServices.Shared.Tests/RentifyxAiServices.Shared.Tests.csproj` |
| quick (Moderation) | `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj` |
| full | `dotnet test RentifyxAiServices.slnx --configuration Release` |
| terraform | `terraform fmt -check` (recursive) — `terraform validate` blocked in this sandbox per STATE.md, re-run where registry access exists |
| docs | none — ADR review only |

---

## Execution Plan

### Phase 1: Foundation (Sequential)

```
T1
```

### Phase 2: Core Components (Parallel OK)

```
        ┌→ T2  [P]
        ├→ T3  [P]
T1 ─────┼→ T4  [P]
        ├→ T5  [P]
        ├→ T6  [P]
        ├→ T7  [P]
        ├→ T8  [P]
        └→ T12 [P]
```

(T2, T3, T5, T6, T7 depend on nothing — grouped in this phase only because they can run alongside T4/T8/T12, not because they need T1.)

### Phase 3: Orchestration (Sequential)

```
T2,T3,T4,T5,T8 → T9 → T10
```

### Phase 4: Integration (Sequential, final)

```
T10 → T11
```

---

## Task Breakdown

### T1: Define Shared moderation event contracts

**What**: Create `Verdict` enum, `ModerationLabel` record, `AssetMediaModerated` record, `AssetPendingManualReview` record in Shared.Events, per design's Data Models section.
**Where**: `src/Shared/RentifyxAiServices.Shared/Events/Verdict.cs`, `ModerationLabel.cs`, `AssetMediaModerated.cs`, `AssetPendingManualReview.cs`
**Depends on**: None
**Reuses**: None (first real Shared types)
**Requirement**: MOD-03

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [x] All four types defined exactly matching design.md's Data Models section, including `SchemaVersion = 1`
- [x] Solution builds clean: `dotnet build RentifyxAiServices.slnx --configuration Release`

**Tests**: none (plain data records, no branching logic — matches TESTING.md's "handler logic and threshold boundary" scope, not applicable here)
**Gate**: build

---

### T2: DynamoDB idempotency store [P]

**What**: `IIdempotencyStore` interface + `DynamoDbIdempotencyStore` implementation — conditional `PutItem` keyed on `{bucket}/{key}#{ETag}` with TTL.
**Where**: `src/Shared/RentifyxAiServices.Shared/Idempotency/IIdempotencyStore.cs`, `DynamoDbIdempotencyStore.cs`
**Depends on**: None
**Reuses**: None
**Requirement**: MOD-01 (AC2 — skip re-scan on duplicate ETag)

**Tools**:
- MCP: `context7` (confirm current `IAmazonDynamoDB` conditional-write API shape before implementing)
- Skill: NONE

**Done when**:
- [x] `TryMarkProcessedAsync` returns `true` on first claim, `false` on duplicate key (conditional expression, not read-then-write)
- [x] Unit tests cover: first-seen success, duplicate-key rejection, TTL attribute set correctly
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.Shared.Tests/RentifyxAiServices.Shared.Tests.csproj`
- [x] Test count: at least 3 tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick

---

### T3: Rekognition moderation client [P]

**What**: `IRekognitionModerationClient` interface + `RekognitionModerationClient` implementation wrapping `DetectModerationLabelsAsync`, with retry/backoff on throttling.
**Where**: `src/Functions/Moderation/RentifyxAiServices.Moderation/RekognitionModerationClient.cs`
**Depends on**: None
**Reuses**: None
**Requirement**: MOD-01 (AC1), MOD-03 (AC3 — DLQ after retries exhausted)

**Tools**:
- MCP: `context7` (confirm retry approach — SDK-native retry config vs. Polly — do not fabricate per design's deliberately-deferred decision)
- Skill: NONE

**Done when**:
- [x] `ScanAsync` returns `ModerationScanResult` with labels+confidences on success
- [x] Throttling triggers retry with backoff before failing; exhausted retries return `Succeeded = false` with `FailureReason` set
- [x] Unit tests cover: success mapping, throttle-then-succeed, throttle-exhausted-fails, malformed-image failure
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj`
- [x] Test count: at least 4 tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick

---

### T4: Threshold evaluator [P]

**What**: `IThresholdEvaluator` interface + `ThresholdEvaluator` implementation — pure function mapping confidence to `Verdict` per MOD-02's exact boundaries (≥90% Rejected, 60–90% inclusive PendingReview, <60% Approved, no labels Approved).
**Where**: `src/Functions/Moderation/RentifyxAiServices.Moderation/ThresholdEvaluator.cs`
**Depends on**: T1 (uses `Verdict`, `ModerationLabel`)
**Reuses**: `Shared.Events.Verdict`, `Shared.Events.ModerationLabel`
**Requirement**: MOD-02

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [x] `Evaluate` returns correct `Verdict` at exactly 59%, 60%, 90%, 90.1%, and no-labels inputs (spec's Independent Test, verbatim)
- [x] Thresholds are named constants referencing ADR-AI-003 (written in T12)
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj`
- [x] Test count: 5 boundary tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick

---

### T5: Asset key convention filter [P]

**What**: `IKeyConventionFilter` interface + `AssetKeyConventionFilter` implementation — matches S3 keys against the assumed `assets/{ownerId}/{assetId}/{filename}` convention, isolated per design so it's a single patchable seam.
**Where**: `src/Functions/Moderation/RentifyxAiServices.Moderation/AssetKeyConventionFilter.cs`
**Depends on**: None
**Reuses**: None
**Requirement**: MOD-01 (AC3), spec Edge Cases (non-matching key skip)

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [x] `Matches` returns `true` for well-formed `assets/{guid}/{guid}/{filename}` keys, `false` for thumbnails/derivatives/malformed keys, never throws
- [x] Unit tests cover valid key, missing segment, thumbnail-suffix key, empty string
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj`
- [x] Test count: at least 4 tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick

---

### T6: IAM policy additions for moderation role [P]

**What**: Add `dynamodb:PutItem` (idempotency table), Kafka/MSK publish, and `sqs:SendMessage` (review queue + DLQ) statements to the existing `moderation` policy document — gap found during design (current `main.tf` only has Rekognition + S3 read).
**Where**: `iac/modules/iam-roles/main.tf` (modify `data.aws_iam_policy_document.moderation`), `variables.tf` (add ARN inputs: idempotency table, Kafka cluster/topic, review queue, DLQ)
**Depends on**: None
**Reuses**: Existing `moderation` policy document structure, existing `aws_iam_policy_document` pattern from `enrichment`/`dedupe`
**Requirement**: MOD-01 (idempotency write), MOD-03 (Kafka/SQS publish)

**Tools**:
- MCP: `context7` (confirm current MSK IAM auth action names — `kafka-cluster:Connect`, `kafka-cluster:WriteData`, etc. — do not fabricate action names)
- Skill: NONE

**Done when**:
- [x] New statements scoped to specific resource ARNs (variables), not `*`, except where AWS API disallows resource-level scoping (comment inline per ADR-AI-002 convention)
- [x] `terraform fmt -check` passes
- [x] No change to `enrichment`/`dedupe` policy documents (isolation preserved per ADR-AI-002)

**Tests**: none (Terraform, not dotnet — no unit test layer applies)
**Gate**: terraform

---

### T7: Review queue + CloudWatch alarm module [P]

**What**: New Terraform module provisioning the SQS manual-review queue, its DLQ, and a CloudWatch alarm on `ApproximateNumberOfMessagesVisible` exceeding threshold for 1 hour (spec's Edge Cases, MOD-04).
**Where**: `iac/modules/review-queue/main.tf`, `variables.tf`, `outputs.tf`
**Depends on**: None
**Reuses**: Repo's existing one-module-per-concern IaC convention (`iac/modules/iam-roles`)
**Requirement**: MOD-04

**Tools**:
- MCP: `context7` (confirm current `aws_cloudwatch_metric_alarm` resource shape)
- Skill: NONE

**Done when**:
- [x] SQS review queue + DLQ resources defined with redrive policy
- [x] CloudWatch alarm fires on queue depth > threshold for 1h (threshold as a variable, no magic number)
- [x] Outputs expose queue ARN/URL for T6's IAM policy to reference
- [x] `terraform fmt -check` passes

**Tests**: none (Terraform)
**Gate**: terraform

---

### T8: Kafka/SQS moderation event publisher [P]

**What**: `IModerationEventPublisher` interface + `KafkaModerationEventPublisher` implementation — publishes `AssetMediaModerated` (Approved/Rejected) or `AssetPendingManualReview` (+ SQS enqueue) per verdict branch.
**Where**: `src/Shared/RentifyxAiServices.Shared/Kafka/IEventPublisher.cs` (generic Kafka producer wrapper) + `src/Functions/Moderation/RentifyxAiServices.Moderation/KafkaModerationEventPublisher.cs` (moderation-specific SQS branch)
**Depends on**: T1 (publishes `AssetMediaModerated`/`AssetPendingManualReview`)
**Reuses**: `Shared.Events` types from T1
**Requirement**: MOD-03

**Tools**:
- MCP: `context7` (confirm Kafka client library choice — Confluent.Kafka vs. MSK IAM auth client — deliberately deferred in design, resolve here)
- Skill: NONE

**Done when**:
- [x] Approved/Rejected verdicts publish only `AssetMediaModerated` to Kafka
- [x] PendingReview verdict publishes `AssetPendingManualReview` to Kafka AND enqueues to SQS review queue
- [x] Unit tests cover all three verdict branches, asserting exact topic + payload shape
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.Shared.Tests/RentifyxAiServices.Shared.Tests.csproj` and `.../RentifyxAiServices.Moderation.Tests.csproj`
- [x] Test count: at least 3 tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick

---

### T9: Moderation service orchestrator

**What**: `ModerationService.ProcessAsync` — wires key filter → idempotency → Rekognition → threshold → publish/DLQ per design's sequence diagram, exactly.
**Where**: `src/Functions/Moderation/RentifyxAiServices.Moderation/ModerationService.cs`
**Depends on**: T2, T3, T4, T5, T8
**Reuses**: All Phase 2 components via constructor injection
**Requirement**: MOD-01, MOD-02, MOD-03

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [x] Non-matching key → skip, no exceptions, no downstream calls (verified via mock verification)
- [x] Duplicate ETag → skip, no Rekognition call
- [x] Rekognition failure after retries → DLQ send, no event published
- [x] Each verdict branch (Approved/Rejected/PendingReview) calls the publisher exactly once with the right event type
- [x] Unit tests cover all five branches above with mocked dependencies
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj`
- [x] Test count: at least 5 tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick

---

### T10: Moderation Lambda entrypoint

**What**: `ModerationHandler.FunctionHandler` — thin entrypoint deserializing `S3Event`, delegating each record to `ModerationService`, DI wiring for all Phase 2 dependencies. Replaces `Class1.cs`.
**Where**: `src/Functions/Moderation/RentifyxAiServices.Moderation/ModerationHandler.cs` (delete `Class1.cs`)
**Depends on**: T9
**Reuses**: `ModerationService` from T9
**Requirement**: MOD-01 (AC3 — malformed event doesn't throw)

**Tools**:
- MCP: `context7` (confirm current `Amazon.Lambda.S3Events` package API — types may have shifted)
- Skill: NONE

**Done when**:
- [x] Handler iterates `S3Event.Records`, delegates each to `ModerationService.ProcessAsync`
- [x] Malformed/empty event payload logs and returns without throwing
- [x] DI container registers all Phase 2 interfaces to their implementations
- [x] `Class1.cs` removed
- [x] Unit tests cover: valid event delegates correctly, malformed event doesn't throw
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj`
- [x] Test count: at least 2 tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(moderation): wire S3-triggered Rekognition moderation pipeline`

---

### T11: LocalStack end-to-end integration test

**What**: Drop a file into a LocalStack S3 bucket, stub Rekognition response, assert the handler invokes Rekognition exactly once, skips a duplicate ETag replay, and the exact event schema/payload lands in the test Kafka topic — per spec's Independent Tests for all three P1 stories.
**Where**: `tests/RentifyxAiServices.IntegrationTests/ModerationPipelineTests.cs` (actual project name — no dot before `Tests`, corrected from this task's original path assumption)
**Depends on**: T10
**Reuses**: `Testcontainers.LocalStack`, `Testcontainers.Kafka` (per CLAUDE.md conventions)
**Requirement**: MOD-01, MOD-02, MOD-03

**Tools**:
- MCP: `context7` (confirm current `Testcontainers.LocalStack`/`Testcontainers.Kafka` API)
- Skill: NONE

**Done when**:
- [x] Full pipeline runs against LocalStack S3 + test Kafka topic + stubbed Rekognition, no real AWS credentials — verified 2026-07-22 with Docker running
- [x] Duplicate ETag replay is asserted to skip re-scanning — verified
- [x] Exact `AssetMediaModerated`/`AssetPendingManualReview` payload asserted in the test topic — verified
- [x] Gate check passes: `dotnet test tests/RentifyxAiServices.IntegrationTests/RentifyxAiServices.IntegrationTests.csproj --filter "FullyQualifiedName~ModerationPipelineTests"` → 3/3 pass, ~1m24s
- [x] Test count: 3 integration tests pass (no silent deletions)

Fixed three real bugs surfaced only by running against a live Docker daemon (not environment flakiness): `Verdict` was serializing as its numeric ordinal instead of its name in Kafka payloads (`KafkaEventPublisher<T>` now uses `JsonStringEnumConverter`); the test's S3/DynamoDB clients set both `ServiceURL` and `RegionEndpoint`, which routed requests to real AWS instead of LocalStack (`AuthenticationRegion` fixes it); Kafka consumers subscribed before the topic existed (topics now pre-created via `AdminClient`).

**Tests**: integration
**Gate**: full

**Commit**: `test(moderation): add LocalStack end-to-end pipeline coverage`

---

### T12: ADR-AI-003 and ADR-AI-004 [P]

**What**: Write and accept `docs/adr/ADR-AI-003-moderation-thresholds.md` (60%/90% boundary rationale) and `docs/adr/ADR-AI-004-hybrid-moderation-strategy.md` (auto-approve/reject + manual review queue rationale), per spec's Success Criteria.
**Where**: `docs/adr/ADR-AI-003-moderation-thresholds.md`, `docs/adr/ADR-AI-004-hybrid-moderation-strategy.md`
**Depends on**: None
**Reuses**: `docs/adr/ADR-AI-001-*.md` / `ADR-AI-002-*.md` as format template
**Requirement**: Spec Success Criteria (ADR-AI-003, ADR-AI-004)

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [x] Both ADRs follow the existing ADR-AI-001/002 format (Context, Options Considered, Decision, Consequences)
- [x] ADR-AI-003 explicitly justifies the 60%/90% boundary values used in T4
- [x] Both marked Accepted

**Tests**: none (docs)
**Gate**: none

---

## Parallel Execution Map

```
Phase 1 (Sequential):
  T1

Phase 2 (Parallel, after T1):
    ├── T2  [P]  (no dep on T1, grouped here for scheduling)
    ├── T3  [P]  (no dep on T1)
    ├── T4  [P]  (needs T1)
    ├── T5  [P]  (no dep on T1)
    ├── T6  [P]  (no dep on T1)
    ├── T7  [P]  (no dep on T1)
    ├── T8  [P]  (needs T1)
    └── T12 [P]  (no dep on T1, docs)

Phase 3 (Sequential):
  T2,T3,T4,T5,T8 complete, then:
    T9 ──→ T10

Phase 4 (Sequential, final):
  T10 complete, then:
    T11
```

---

## Task Granularity Check

| Task | Scope | Status |
|---|---|---|
| T1: Shared event contracts | 4 files, 1 concept (event shapes) | ✅ Granular |
| T2: DynamoDB idempotency store | 1 interface + 1 impl | ✅ Granular |
| T3: Rekognition client | 1 interface + 1 impl | ✅ Granular |
| T4: Threshold evaluator | 1 pure function | ✅ Granular |
| T5: Key convention filter | 1 pure function | ✅ Granular |
| T6: IAM policy additions | 1 file, 1 concept (moderation policy) | ✅ Granular |
| T7: Review queue module | 3 files, 1 concept (queue+alarm) | ✅ Granular |
| T8: Event publisher | 2 files, 1 concept (publish routing) | ✅ Granular |
| T9: Orchestrator | 1 file, 1 concept (wiring) | ✅ Granular |
| T10: Lambda entrypoint | 1 file, 1 concept (entrypoint) | ✅ Granular |
| T11: Integration test | 1 file, 1 concept (e2e coverage) | ✅ Granular |
| T12: ADRs | 2 files, 1 concept (threshold+strategy rationale) | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
|---|---|---|---|
| T1 | None | (start of Phase 1) | ✅ Match |
| T2 | None | Phase 2, parallel | ✅ Match |
| T3 | None | Phase 2, parallel | ✅ Match |
| T4 | T1 | Phase 2, parallel, arrow from T1 | ✅ Match |
| T5 | None | Phase 2, parallel | ✅ Match |
| T6 | None | Phase 2, parallel | ✅ Match |
| T7 | None | Phase 2, parallel | ✅ Match |
| T8 | T1 | Phase 2, parallel, arrow from T1 | ✅ Match |
| T9 | T2, T3, T4, T5, T8 | Phase 3, arrows from T2/T3/T4/T5/T8 | ✅ Match |
| T10 | T9 | Phase 3, arrow from T9 | ✅ Match |
| T11 | T10 | Phase 4, arrow from T10 | ✅ Match |
| T12 | None | Phase 2, parallel | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
|---|---|---|---|---|
| T1 | Shared event contracts (data records) | none (no branching logic) | none | ✅ OK |
| T2 | Shared idempotency store | unit (per TESTING.md: handler-adjacent logic) | unit | ✅ OK |
| T3 | Rekognition client wrapper | unit | unit | ✅ OK |
| T4 | Threshold evaluator | unit (explicit "threshold boundary behavior" in TESTING.md) | unit | ✅ OK |
| T5 | Key convention filter | unit | unit | ✅ OK |
| T6 | Terraform IAM policy | none (no dotnet test layer) | none | ✅ OK |
| T7 | Terraform review queue module | none | none | ✅ OK |
| T8 | Kafka/SQS event publisher | unit | unit | ✅ OK |
| T9 | Moderation orchestrator ("handler logic" per TESTING.md) | unit | unit | ✅ OK |
| T10 | Lambda entrypoint | unit | unit | ✅ OK |
| T11 | Cross-component pipeline | integration (per TESTING.md: S3/Rekognition/Kafka via LocalStack) | integration | ✅ OK |
| T12 | ADR docs | none | none | ✅ OK |

All checks pass — no restructuring needed.

---

## Parallelism Note

TESTING.md has no formal Parallelism Assessment table (no coverage matrix with parallel-safety flags exists yet — greenfield gap, not a violation). All `[P]` tasks above write to distinct files with no shared mutable state (separate classes, separate Terraform files/modules), so parallel-safety is judged from file/dependency isolation alone, per the Tips section's fallback rule ("no tests → `[P]` depends only on code dependencies" extended here since the matrix itself doesn't exist).
