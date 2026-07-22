# RentifyX AI Services — Project Plan

**Repo:** `rentifyx-ai-services`
**Stack:** .NET 10 (LTS) · AWS Lambda Managed Runtime · Native AOT · AWSSDK.Rekognition · AWSSDK.BedrockRuntime · Terraform · GitHub Actions · Amazon.Lambda.Tools · Powertools for AWS Lambda (.NET) · OpenTelemetry
**Estimate:** 6 Epics · ~15 days · ~60 tasks
**Scope:** Event-driven AI capabilities for the RentifyX platform — image moderation and content enrichment, triggered by S3 uploads and Kafka events, never by synchronous HTTP calls.
**Explicitly out of scope (v1):** Duplicate/fraud detection is scoped (event contract only) but not implemented — deferred to Phase 2 (DEF-AI-001).
**Depends on:** `asset-registry-api` (publishes `AssetCreated`, owns the media S3 bucket) · **Feeds back into:** `asset-registry-api` (consumes `AssetMediaModerated` / `AssetEnrichmentSuggested`)

---

## Why a Separate Repo

This service is intentionally isolated from `asset-registry-api`:

- **Different runtime lifecycle.** Lambda functions here are triggered by S3 events and Kafka topics, not deployed as a long-running API host. Coupling their deploy pipeline to `asset-registry-api`'s would create unnecessary release friction.
- **Reusable across future services.** The enrichment and (eventually) fraud-detection capabilities are not asset-specific in principle — `leasing-api` or `risk-api` may want similar AI capabilities later without depending on `asset-registry-api`'s codebase.
- **Event-only contract (ADR-AI-007).** No service calls this repo synchronously. It reacts to events and publishes events back. This keeps the coupling explicit and versioned rather than implicit and hidden inside a shared library.

---

## Repository Structure

```
rentifyx-ai-services/
├── src/
│   ├── Functions/
│   │   ├── Moderation/
│   │   │   ├── RentifyX.AiServices.Moderation.csproj
│   │   │   ├── Function.cs                    # entry point / handler
│   │   │   ├── ModerationHandler.cs            # core logic (testable, no Lambda types)
│   │   │   ├── Models/
│   │   │   │   ├── ModerationVerdict.cs
│   │   │   │   └── S3EventPayload.cs
│   │   │   └── aws-lambda-tools-defaults.json  # deploy config for this function
│   │   │
│   │   ├── Enrichment/
│   │   │   ├── RentifyX.AiServices.Enrichment.csproj
│   │   │   ├── Function.cs
│   │   │   ├── EnrichmentHandler.cs
│   │   │   ├── Prompts/
│   │   │   │   └── EnrichmentPromptTemplate.cs
│   │   │   ├── Models/
│   │   │   │   └── EnrichmentSuggestion.cs
│   │   │   └── aws-lambda-tools-defaults.json
│   │   │
│   │   └── Dedupe/                             # Phase 2 — scaffolded, not implemented
│   │       ├── RentifyX.AiServices.Dedupe.csproj
│   │       └── Function.cs                     # stub, throws NotImplementedException + DEF-AI-001 reference
│   │
│   ├── Shared/
│   │   ├── RentifyX.AiServices.Shared.csproj
│   │   ├── Aws/
│   │   │   ├── RekognitionClientFactory.cs
│   │   │   └── BedrockClientFactory.cs
│   │   ├── Events/                             # event contracts (mirrors E-05 in the plan)
│   │   │   ├── AssetCreated.cs                 # consumed
│   │   │   ├── AssetMediaModerated.cs          # published
│   │   │   ├── AssetPendingManualReview.cs     # published
│   │   │   ├── AssetEnrichmentSuggested.cs     # published
│   │   │   └── AssetDuplicateSuspected.cs      # schema only, Phase 2
│   │   ├── Kafka/
│   │   │   ├── IEventPublisher.cs
│   │   │   └── KafkaEventPublisher.cs
│   │   ├── Idempotency/
│   │   │   └── DynamoDbIdempotencyStore.cs
│   │   ├── ErrorHandling/
│   │   │   └── ErrorOr.cs                      # reused pattern from identity-api
│   │   └── Observability/
│   │       ├── OtelSetup.cs
│   │       └── MetricsPublisher.cs
│   │
│   └── Directory.Packages.props                # centralized NuGet versioning across all functions
│
├── tests/
│   ├── RentifyX.AiServices.Moderation.Tests/
│   │   ├── ModerationHandlerTests.cs
│   │   └── ThresholdBoundaryTests.cs
│   ├── RentifyX.AiServices.Enrichment.Tests/
│   │   └── EnrichmentHandlerTests.cs
│   ├── RentifyX.AiServices.Shared.Tests/
│   │   └── IdempotencyStoreTests.cs
│   └── RentifyX.AiServices.IntegrationTests/
│       ├── LocalStackFixture.cs
│       ├── ModerationPipelineTests.cs          # S3 → Rekognition → Kafka, end to end
│       └── EnrichmentPipelineTests.cs
│
├── infra/
│   ├── main.tf
│   ├── variables.tf
│   ├── outputs.tf                              # publishes to SSM /rentifyx/ai-services/*
│   └── modules/
│       ├── lambda-moderation/
│       ├── lambda-enrichment/
│       ├── iam-roles/                          # one role per function (ADR-AI-002)
│       ├── s3-trigger/
│       └── kafka-event-source-mapping/
│
├── docs/
│   ├── adr/
│   │   ├── ADR-AI-001-independent-deploy-native-aot.md
│   │   ├── ADR-AI-002-iam-isolation.md
│   │   ├── ADR-AI-003-confidence-thresholds.md
│   │   ├── ADR-AI-004-hybrid-moderation-strategy.md
│   │   ├── ADR-AI-005-enrichment-opt-in.md
│   │   ├── ADR-AI-006-dedupe-deferred.md
│   │   └── ADR-AI-007-event-only-service.md
│   └── RentifyX_AIServices_Plan.md             # this document
│
├── .github/
│   └── workflows/
│       └── ci-cd.yml                           # matrix build: one job per function
│
├── .editorconfig                               # reused from identity-api
├── Directory.Build.props                       # Nullable, TreatWarningsAsErrors
├── global.json                                 # pin .NET 10 SDK
├── RentifyX.AiServices.sln
└── README.md
```

