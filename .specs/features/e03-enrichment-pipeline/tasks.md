# E-03 Enrichment Pipeline Tasks

**Design**: `.specs/features/e03-enrichment-pipeline/design.md`
**Status**: Approved

**Scope note**: this breakdown covers the C# implementation only (Lambda + Shared contract change). Terraform (`iac/modules/kafka-event-source-mapping`, `iac/modules/lambda-enrichment`, a new enrichment idempotency table module, a new enrichment failure-DLQ module, and extending `iac/modules/iam-roles`' enrichment policy with S3/DynamoDB/SQS/Bedrock permissions) is deliberately out of this tasks.md — same posture as E-02, where the Lambda code shipped before its Terraform wiring did, in a separate pass. Tracked as a follow-up once T1–T10 land.

---

## Gate Check Commands

| Gate | Command |
|---|---|
| quick (Shared) | `dotnet test tests/RentifyxAiServices.Shared.Tests/RentifyxAiServices.Shared.Tests.csproj` |
| quick (Moderation) | `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj` |
| quick (Enrichment) | `dotnet test tests/RentifyxAiServices.Enrichment.Tests/RentifyxAiServices.Enrichment.Tests.csproj` |
| full | `dotnet test RentifyxAiServices.slnx --configuration Release` |
| build | `dotnet build RentifyxAiServices.slnx --configuration Release` |

---

## Test Coverage Matrix (formalizing TESTING.md's noted gap, per design's research)

