# Enrichment Pipeline IaC Specification

## Problem Statement

E-03 shipped Enrichment's Lambda code (`EnrichmentHandler`/`EnrichmentService`/`BedrockEnrichmentClient`) but deliberately deferred its Terraform — same order Moderation (E-02) took. Today `iac/modules/lambda-enrichment` and `iac/modules/kafka-event-source-mapping` are empty directories, `iam-roles`' `enrichment` policy only has `bedrock:InvokeModel`, and no DynamoDB table or failure-DLQ resource exists for Enrichment anywhere in `iac/`. Without this, Enrichment cannot be deployed even once the media bucket / package path / Bedrock ARN gaps (tracked separately in STATE.md) are resolved.

## Goals

- [ ] `iac/modules/lambda-enrichment` — Enrichment Lambda function, VPC-attached to reach `rentifyx-platform`'s Kafka broker, mirrors `lambda-moderation`'s shape
- [ ] `iac/modules/kafka-event-source-mapping` — wires the Lambda to consume `AssetMediaModerated` from Kafka (self-managed Kafka ESM, not MSK)
- [ ] A DynamoDB table resource for Enrichment's idempotency store (none exists in `iac/` for either Lambda today)
- [ ] An SQS failure DLQ for Enrichment (mirrors `review-queue`'s bare `moderation_failure_dlq` shape)
- [ ] `iam-roles`' `enrichment` policy extended with S3 read, DynamoDB PutItem, SQS SendMessage (currently only `bedrock:InvokeModel`)
- [ ] Compose all of the above into `iac/terraform/` root config alongside the existing Moderation modules; `terraform validate` clean

## Out of Scope

| Feature | Reason |
| --- | --- |
| `terraform apply` / real deploy | Blocked on pre-existing gaps: media bucket, Lambda package path, Bedrock model ARN (tracked in STATE.md, not this feature's job to resolve) |
| Reworking Moderation's idempotency table gap | Moderation's `idempotency_table_name` stays a bare variable; only a shared/reusable DynamoDB table module is introduced here, Moderation adopting it is a separate follow-up |
| S3 key convention (G-001) | Still unconfirmed by `asset-registry-api`; doesn't block Enrichment's IaC (Enrichment reads `Bucket`/`Key` off the triggering Kafka event, not a static prefix filter) |
| Kafka topic/ACL provisioning | `rentifyx-platform` owns the broker; this feature only configures the Lambda's event source mapping against topics assumed to already exist |
| CloudWatch alarms on the enrichment DLQ | Not requested; `review-queue`'s alarm only watches the review queue, not its failure DLQ, so no precedent to mirror either |

---

## User Stories

### P1: Deploy the Enrichment Lambda ⭐ MVP

**User Story**: As the platform operator, I want the Enrichment Lambda's compute, networking, and event source wired in Terraform so it can actually run once the remaining app-level gaps (bucket, package, model ARN) are filled in.

**Why P1**: Without this, E-03's code has no path to production regardless of what else is resolved.

**Acceptance Criteria**:

1. WHEN `iac/modules/lambda-enrichment` is applied THEN it SHALL create an `aws_lambda_function` using the `enrichment` role from `iam-roles`, VPC-attached the same way `lambda-moderation` is (public subnet, egress-only SG, `rentifyx-platform` remote state)
2. WHEN the function is created THEN its environment SHALL include `ENRICHMENT_IDEMPOTENCY_TABLE_NAME`, `ENRICHMENT_FAILURE_DLQ_URL`, `BEDROCK_REGION`, `KAFKA_ENRICHMENT_SUGGESTED_TOPIC`, `KAFKA_BOOTSTRAP_SERVERS` (exact names `EnrichmentHandler.BuildService()` reads)
3. WHEN `iac/modules/kafka-event-source-mapping` is applied THEN it SHALL create an `aws_lambda_event_source_mapping` of `self_managed_event_source` pointing at the Kafka broker's bootstrap servers, topic = the Moderation-published `AssetMediaModerated` topic, targeting the Enrichment Lambda's ARN
4. WHEN both modules exist THEN `iac/terraform/` root config SHALL compose them together with the other modules the same way `lambda-moderation`/`s3-trigger` are composed today

**Independent Test**: `terraform validate` passes clean across both new modules and the updated root config; `terraform plan` (no apply) shows the expected resource set with no unresolved variable errors beyond the pre-existing, documented ones (bucket/package/ARN).

---

### P2: Enrichment idempotency table and failure DLQ exist as real resources

**User Story**: As the platform operator, I want a real DynamoDB table and SQS queue provisioned for Enrichment instead of bare Terraform input variables with no backing resource, so the module set is actually deployable end to end.

**Why P2**: P1 can technically `validate` by accepting these as plain variables (as `lambda-moderation` already does for Moderation's table) — but E-03's own STATE.md gap explicitly calls out that *no* DynamoDB table resource exists anywhere yet, and this is the natural place to close it for Enrichment.

**Acceptance Criteria**:

1. WHEN the new DynamoDB table module is applied THEN it SHALL create a table with partition key `IdempotencyKey` (String), TTL enabled on attribute `ExpiresAt`, matching `DynamoDbIdempotencyStore`'s actual read/write shape
2. WHEN the new failure DLQ module (or an addition to an existing SQS-owning module) is applied THEN it SHALL create a plain `aws_sqs_queue` (no redrive policy — mirrors `moderation_failure_dlq`'s existing shape, not `review`'s)
3. WHEN both exist THEN `lambda-enrichment`'s `idempotency_table_name`/`failure_dlq_url` variables SHALL be wired from these new resources' outputs in the root config, not left as free-floating unbacked variables

**Independent Test**: `terraform plan` on the root config shows the DynamoDB table and SQS queue as resources to create, and the Lambda's env var values reference their `.name`/`.url` attributes (traceable in the plan output), not hardcoded strings.

---

### P3: `iam-roles`' enrichment policy has real least-privilege permissions

**User Story**: As a security reviewer, I want Enrichment's IAM role scoped to exactly what its code calls, so it isn't running with only `bedrock:InvokeModel` while actually also touching S3/DynamoDB/SQS at runtime with no policy grant for them.

**Why P3**: Correctness gate, not new infrastructure — extends an existing module (`iam-roles`) rather than building something new, and only starts to matter for real once P1/P2's resources exist to scope the policy against.

**Acceptance Criteria**:

1. WHEN `iam-roles`' `enrichment` policy is updated THEN it SHALL add `s3:GetObject` scoped to the media bucket ARN (same object Moderation already reads), `dynamodb:PutItem` scoped to the new enrichment idempotency table ARN, and `sqs:SendMessage` scoped to the new enrichment failure DLQ ARN
2. WHEN the policy is evaluated THEN it SHALL NOT gain any Moderation- or Dedupe-scoped resource ARNs (ADR-AI-002, zero permission overlap)

**Independent Test**: `terraform validate` passes; a manual read of `data.aws_iam_policy_document.enrichment.json`'s statements shows exactly four actions (`bedrock:InvokeModel`, `s3:GetObject`, `dynamodb:PutItem`, `sqs:SendMessage`), each scoped to a specific resource ARN, no wildcards beyond what Bedrock already required.

---

## Edge Cases

- WHEN `rentifyx-platform`'s Kafka SSM parameter doesn't exist yet (broker not applied) THEN `lambda-enrichment` SHALL follow the same `try()` fallback to `""` that `lambda-moderation` already uses, not hard-fail the plan
- WHEN the DynamoDB table module is later reused by Moderation THEN its variables SHALL be generic enough (table name, partition key name/type, TTL attribute name) to not be Enrichment-specific in naming — deferred as a follow-up, not blocking this feature
- WHEN the Kafka event source mapping's consumed topic name changes THEN it SHALL be a plain Terraform variable (matching the `s3-trigger` module's precedent of not hardcoding conventions IaC doesn't own)

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| EIAC-01 | P1 | Design | Pending |
| EIAC-02 | P1 | Design | Pending |
| EIAC-03 | P1 | Design | Pending |
| EIAC-04 | P1 | Design | Pending |
| EIAC-05 | P2 | Design | Pending |
| EIAC-06 | P2 | Design | Pending |
| EIAC-07 | P2 | Design | Pending |
| EIAC-08 | P3 | Design | Pending |
| EIAC-09 | P3 | Design | Pending |

**Coverage:** 9 total, 0 mapped to tasks yet, 9 unmapped ⚠️ (Design phase not yet run)

---

## Success Criteria

- [ ] `terraform fmt -check` and `terraform validate` clean across all new/modified modules and the root config
- [ ] `terraform plan` against the root config shows only the pre-existing, documented blockers (media bucket, package path, Bedrock ARN) as unresolved — no new unresolved variables introduced by this feature
- [ ] `iam-roles`' `enrichment` policy has zero resource-ARN overlap with `moderation`/`dedupe` policies