**Design notes:**

1. **Thin `Function.cs`, fat `*Handler.cs`.** The Lambda handler itself only parses the incoming event and delegates to a pure logic class, testable without instantiating `Amazon.Lambda.Core` types. This keeps the same Clean Architecture philosophy as the other services, even without formal Domain/Application/Infrastructure layers.
2. **`Dedupe/` exists as a stub, not a gap.** Scaffolded but not implemented, with a direct reference to DEF-AI-001 in code — so the deferred decision stays visible rather than looking like something was forgotten.
3. **`Shared/Events/` is the versioned contract.** These files are the literal implementation of the schemas formalized under ADR-AI-007. If this ever needs to become a shared NuGet package consumed by `asset-registry-api`, it's already isolated for extraction.
4. **One `aws-lambda-tools-defaults.json` per function** — enables `Amazon.Lambda.Tools` to deploy each Lambda independently, consistent with ADR-AI-001.
5. **`infra/modules/` mirrors the granular IAM approach from ADR-AI-002** — one IAM role module per function, never a shared role.

---

## Epic Overview

| # | Epic | Days | Goal |
|---|------|------|------|
| E-01 | Project Foundation & Lambda Infrastructure | 1–3 | Multi-Lambda solution scaffold, CI/CD per function, least-privilege IAM per Lambda |
| E-02 | Image Moderation Pipeline (Rekognition) | 4–7 | Every uploaded asset image is scanned async and classified before going live |
| E-03 | AI Enrichment Pipeline (Bedrock) | 8–10 | Suggest title/description/tag improvements — assistive, never blocking |
| E-04 | Duplicate & Fraud Detection (Phase 2 — deferred) | 11–12 | Event contract scoped now to stay forward-compatible; implementation deferred |
| E-05 | Event Contracts & Cross-Service Integration | 13 | Formal, versioned contracts for all consumers |
| E-06 | Observability & Production Readiness | 14–15 | Cost visibility, alerting, v1.0.0 ship |