| Code layer | Test type | Parallel-safe |
|---|---|---|
| Plain data records (events, result types) | none | Yes |
| Client wrapping an AWS SDK call with retry/backoff (`BedrockEnrichmentClient`) | unit | Yes |
| Event publisher (`KafkaEnrichmentEventPublisher`) | unit | Yes |
| Orchestrator (`EnrichmentService`) | unit | Yes |
| Lambda entrypoint (`EnrichmentHandler`) | unit | Yes |
| Full pipeline against LocalStack/Kafka via Testcontainers | integration | **No** (shared Docker containers, sequential by nature — same as E-02's `ModerationPipelineTests`) |

---

## Execution Plan

### Phase 1: Foundation (Sequential)

```
T1
```

### Phase 2: Contracts + Scaffold (Parallel OK)

```
      ┌→ T2 [P]
T1 ───┼→ T3 [P]
      └→ T4 [P]
```

(T2/T3/T4 don't actually depend on T1's package bump, but are grouped here — same rationale E-02 used — because T5+ need all of T1–T4 done first.)

### Phase 3: Core Components (Parallel OK)

```
T1,T4 ──→ T5 [P] ──→ T6
T3,T4 ──────────────→ T7 [P]
```

### Phase 4: Orchestration (Sequential)

```
T2,T3,T6,T7 → T8
```

### Phase 5: Entrypoint (Sequential)

```
T1,T4,T8 → T9
```

### Phase 6: Integration (Sequential, final)

```
T2,T9 → T10
```

---

## Task Breakdown

### T1: Fix stale package pins, add Kafka event source package

**What**: Bump `AWSSDK.BedrockRuntime` `4.0.14.0` → `4.0.100.6` (current pin doesn't exist on NuGet — confirmed via flat-container API during Design). Add `Amazon.Lambda.KafkaEvents` `3.0.0` (new dependency, confirmed exists on NuGet during Design).
**Where**: `Directory.Packages.props`
**Depends on**: None
**Reuses**: None
**Requirement**: ENR-01 (enabling dependency)

**Tools**:
- MCP: NONE (versions already confirmed against the live NuGet flat-container API during Design — do not re-guess)
- Skill: NONE

**Done when**:
- [ ] `AWSSDK.BedrockRuntime` reads `4.0.100.6`
- [ ] `Amazon.Lambda.KafkaEvents` `3.0.0` added under the "Lambda" `ItemGroup`
- [ ] Gate check passes: `dotnet build RentifyxAiServices.slnx --configuration Release`

**Tests**: none (package manifest change)
**Gate**: build

---

### T2: `AssetMediaModerated` v2 — add Bucket/Key [P]

**What**: Add `string Bucket`, `string Key` to `AssetMediaModerated`; bump `SchemaVersion` default `1` → `2`. Update `ModerationService.ProcessAsync` to pass `bucket`/`key` (already in scope) into both the `AssetMediaModerated` and downstream construction sites. Update existing `ModerationServiceTests`/`KafkaModerationEventPublisherTests` call sites for the new required constructor args.
**Where**: `src/Shared/RentifyxAiServices.Shared/Events/AssetMediaModerated.cs`, `src/Functions/Moderation/RentifyxAiServices.Moderation/ModerationService.cs`, `tests/RentifyxAiServices.Moderation.Tests/ModerationServiceTests.cs`, `tests/RentifyxAiServices.Moderation.Tests/KafkaModerationEventPublisherTests.cs`
**Depends on**: None
**Reuses**: Existing `AssetMediaModerated` (additive change, per CLAUDE.md's "Shared contracts must stay versioned and additive-only")
**Requirement**: ENR-12

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `AssetMediaModerated` has `Bucket`/`Key` fields, `SchemaVersion` defaults to `2`
- [ ] `ModerationService.ProcessAsync` populates both fields from the triggering S3 record
- [ ] All pre-existing Moderation tests still pass with updated call sites (no test deleted to make this pass)
- [ ] Gate check passes: `dotnet test tests/RentifyxAiServices.Moderation.Tests/RentifyxAiServices.Moderation.Tests.csproj`
- [ ] Test count: 24 tests still pass (same count as before this task — this task adds no new test, it updates existing ones)

**Tests**: unit (existing suite, updated)
**Gate**: quick (Moderation)

---

### T3: `AssetEnrichmentSuggested` event contract [P]

**What**: New record `AssetEnrichmentSuggested(Guid AssetId, string Description, IReadOnlyList<string> Tags, DateTimeOffset Timestamp, int SchemaVersion = 1)`.
**Where**: `src/Shared/RentifyxAiServices.Shared/Events/AssetEnrichmentSuggested.cs`
**Depends on**: None
**Reuses**: Same shape convention as `AssetMediaModerated`/`AssetPendingManualReview`
**Requirement**: ENR-03

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] Record matches design.md's Data Models section exactly
- [ ] Gate check passes: `dotnet build RentifyxAiServices.slnx --configuration Release`

**Tests**: none (plain data record, no branching logic)
**Gate**: build

---

### T4: Scaffold Enrichment project dependencies [P]

**What**: Replace the placeholder `Class1.cs` scaffold with real package references: `AWSSDK.BedrockRuntime`, `AWSSDK.S3`, `AWSSDK.SQS`, `AWSSDK.DynamoDBv2`, `Amazon.Lambda.Core`, `Amazon.Lambda.KafkaEvents`, `Amazon.Lambda.Serialization.SystemTextJson`, `Confluent.Kafka`, `Microsoft.Extensions.Logging.Abstractions`; `ProjectReference` to `RentifyxAiServices.Shared`; `InternalsVisibleTo` for `RentifyxAiServices.Enrichment.Tests` — mirrors `RentifyxAiServices.Moderation.csproj` exactly. Also update `tests/RentifyxAiServices.Enrichment.Tests/RentifyxAiServices.Enrichment.Tests.csproj` with the same package set as `Moderation.Tests.csproj` (`FluentAssertions`, `Moq`, `Amazon.Lambda.TestUtilities`) plus a `ProjectReference` to the Enrichment project, replacing its current placeholder-only setup. Delete `src/Functions/Enrichment/RentifyxAiServices.Enrichment/Class1.cs` and its matching `PlaceholderTests.cs` content (once T9's real handler test exists — don't delete the placeholder test file until there's a replacement, to avoid an empty test project failing discovery).
**Where**: `src/Functions/Enrichment/RentifyxAiServices.Enrichment/RentifyxAiServices.Enrichment.csproj`, `tests/RentifyxAiServices.Enrichment.Tests/RentifyxAiServices.Enrichment.Tests.csproj`
**Depends on**: None
**Reuses**: `RentifyxAiServices.Moderation.csproj`/`RentifyxAiServices.Moderation.Tests.csproj` as the template
**Requirement**: ENR-01 (enabling dependency)

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] Enrichment csproj package set matches design's dependency list
- [ ] `Class1.cs` removed
- [ ] Gate check passes: `dotnet build RentifyxAiServices.slnx --configuration Release`

**Tests**: none (project scaffolding)
**Gate**: build

---

### T5: `EnrichmentResult` + `IBedrockEnrichmentClient` interface [P]

**What**: `record EnrichmentResult(string? Description, IReadOnlyList<string> Tags, bool Succeeded, string? FailureReason)`; `interface IBedrockEnrichmentClient { Task<EnrichmentResult> GenerateAsync(byte[] imageBytes, CancellationToken cancellationToken = default); }`.
**Where**: `src/Functions/Enrichment/RentifyxAiServices.Enrichment/Bedrock/EnrichmentResult.cs`, `IBedrockEnrichmentClient.cs`
**Depends on**: T1, T4
**Reuses**: `ModerationScanResult`'s success/failure-reason shape
**Requirement**: ENR-01

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] Both types match design.md's Components section
- [ ] Gate check passes: `dotnet build RentifyxAiServices.slnx --configuration Release`

**Tests**: none (interface + plain record)
**Gate**: build

---

### T6: `BedrockEnrichmentClient` implementation

**What**: Implements `IBedrockEnrichmentClient` via `IAmazonBedrockRuntime.ConverseAsync`. System/instruction prompt built as a separate `Message` from the image `ContentBlock` (role separation, ENR-10); forces structured tool-based output for description+tags (ENR-11); caps `InferenceConfiguration.MaxTokens` to a fixed constant (ENR-08); retry/backoff on throttling mirroring `RekognitionModerationClient`'s shape, DLQ-worthy failure after retries exhausted (ENR-05); treats a tool-output schema mismatch as `Succeeded = false` (ENR-07, ENR-11).
**Where**: `src/Functions/Enrichment/RentifyxAiServices.Enrichment/Bedrock/BedrockEnrichmentClient.cs`
**Depends on**: T5
**Reuses**: `RekognitionModerationClient`'s retry-loop shape
**Requirement**: ENR-05, ENR-07, ENR-08, ENR-10, ENR-11

**Tools**:
- MCP: `context7` (confirm `ContentBlock.Image`/`ImageBlock` and `ToolConfig` exact field names via the restored `AWSSDK.BedrockRuntime` 4.0.100.6 package — design.md flagged these as NOT confirmed yet; if Context7 can't resolve them, fall back to the package's own model-file XML docs per CLAUDE.md's established convention, grep `name="T:Amazon.BedrockRuntime.Model.ImageBlock"` etc. Also confirm the Bedrock throttling exception type — design assumed `Amazon.BedrockRuntime.Model.ThrottlingException`, not verified)
- Skill: NONE

**Done when**:
- [ ] `GenerateAsync` returns `EnrichmentResult` with `Description`+`Tags` on a valid tool-use response
- [ ] Throttling triggers retry with backoff before failing; exhausted retries return `Succeeded = false` with `FailureReason` set
- [ ] A response that doesn't match the expected tool schema returns `Succeeded = false`, never a partially-parsed guess
- [ ] Request always sets a `MaxTokens` cap
- [ ] Unit tests cover: success mapping, throttle-then-succeed, throttle-exhausted-fails, schema-mismatch-fails
- [ ] Gate check passes: `dotnet test tests/RentifyxAiServices.Enrichment.Tests/RentifyxAiServices.Enrichment.Tests.csproj`
- [ ] Test count: at least 4 tests pass

**Tests**: unit
**Gate**: quick (Enrichment)

---

### T7: `IEnrichmentEventPublisher` / `KafkaEnrichmentEventPublisher` [P]

**What**: `interface IEnrichmentEventPublisher { Task PublishAsync(AssetEnrichmentSuggested suggestedEvent, CancellationToken cancellationToken = default); }` and its Kafka implementation, wrapping `KafkaEventPublisher<AssetEnrichmentSuggested>` — no SQS side-effect (Enrichment has no manual-review concept, unlike Moderation's `PendingReview` path).
**Where**: `src/Functions/Enrichment/RentifyxAiServices.Enrichment/Publishing/IEnrichmentEventPublisher.cs`, `KafkaEnrichmentEventPublisher.cs`
**Depends on**: T3, T4
**Reuses**: `KafkaModerationEventPublisher`'s shape (minus the SQS branch), `KafkaEventPublisher<T>` (Shared, already has the `JsonStringEnumConverter` fix from E-02)
**Requirement**: ENR-03

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `PublishAsync` delegates to `KafkaEventPublisher<AssetEnrichmentSuggested>.PublishAsync(assetId.ToString(), suggestedEvent, ...)`
- [ ] Unit test confirms the publisher is invoked with the correct key/event
- [ ] Gate check passes: `dotnet test tests/RentifyxAiServices.Enrichment.Tests/RentifyxAiServices.Enrichment.Tests.csproj`
- [ ] Test count: at least 1 test passes

**Tests**: unit
**Gate**: quick (Enrichment)

---

### T8: `EnrichmentService` orchestrator

**What**: `ProcessAsync(AssetMediaModerated moderatedEvent, CancellationToken)` — skip if `Verdict != Approved` (ENR-02); idempotency claim keyed by `AssetId` via `IIdempotencyStore` against the (separate, per your decision) enrichment idempotency table (ENR-04); fetch S3 object via `moderatedEvent.Bucket`/`Key` (ENR-01); call `IBedrockEnrichmentClient.GenerateAsync`; on success publish via `IEnrichmentEventPublisher` (ENR-03); on any failure (S3 not-found, Bedrock failure) route to the (separate, per your decision) enrichment failure DLQ via `IAmazonSQS` (ENR-05, ENR-06).
**Where**: `src/Functions/Enrichment/RentifyxAiServices.Enrichment/EnrichmentService.cs`
**Depends on**: T2, T3, T6, T7
**Reuses**: `ModerationService`'s structure almost line-for-line
**Requirement**: ENR-01, ENR-02, ENR-03, ENR-04, ENR-05, ENR-06

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `Rejected`/`PendingReview` verdicts skip without any S3/Bedrock/DynamoDB call
- [ ] Duplicate `AssetId` (idempotency already claimed) skips without any S3/Bedrock call
- [ ] Missing S3 object routes to failure DLQ, doesn't throw unhandled
- [ ] Bedrock failure (`Succeeded = false`) routes to failure DLQ, doesn't publish
- [ ] Successful path publishes exactly one `AssetEnrichmentSuggested`
- [ ] Unit tests cover all five branches above
- [ ] Gate check passes: `dotnet test tests/RentifyxAiServices.Enrichment.Tests/RentifyxAiServices.Enrichment.Tests.csproj`
- [ ] Test count: at least 5 tests pass

**Tests**: unit
**Gate**: quick (Enrichment)

---

### T9: `EnrichmentHandler` Lambda entrypoint

**What**: Thin entrypoint — `FunctionHandler(KafkaEvent kafkaEvent, ILambdaContext context)`, deserializes each Kafka record's base64 `Value` into `AssetMediaModerated`, delegates to `EnrichmentService.ProcessAsync` per record; malformed/empty event batches skip gracefully (mirrors `ModerationHandler`'s empty-S3Event handling). Composition root (`BuildService()`) wires real AWS clients from env vars, same shape as `ModerationHandler.BuildService()`.
**Where**: `src/Functions/Enrichment/RentifyxAiServices.Enrichment/EnrichmentHandler.cs`
**Depends on**: T1, T4, T8
**Reuses**: `ModerationHandler`'s thin-entrypoint + `BuildService()` shape
**Requirement**: ENR-01

**Tools**:
- MCP: `context7` (confirm exact `KafkaEvent`/`KafkaEventRecord` shape from `Amazon.Lambda.KafkaEvents` 3.0.0 — design.md did not confirm this beyond "base64 Value per record, keyed by topic-partition"; if Context7 can't resolve it, use the package's XML docs per the established convention)
- Skill: NONE

**Done when**:
- [ ] Empty/malformed Kafka event batch doesn't throw, logs a warning (mirrors `ModerationHandler`'s `s3Event?.Records is null` guard)
- [ ] Each record's `AssetMediaModerated` payload is deserialized and passed to `EnrichmentService.ProcessAsync`
- [ ] `PlaceholderTests.cs` replaced with real handler tests (T4 deferred this deletion until now)
- [ ] Unit tests cover: empty batch no-op, single-record delegation
- [ ] Gate check passes: `dotnet test tests/RentifyxAiServices.Enrichment.Tests/RentifyxAiServices.Enrichment.Tests.csproj`
- [ ] Test count: at least 2 tests pass

**Tests**: unit
**Gate**: quick (Enrichment)

**Commit**: `feat(enrichment): implement E-03 enrichment pipeline (Bedrock)`

---

### T10: Integration test against real Docker

**What**: `EnrichmentPipelineTests.cs` mirroring `ModerationPipelineTests.cs`'s shape — LocalStack S3 (upload a real test image) + Testcontainers Kafka (publish a real `AssetMediaModerated(Verdict=Approved, Bucket, Key)` event, consume the resulting `AssetEnrichmentSuggested`). Stubs Bedrock (no LocalStack Bedrock support — confirm this gap explicitly rather than assuming, same as E-02 had to work around LocalStack's S3/DynamoDB support but real Kafka).
**Where**: `tests/RentifyxAiServices.IntegrationTests/EnrichmentPipelineTests.cs`
**Depends on**: T2, T9
**Reuses**: `ModerationPipelineTests.cs`'s Testcontainers setup (LocalStack + Kafka containers, `AdminClient` topic pre-creation lesson from E-02)
**Requirement**: ENR-01, ENR-02, ENR-03, ENR-04

