# Enrichment Pipeline IaC Tasks

**Design**: `.specs/features/e03b-enrichment-iac/design.md`
**Status**: Done — all 7 tasks (T1-T7) complete, gate checks clean, 2026-07-24

---

## Execution Plan

### Phase 1: Foundation (Parallel OK)

Independent new/extended modules with no dependency on each other.

```
T1 [P] (dynamodb-table)
T2 [P] (review-queue: enrichment_failure_dlq)
T3 [P] (iam-roles: enrichment policy statements)
```

### Phase 2: Lambda module (Sequential, needs Phase 1 outputs conceptually but only via root wiring)

```
T1, T2, T3 → T4 (lambda-enrichment)
```

### Phase 3: Event source mapping (Sequential, needs T4's VPC outputs)

```
T4 → T5 (kafka-event-source-mapping)
```

### Phase 4: Root composition + validation (Sequential)

```
T5 → T6 (root config wiring) → T7 (fmt/validate/plan sweep across everything)
```

---

## Task Breakdown

### T1: Create generic `dynamodb-table` module [P]

**What**: New Terraform module — single-partition-key DynamoDB table with optional TTL, `PAY_PER_REQUEST` billing.
**Where**: `iac/modules/dynamodb-table/main.tf`, `variables.tf`, `outputs.tf`
**Depends on**: None
**Reuses**: Nothing existing (first DynamoDB resource in this repo's `iac/`) — generic per design.md's Tech Decisions
**Requirement**: EIAC-05

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `variables.tf` defines `table_name`, `hash_key` (default `IdempotencyKey`), `hash_key_type` (default `S`), `ttl_attribute_name` (default `ExpiresAt`), `billing_mode` (default `PAY_PER_REQUEST`)
- [ ] `main.tf`'s `aws_dynamodb_table` sets `hash_key`, one `attribute` block for the hash key, and a `ttl` block (`attribute_name = var.ttl_attribute_name`, `enabled = true`)
- [ ] `outputs.tf` exposes `table_name`, `table_arn`
- [ ] Gate check passes: `terraform fmt -check` and `terraform init && terraform validate` (run inside `iac/modules/dynamodb-table`)

**Tests**: none (Terraform module — no unit test layer in this repo's TESTING.md; `validate` is the gate)
**Gate**: `terraform fmt -check` (recursive) + `terraform validate`

**Verify**:
```
cd iac/modules/dynamodb-table && terraform fmt -check && terraform init -backend=false && terraform validate
```
Expect: `Success! The configuration is valid.`

---

### T2: Add enrichment failure DLQ to `review-queue` [P]

**What**: Add `aws_sqs_queue.enrichment_failure_dlq` (bare, no redrive policy — mirrors `moderation_failure_dlq`) plus `enrichment_failure_dlq_url`/`enrichment_failure_dlq_arn` outputs.
**Where**: `iac/modules/review-queue/main.tf` (add resource), `outputs.tf` (add two outputs)
**Depends on**: None
**Reuses**: `aws_sqs_queue.moderation_failure_dlq` as the direct template (copy-paste-rename)
**Requirement**: EIAC-06

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `aws_sqs_queue.enrichment_failure_dlq` named `"${var.prefix}-enrichment-failure-dlq"`, no `redrive_policy`, no attribute overrides (same shape as `moderation_failure_dlq`)
- [ ] `outputs.tf` adds `enrichment_failure_dlq_url` and `enrichment_failure_dlq_arn`
- [ ] Existing `moderation_failure_dlq`/`review`/`review_dlq` resources and their outputs untouched
- [ ] Gate check passes: `terraform fmt -check` and `terraform validate` (inside `iac/modules/review-queue`)

**Tests**: none
**Gate**: `terraform fmt -check` (recursive) + `terraform validate`

**Verify**:
```
cd iac/modules/review-queue && terraform fmt -check && terraform validate
```
Expect: `Success! The configuration is valid.`

---

### T3: Extend `iam-roles`' enrichment policy [P]

**What**: Add `S3Read`, `IdempotencyTableWrite`, `FailureDlqSend` statements to `data.aws_iam_policy_document.enrichment`; add the three backing input variables.
**Where**: `iac/modules/iam-roles/main.tf` (add statements to existing `enrichment` doc), `variables.tf` (add `media_bucket_arn` if not already present, `enrichment_idempotency_table_arn`, `enrichment_failure_dlq_arn`)
**Depends on**: None
**Reuses**: `data.aws_iam_policy_document.moderation`'s statement shape as the structural template (per design.md)
**Requirement**: EIAC-08, EIAC-09

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `enrichment` policy document has exactly four statements: `BedrockInvoke` (unchanged), `S3Read` (`s3:GetObject` on `${var.media_bucket_arn}/*`), `IdempotencyTableWrite` (`dynamodb:PutItem` on `var.enrichment_idempotency_table_arn`), `FailureDlqSend` (`sqs:SendMessage` on `var.enrichment_failure_dlq_arn`)
- [ ] No new statement references `var.moderation_*` or dedupe-scoped ARNs (ADR-AI-002 — zero permission overlap)
- [ ] `variables.tf` declares the new variables with descriptions matching the moderation module's style (explaining what they wire to)
- [ ] Gate check passes: `terraform fmt -check` and `terraform validate` (inside `iac/modules/iam-roles`)

**Tests**: none
**Gate**: `terraform fmt -check` (recursive) + `terraform validate`

**Verify**:
```
cd iac/modules/iam-roles && terraform fmt -check && terraform validate
```
Expect: `Success! The configuration is valid.` Manually confirm the four-statement count by reading `main.tf`.

---

### T4: Build `lambda-enrichment` module

**What**: New Terraform module — Enrichment Lambda function, VPC-attached, mirrors `lambda-moderation`'s shape with Enrichment's own env vars.
**Where**: `iac/modules/lambda-enrichment/main.tf`, `variables.tf`, `outputs.tf`
**Depends on**: T1 (table module exists, so this module's variables can reference the concept even though wiring happens in root config), T2 (DLQ), T3 (role policy) — conceptually; no hard Terraform dependency since this module takes ARNs/names as plain input variables like `lambda-moderation` does today
**Reuses**: `iac/modules/lambda-moderation/main.tf` verbatim for the `terraform_remote_state`/SSM/security-group/VPC-attach block; same `variables.tf` structure adapted to Enrichment's own variable names
**Requirement**: EIAC-01, EIAC-02

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `data.terraform_remote_state.platform` block copied from `lambda-moderation` (same backend/bucket/key/region)
- [ ] `aws_security_group.enrichment_lambda` egress-only, same shape as `moderation_lambda`'s
- [ ] `aws_lambda_function.enrichment` uses `var.enrichment_role_arn`, `var.lambda_handler` defaulting to `RentifyxAiServices.Enrichment::RentifyxAiServices.Enrichment.EnrichmentHandler::FunctionHandler`, `var.lambda_runtime` defaulting to `dotnet10`, VPC-attached to `public_subnets[0]` + the new SG
- [ ] `environment.variables` sets exactly: `ENRICHMENT_IDEMPOTENCY_TABLE_NAME`, `ENRICHMENT_FAILURE_DLQ_URL`, `BEDROCK_REGION` (default `us-east-1`), `KAFKA_ENRICHMENT_SUGGESTED_TOPIC` (default `asset-enrichment-suggested`), `KAFKA_BOOTSTRAP_SERVERS` (from the `try()`-wrapped SSM lookup)
- [ ] `outputs.tf` exposes `function_arn`, `function_name`, `security_group_id`, `subnet_ids` (the last so T5 doesn't re-derive VPC placement)
- [ ] Gate check passes: `terraform fmt -check` and `terraform validate` (inside `iac/modules/lambda-enrichment`)

**Tests**: none
**Gate**: `terraform fmt -check` (recursive) + `terraform validate`

**Verify**:
```
cd iac/modules/lambda-enrichment && terraform fmt -check && terraform init -backend=false && terraform validate
```
Expect: `Success! The configuration is valid.`

---

### T5: Build `kafka-event-source-mapping` module

**What**: New Terraform module — `aws_lambda_event_source_mapping` with `self_managed_event_source` wiring the Enrichment Lambda to consume `AssetMediaModerated` from the self-hosted Kafka broker.
**Where**: `iac/modules/kafka-event-source-mapping/main.tf`, `variables.tf`, `outputs.tf`
**Depends on**: T4 (consumes `lambda-enrichment`'s `function_name`, `security_group_id`, `subnet_ids` as input variables)
**Reuses**: Nothing existing in this repo — shape confirmed via Context7 (`/hashicorp/terraform-provider-aws`, `aws_lambda_event_source_mapping` docs), not guessed
**Requirement**: EIAC-03

**Tools**:
- MCP: `Context7` (already queried during design — re-confirm exact block names only if the compiler/`validate` output disagrees)
- Skill: NONE

**Done when**:
- [ ] `variables.tf` defines `function_name`, `topics` (default `["asset-media-moderated"]`), `starting_position` (default `TRIM_HORIZON`), `kafka_bootstrap_servers`, `subnet_ids`, `security_group_id`, `consumer_group_id` (default `"${var.prefix}-enrichment-consumer"` — resolves the design.md Open Question toward "explicit")
- [ ] `main.tf`'s `aws_lambda_event_source_mapping.enrichment` sets `function_name`, `topics`, `starting_position`, a `self_managed_event_source { endpoints = { KAFKA_BOOTSTRAP_SERVERS = var.kafka_bootstrap_servers } }` block, a `self_managed_kafka_event_source_config { consumer_group_id = var.consumer_group_id }` block, one `source_access_configuration { type = "VPC_SUBNET", uri = "subnet:..." }` per subnet, and one `source_access_configuration { type = "VPC_SECURITY_GROUP", uri = "security_group:..." }`
- [ ] No `source_access_configuration` auth-type block added (PLAINTEXT broker, per design.md)
- [ ] `outputs.tf` exposes `event_source_mapping_uuid`
- [ ] Gate check passes: `terraform fmt -check` and `terraform validate` (inside `iac/modules/kafka-event-source-mapping`)

**Tests**: none
**Gate**: `terraform fmt -check` (recursive) + `terraform validate`

**Verify**:
```
cd iac/modules/kafka-event-source-mapping && terraform fmt -check && terraform init -backend=false && terraform validate
```
Expect: `Success! The configuration is valid.`

---

### T6: Wire everything into `iac/terraform/` root config

**What**: Instantiate `dynamodb-table` (as `enrichment_idempotency_table`), the extended `review-queue` (already instantiated — just consume its new outputs), the extended `iam-roles` (already instantiated — pass the three new variables), `lambda-enrichment`, and `kafka-event-source-mapping` in `iac/terraform/main.tf`; add corresponding new root `variables.tf` entries only for values with no other source (e.g. `bedrock_model_arn` already exists as a gap per STATE.md — reuse it, don't re-add).
**Where**: `iac/terraform/main.tf`, `iac/terraform/variables.tf` (only if a genuinely new root-level input is needed), `iac/terraform/outputs.tf` (optional: surface `enrichment` function ARN/ESM UUID if useful)
**Depends on**: T1, T2, T3, T4, T5
**Reuses**: Existing `module.lambda_moderation`/`module.s3_trigger` wiring style in `main.tf` as the template
**Requirement**: EIAC-04, EIAC-07

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `module.enrichment_idempotency_table` instantiated with `table_name = "${local.prefix}-enrichment-idempotency"`
- [ ] `module.iam_roles`'s enrichment inputs (`media_bucket_arn`, `enrichment_idempotency_table_arn`, `enrichment_failure_dlq_arn`) wired from `module.enrichment_idempotency_table.table_arn` and `module.review_queue.enrichment_failure_dlq_arn` (and the existing `media_bucket_arn` variable moderation already uses)
- [ ] `module.lambda_enrichment` instantiated, its `enrichment_role_arn`/`idempotency_table_name`/`failure_dlq_url` wired from the modules above's outputs, not hardcoded
- [ ] `module.kafka_event_source_mapping` instantiated, wired from `module.lambda_enrichment`'s outputs
- [ ] Gate check passes: `terraform fmt -check` and `terraform init -backend=false && terraform validate` at `iac/terraform/` root

**Tests**: none
**Gate**: `terraform fmt -check` (recursive) + `terraform validate`

**Verify**:
```
cd iac/terraform && terraform fmt -check && terraform init -backend=false && terraform validate
```
Expect: `Success! The configuration is valid.`

**Commit**: `feat(iac): wire enrichment Lambda, Kafka ESM, idempotency table, and failure DLQ into root config`

---

### T7: Full repo-wide fmt/validate/plan sweep

**What**: Run the full gate across every touched and pre-existing module to confirm nothing regressed, and attempt a `terraform plan` at the root to confirm only the pre-existing, documented variables (media bucket, package path, Bedrock ARN) remain unresolved.
**Where**: N/A (verification only, no file changes expected)
**Depends on**: T6
**Reuses**: N/A
**Requirement**: All (EIAC-01 through EIAC-09), Success Criteria in spec.md

**Tools**:
- MCP: NONE
- Skill: NONE

**Done when**:
- [ ] `terraform fmt -check -recursive` from `iac/` passes with zero diffs across all modules
- [ ] `terraform validate` passes clean for every module directory under `iac/modules/` and `iac/terraform/`
- [ ] `terraform plan` at `iac/terraform/` (with dummy/placeholder values for the pre-existing unresolved variables, or `-target`/`-var` flags as needed) shows no errors beyond the documented gaps — record which variables still lack defaults
- [ ] STATE.md and ROADMAP.md updated to reflect this feature's closure, same pattern as prior features

**Tests**: none
**Gate**: `terraform fmt -check` (recursive) + `terraform validate` + `terraform plan` (informational)

**Verify**:
```
cd iac && terraform fmt -check -recursive
```
Then `terraform validate` in each module directory and `iac/terraform/`. Expect all `Success!`.

**Commit**: `docs: close out E-03b enrichment IaC (STATE, ROADMAP)`

---

## Parallel Execution Map

```
Phase 1 (Parallel):
  T1 [P] ── dynamodb-table
  T2 [P] ── review-queue extension
  T3 [P] ── iam-roles extension

Phase 2 (Sequential, after Phase 1):
  T4 ── lambda-enrichment

Phase 3 (Sequential, after T4):
  T5 ── kafka-event-source-mapping

Phase 4 (Sequential, after T5):
  T6 ── root composition
  T7 ── full sweep + STATE/ROADMAP update
```

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T1: Create `dynamodb-table` module | 1 module, 3 files | ✅ Granular |
| T2: Add enrichment DLQ to `review-queue` | 1 resource + 2 outputs, 1 module | ✅ Granular |
| T3: Extend `iam-roles` enrichment policy | 3 statements + 3 variables, 1 module | ✅ Granular |
| T4: Build `lambda-enrichment` module | 1 module, 3 files | ✅ Granular |
| T5: Build `kafka-event-source-mapping` module | 1 module, 3 files | ✅ Granular |
| T6: Wire root config | 1 file (`main.tf`), additive wiring only | ✅ Granular |
| T7: Full sweep + docs closeout | Verification + 2 doc files | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T1 | None | No incoming arrow | ✅ Match |
| T2 | None | No incoming arrow | ✅ Match |
| T3 | None | No incoming arrow | ✅ Match |
| T4 | T1, T2, T3 (conceptual) | T1/T2/T3 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T1, T2, T3, T4, T5 | T5 → T6 (T1–T3 already merged into T4's phase boundary) | ✅ Match |
| T7 | T6 | T6 → T7 | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T1 | `terraform` module | `terraform fmt -check` / `validate` | `terraform fmt -check` + `validate` | ✅ OK |
| T2 | `terraform` module (extend) | same | same | ✅ OK |
| T3 | `terraform` module (extend) | same | same | ✅ OK |
| T4 | `terraform` module | same | same | ✅ OK |
| T5 | `terraform` module | same | same | ✅ OK |
| T6 | `terraform` root config | same | same | ✅ OK |
| T7 | verification only | same | same | ✅ OK |

---

## Tools for Execution

No project MCPs beyond Context7 (already used during Design for T5's resource shape) are needed — all tasks are local Terraform file edits + CLI gate checks. No skills beyond this one apply per-task.