---

## E-01 · Project Foundation & Lambda Infrastructure (Day 1–3)

**Goal:** Repo scaffold, IaC baseline, IAM least-privilege, CI/CD for Lambda packaging.

### F-01 · Repo & Lambda Scaffold

**US-001** — As a dev, I want a clean multi-Lambda solution structure so each function is independently deployable
- [ ] T-001 Solution structure: `/src/Functions/{Moderation,Enrichment,Dedupe}`, `/src/Shared`, `/infra`, `/tests`
- [ ] T-002 Shared class library: AWSSDK clients, Serilog config, ErrorOr\<T\> result wrapper (reuse pattern from identity-api)
- [ ] T-003 Directory.Packages.props for centralized NuGet versioning across all functions
- [ ] T-004 Choose per-function deployment model: Native AOT executable vs. managed runtime zip (evaluate cold start vs. build complexity)
- [ ] T-005 Local dev: Amazon.Lambda.TestTool + LocalStack for offline S3/Rekognition/Bedrock emulation
- [ ] T-006 `.editorconfig` reused from identity-api template: CA5xxx security rules, nullable enforcement, TreatWarningsAsErrors

**US-002** — As a tech lead, I want CI/CD that builds, tests, and deploys each Lambda independently
- [ ] T-007 GitHub Actions: `dotnet build` → `dotnet test` → `dotnet lambda package` per function (matrix build)
- [ ] T-008 Coverage gate ≥80% per function (coverlet + ReportGenerator)
- [ ] T-009 OWASP dependency-check (NuGet vulnerability scan) + Trivy scan if container image path is used
- [ ] T-010 Deploy step: Amazon.Lambda.Tools (`dotnet lambda deploy-function`) per function, gated by branch protection
- [ ] T-011 Document ADR-AI-001: independent deploy per Lambda vs. monolithic package; Native AOT vs. managed runtime tradeoff

### F-02 · IAM, Secrets & Shared Infra

**US-003** — As a security engineer, I want least-privilege IAM per Lambda so a compromised function has minimal blast radius
- [ ] T-012 Terraform module: dedicated IAM role per Lambda (moderation, enrichment, dedupe)
- [ ] T-013 Moderation role: `rekognition:DetectModerationLabels` + `s3:GetObject` scoped to media bucket only
- [ ] T-014 Enrichment role: `bedrock:InvokeModel` scoped to specific model ARN only
- [ ] T-015 Secrets Manager: any third-party API keys (none expected for AWS-native AI, but wire ISecretsProvider-equivalent for future use)
- [ ] T-016 ADR-AI-002: Lambda IAM isolation strategy — one role per function, no shared execution role

---

## E-02 · Image Moderation Pipeline (Rekognition) (Day 4–7)

**Goal:** Every uploaded asset image is scanned async and classified before going live.

### F-03 · S3 Trigger & Rekognition Integration

**US-004** — As a dev, I want an S3 event trigger so new media is scanned automatically on upload
- [ ] T-017 S3 event notification: `ObjectCreated` on media bucket → moderation Lambda
- [ ] T-018 Filter by prefix/suffix so only asset image uploads trigger (not thumbnails/derivatives)
- [ ] T-019 Idempotency: DynamoDB conditional write keyed by S3 object ETag to avoid duplicate scans
- [ ] T-020 Unit tests: event parsing, malformed S3 event handling

**US-005** — As a dev, I want Rekognition moderation calls with clear confidence thresholds so obvious violations are auto-rejected
- [ ] T-021 Call `rekognition:DetectModerationLabels` against uploaded image
- [ ] T-022 Define thresholds: confidence ≥90% → auto-reject, 60–90% → manual review, <60% → auto-approve
- [ ] T-023 Map Rekognition label taxonomy → internal `ModerationVerdict` enum
- [ ] T-024 ADR-AI-003: confidence threshold rationale + false-positive/negative tradeoffs
- [ ] T-025 Unit tests: all threshold bands, edge cases at boundary values