**Tools**:
- MCP: `context7` (check whether LocalStack's Pro/paid tier is needed for any Bedrock mocking, or confirm Bedrock must be stubbed via a test double instead — do not assume LocalStack supports Bedrock without checking)
- Skill: NONE

**Done when**:
- [ ] Approved event in → `AssetEnrichmentSuggested` out, verified end-to-end
- [ ] Duplicate event delivery does not double-invoke the (stubbed) Bedrock client
- [ ] `Rejected`/`PendingReview` events produce no `AssetEnrichmentSuggested`
- [ ] Gate check passes: `dotnet test RentifyxAiServices.slnx --configuration Release` with Docker running
- [ ] Test count: at least 3 new integration tests pass, existing `ModerationPipelineTests` (4) still pass

**Tests**: integration
**Gate**: full (requires Docker daemon)

---

## Parallel Execution Map

```
Phase 1 (Sequential):
  T1

Phase 2 (Parallel):
  T1 complete, then:
    ├── T2 [P]
    ├── T3 [P]
    └── T4 [P]

Phase 3 (Parallel):
  T1,T4 complete → T5 [P] → T6
  T3,T4 complete → T7 [P]

Phase 4 (Sequential):
  T2,T3,T6,T7 complete, then:
    T8

Phase 5 (Sequential):
  T1,T4,T8 complete, then:
    T9

Phase 6 (Sequential, final):
  T2,T9 complete, then:
    T10
```

---

## Task Granularity Check

| Task | Scope | Status |
|---|---|---|
| T1: Package pin fixes | 1 file (manifest) | ✅ Granular |
| T2: `AssetMediaModerated` v2 | 1 contract + 1 producer + 2 test files | ✅ Granular (cohesive — same change ripples through its own call sites) |
| T3: `AssetEnrichmentSuggested` | 1 record | ✅ Granular |
| T4: Enrichment project scaffold | 2 csproj files | ✅ Granular (cohesive — one dependency-wiring change) |
| T5: `EnrichmentResult` + interface | 2 files, no logic | ✅ Granular |
| T6: `BedrockEnrichmentClient` | 1 component | ✅ Granular |
| T7: `KafkaEnrichmentEventPublisher` | 1 component | ✅ Granular |
| T8: `EnrichmentService` | 1 component | ✅ Granular |
| T9: `EnrichmentHandler` | 1 component | ✅ Granular |
| T10: Integration test | 1 test file | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
|---|---|---|---|
| T1 | None | None (Phase 1 root) | ✅ Match |
| T2 | None | T1 → T2 (grouped, not a real dependency — same as E-02's T2-T7) | ✅ Match (documented as organizational, not blocking) |
| T3 | None | T1 → T3 (same organizational grouping) | ✅ Match |
| T4 | None | T1 → T4 (same organizational grouping) | ✅ Match |
| T5 | T1, T4 | T1,T4 → T5 | ✅ Match |
| T6 | T5 | T5 → T6 | ✅ Match |
| T7 | T3, T4 | T3,T4 → T7 | ✅ Match |
| T8 | T2, T3, T6, T7 | T2,T3,T6,T7 → T8 | ✅ Match |
| T9 | T1, T4, T8 | T1,T4,T8 → T9 | ✅ Match |
| T10 | T2, T9 | T2,T9 → T10 | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
|---|---|---|---|---|
| T1 | Package manifest | none | none | ✅ OK |
| T2 | Event contract + producer (existing tests updated) | unit (existing) | unit | ✅ OK |
| T3 | Plain data record | none | none | ✅ OK |
| T4 | Project scaffolding | none | none | ✅ OK |
| T5 | Plain record + interface | none | none | ✅ OK |
| T6 | Client wrapping AWS SDK call w/ retry | unit | unit | ✅ OK |
| T7 | Event publisher | unit | unit | ✅ OK |
| T8 | Orchestrator | unit | unit | ✅ OK |
| T9 | Lambda entrypoint | unit | unit | ✅ OK |
| T10 | Full pipeline (LocalStack/Kafka) | integration | integration | ✅ OK |

All ✅ — no restructuring needed before presenting.
