# ADR-AI-002: One IAM Role per Lambda, No Shared Execution Role

- **Date:** 2026-07-22
- **Status:** Accepted

## Context

Three Lambdas (Moderation, Enrichment, Dedupe) run in this repo, each touching a different AWS service: Rekognition, Bedrock, and (eventually) an image-similarity API. A single shared execution role would be simplest to wire up in Terraform, but it means a compromised or misconfigured function inherits permissions it doesn't need — e.g. the Enrichment function would be able to call Rekognition even though it never needs to.

## Options Considered

- **Option A — One shared execution role for all three Lambdas.** Fewer Terraform resources, faster to wire up, but violates least privilege: every function's blast radius includes every other function's permissions.
- **Option B — One dedicated IAM role + policy per Lambda.** More Terraform resources (one role, one policy document, one attachment per function), but each function's permissions are scoped to exactly what it calls.

## Decision

**Option B.** `iac/modules/iam-roles` defines three roles:

- `moderation` — `rekognition:DetectModerationLabels` (no resource-level scoping supported by this action) + `s3:GetObject` scoped to the media bucket ARN only.
- `enrichment` — `bedrock:InvokeModel` scoped to one model ARN (passed in as `var.bedrock_model_arn`), not `*`.
- `dedupe` — scoped narrowly ahead of implementation (DEF-AI-001) so the role isn't a blank check once the function ships; the exact action set will be revisited when Dedupe is actually implemented (ADR-AI-006).

No role is shared. Each function's Terraform module (`lambda-moderation`, `lambda-enrichment`, future `lambda-dedupe`) attaches only its own role.

## Consequences

- A compromised Moderation function cannot call `bedrock:InvokeModel` — the two functions have zero permission overlap.
- Terraform has more resources to manage (3 roles + 3 policies vs. 1+1), but this is a one-time cost paid once in `iac/modules/iam-roles`, not per-environment.
- `rekognition:DetectModerationLabels` and `rekognition:CompareFaces` (dedupe placeholder) don't support resource-level ARN scoping — those statements use `resources = ["*"]` by AWS API design, not as a shortcut; this is called out inline in `main.tf` so it isn't mistaken for a least-privilege violation later.