**US-006** — As the asset-registry-api, I want moderation results published as events so I never poll for status
- [ ] T-026 Publish `AssetMediaModerated` event to Kafka (assetId, verdict, labels, confidence, timestamp)
- [ ] T-027 Define and version the event schema (JSON Schema or Avro) shared via a contracts package
- [ ] T-028 Dead-letter: failed Rekognition calls (throttling, malformed image) → SQS DLQ after 3 retries
- [ ] T-029 Integration test: LocalStack S3 + Rekognition stub → Kafka event produced end-to-end

### F-04 · Manual Review Fallback

**US-007** — As a moderator, I want mid-confidence items routed to a review queue instead of auto-decided
- [ ] T-030 SQS review queue: items in 60–90% confidence band land here with metadata
- [ ] T-031 Publish `AssetPendingManualReview` event (consumed by asset-registry-api or an admin UI)
- [ ] T-032 CloudWatch alarm: review queue depth > threshold for > 1h (SLA risk)
- [ ] T-033 ADR-AI-004: hybrid auto + manual moderation strategy (supersedes pure-manual-queue plan)

---

## E-03 · AI Enrichment Pipeline (Bedrock) (Day 8–10)

**Goal:** Suggest title/description/tag improvements from asset photos + owner input — assistive, never blocking.

### F-05 · Content Enrichment via Bedrock

**US-008** — As a dev, I want to consume AssetCreated events so enrichment runs async without slowing down asset creation
- [ ] T-034 Lambda event source mapping: MSK/Kafka topic `AssetCreated` → enrichment Lambda
- [ ] T-035 Batch size + parallelization config tuned for Bedrock rate limits
- [ ] T-036 Idempotency: skip re-enrichment if `AssetEnrichmentSuggested` already emitted for this assetId

**US-009** — As an owner, I want AI-suggested titles/descriptions/tags so my listing is more complete without extra effort
- [ ] T-037 Prompt template: asset photos (via Bedrock multimodal) + owner-provided text → suggested title/description/tags
- [ ] T-038 Structured output: force JSON response format, validate schema before publishing
- [ ] T-039 Cost guardrail: max tokens per invocation, model selection (Haiku-class for cost, escalate only if needed)
- [ ] T-040 Unit tests: malformed model output, timeout, safety refusal handling

**US-010** — As the asset-registry-api, I want enrichment suggestions delivered as an event so the owner can accept/reject them
- [ ] T-041 Publish `AssetEnrichmentSuggested` event (assetId, suggestedTitle, suggestedDescription, suggestedTags)
- [ ] T-042 Suggestions are additive only — never overwrite owner data automatically
- [ ] T-043 ADR-AI-005: enrichment is assistive/opt-in, never auto-applied — LGPD + trust rationale
- [ ] T-044 Integration test: event consumed → suggestion produced → schema-valid event published

---

## E-04 · Duplicate & Fraud Detection — Phase 2 (Day 11–12)

**Goal:** Detect duplicate/stolen listings via image similarity — deferred to post-v1, scoped now to avoid rework later.

### F-06 · Image Similarity Groundwork

**US-011** — As a dev, I want the duplicate-detection contract defined now so v1 events are forward-compatible
- [ ] T-045 Define `AssetDuplicateSuspected` event schema (assetId, candidateAssetId, similarityScore) — schema only, no implementation
- [ ] T-046 Evaluate Rekognition image similarity vs. Bedrock embeddings + vector search (cost/accuracy tradeoff)
- [ ] T-047 **DEF-AI-001**: full duplicate-detection Lambda implementation deferred to Phase 2
- [ ] T-048 ADR-AI-006: rationale for deferring dedupe — moderation + enrichment ship first, dedupe needs catalog volume to be useful

---

## E-05 · Event Contracts & Cross-Service Integration (Day 13)

**Goal:** Formal, versioned contracts so asset-registry-api (and future consumers) integrate without guesswork.

### F-07 · Shared Event Contracts

**US-012** — As a dev on any service, I want a versioned schema registry so event contracts don't silently break consumers
- [ ] T-049 Create shared contracts package/repo reference: `AssetCreated` (consumed), `AssetMediaModerated`, `AssetPendingManualReview`, `AssetEnrichmentSuggested`, `AssetDuplicateSuspected` (schema only) (published)
- [ ] T-050 Schema versioning strategy: additive-only changes, major version bump = new topic
- [ ] T-051 ADR-AI-007: `rentifyx-ai-services` is event-only — no synchronous HTTP endpoints exposed to other services
- [ ] T-052 Consumer contract test against asset-registry-api's `AssetCreated` producer (schema compatibility check in CI)

---

## E-06 · Observability & Production Readiness (Day 14–15)

**Goal:** Cost visibility, failure alerting, and a confident v1.0.0 ship.

### F-08 · Observability, Cost Control & Ship Gate

**US-013** — As a dev, I want tracing and metrics across all Lambdas so failures are diagnosable in production
- [ ] T-053 Enable AWS X-Ray tracing on all three Lambdas
- [ ] T-054 Custom CloudWatch metrics: `moderation_verdicts_total{verdict}`, `enrichment_suggestions_total`, `ai_invocation_errors_total`
- [ ] T-055 Cost dashboard: Rekognition + Bedrock spend per day, alert if daily spend > budget threshold (AWS Budgets)
- [ ] T-056 Alarms: DLQ depth > 0 for 15min, Lambda error rate > 1%, Bedrock throttling rate

**US-014** — As a tech lead, I want a final review before shipping v1.0.0
- [ ] T-057 Terraform: finalize Lambda + IAM + S3 trigger + Kafka event source mapping modules
- [ ] T-058 Verify: no PII sent to Bedrock beyond what's in asset description/photos (LGPD data minimization check)
- [ ] T-059 Load test: simulate burst of 500 AssetCreated events, confirm no throttling cascades
- [ ] T-060 Finalize ADRs AI-001 through AI-007, cross-link with asset-registry-api ADRs
- [ ] T-061 Tag v1.0.0 → deploy Lambda packages → enable triggers in prod

---

## Known Decisions & Watch Items

| ID | Decision |
|----|----------|
| ADR-AI-001 | Independent deploy per Lambda; Native AOT vs. managed runtime evaluated per function |
| ADR-AI-002 | One IAM role per Lambda — no shared execution role |
| ADR-AI-003 | Rekognition confidence thresholds: ≥90% auto-reject, 60–90% manual review, <60% auto-approve |
| ADR-AI-004 | Hybrid auto + manual moderation strategy (not pure manual queue) |
| ADR-AI-005 | Bedrock enrichment is assistive/opt-in — suggestions never auto-overwrite owner data |
| ADR-AI-006 | Duplicate/fraud detection deferred to Phase 2 (DEF-AI-001); contract scoped now, implementation later |
| ADR-AI-007 | Service is event-only — no synchronous HTTP endpoints exposed to any consumer |

**Watch item:** Rekognition confidence thresholds (90%/60%) are a reasonable starting point, not validated against real data yet. Revisit once production volume provides false-positive/negative signal.

**Watch item:** Bedrock model selection defaults to a Haiku-class model for cost control. If enrichment quality proves insufficient, escalate to a larger model — this should be a measured decision based on real suggestion quality, not assumed upfront.

**Open dependency:** This service cannot be meaningfully tested end-to-end until `asset-registry-api` publishes real `AssetCreated` events and the media S3 bucket exists. Build order: `asset-registry-api` (domain + use cases + AssetCreated publishing) → `rentifyx-ai-services` → back to `asset-registry-api` (to close the moderation loop before shipping its own v1.0.0).

---

## Gap Analysis vs. Original Scope Note

- Duplicate/fraud detection (originally discussed as case use #3) is **explicitly scoped but not implemented** in this plan — the event contract (`AssetDuplicateSuspected`) exists so `asset-registry-api` and future consumers don't need a breaking schema change later, but the Lambda itself is DEF-AI-001, tracked for Phase 2.
- This service intentionally exposes **zero synchronous endpoints**. Any future requirement for a synchronous AI capability (e.g., "moderate this image right now, block until result") would be a deliberate architectural exception requiring its own ADR — not a default extension of this service.
- Cost control (model selection, token limits, AWS Budgets alerting) is treated as a first-class concern from day one, not an afterthought — given that this is the platform's first pay-per-invocation AI workload.
