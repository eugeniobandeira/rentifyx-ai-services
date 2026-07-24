# Infrastructure (Terraform)

This service's AWS infrastructure is plain Terraform — no Dockerfile, no Kubernetes manifests, no
EC2 instances of its own. Every workload is an AWS Lambda function reacting to an S3 or Kafka event;
there is no synchronous HTTP endpoint anywhere in this repo (see the root `README.md`'s Security
section).

```
iac/
  terraform/     – root composition: wires every module below together, has the S3 backend config
  modules/       – seven reusable modules, each provisioning one concern
```

## Modules (`iac/modules/`)

Each module's `main.tf` was read directly to confirm what it actually provisions — nothing here is
aspirational.

- **`media-bucket`** (`iac/modules/media-bucket/main.tf`) — one private S3 bucket
  (`aws_s3_bucket.media`) with public access fully blocked (`aws_s3_bucket_public_access_block`,
  all four flags true) and configurable versioning (`aws_s3_bucket_versioning`, on/off via
  `var.versioning_enabled`). This is the asset media bucket: Moderation's S3 trigger fires off it,
  and both Moderation and Enrichment read objects from it via IAM-scoped `GetObject`.

- **`iam-roles`** (`iac/modules/iam-roles/main.tf`) — one dedicated IAM role + inline policy per
  Lambda, zero permission overlap (ADR-AI-002):
  - `moderation` role/policy: `rekognition:DetectModerationLabels` (resource `*`, no resource-level
    scoping support in that API), `s3:GetObject` on the media bucket, `dynamodb:PutItem` on
    Moderation's idempotency table, `sqs:SendMessage` on the review queue and the moderation
    failure DLQ, plus the AWS-managed `AWSLambdaVPCAccessExecutionRole` policy attachment (VPC ENI
    management for the Kafka-reaching VPC attachment).
  - `enrichment` role/policy: `bedrock:InvokeModel` scoped to `var.bedrock_model_arn`,
    `s3:GetObject` on the media bucket, `dynamodb:PutItem` on Enrichment's idempotency table,
    `sqs:SendMessage` on the enrichment failure DLQ, `ec2:DescribeSecurityGroups` /
    `DescribeSubnets` / `DescribeVpcs` (required by the self-managed Kafka event source mapping,
    resource `*`), and the same `AWSLambdaVPCAccessExecutionRole` attachment.
  - `dedupe` role/policy: `rekognition:CompareFaces` only (resource `*`) — pre-scoped ahead of the
    still-unimplemented Dedupe Lambda (DEF-AI-001), no VPC attachment.

- **`dynamodb-table`** (`iac/modules/dynamodb-table/main.tf`) — generic, feature-agnostic module:
  one `aws_dynamodb_table` with a single partition key (`var.hash_key`/`var.hash_key_type`),
  `var.billing_mode` (PAY_PER_REQUEST by convention), and TTL always enabled on
  `var.ttl_attribute_name`. Instantiated twice by the root config — once for Moderation's
  idempotency table, once for Enrichment's — rather than having a Moderation-specific or
  Enrichment-specific table module.

- **`review-queue`** (`iac/modules/review-queue/main.tf`) — four SQS queues plus one CloudWatch
  alarm: `review` (the manual-review queue, redrives to `review_dlq` after
  `var.max_receive_count`), `review_dlq`, `moderation_failure_dlq` (Rekognition invocations that
  fail after retries are exhausted), and `enrichment_failure_dlq` (Bedrock/S3 failures on the
  Enrichment side). The alarm (`review_queue_depth`) watches `ApproximateNumberOfMessagesVisible`
  on the review queue against `var.alarm_queue_depth_threshold` (MOD-04).

- **`s3-trigger`** (`iac/modules/s3-trigger/main.tf`) — `aws_lambda_permission` (lets S3 invoke the
  Moderation Lambda) + `aws_s3_bucket_notification` (`s3:ObjectCreated:*`, with
  `filter_prefix`/`filter_suffix` passed through, no convention baked in — deliberately, see G-001
  below) wiring the media bucket to the Moderation Lambda. Only the notification/permission glue;
  the bucket and the function are owned by other modules.

- **`lambda-moderation`** (`iac/modules/lambda-moderation/main.tf`) — the Moderation Lambda function
  itself (`aws_lambda_function.moderation`, managed .NET runtime zip, not Native AOT). Reads
  `rentifyx-platform`'s state directly via its own `terraform_remote_state` data source (bucket
  `rentifyx-tfstate-166613156216`, key `platform/terraform.tfstate`, region `us-east-1`) to resolve
  `vpc_id`/`private_subnets[0]` and the Kafka bootstrap SSM parameter path, VPC-attaches the
  function to a private subnet with its own egress-only security group
  (`aws_security_group.moderation_lambda`), and injects `IDEMPOTENCY_TABLE_NAME`,
  `KAFKA_BOOTSTRAP_SERVERS`, `KAFKA_MODERATED_TOPIC`, `KAFKA_PENDING_REVIEW_TOPIC`,
  `REVIEW_QUEUE_URL`, `FAILURE_DLQ_URL` as Lambda environment variables. Does **not** define the S3
  trigger itself — that's `s3-trigger`'s job; this module only exposes the function's ARN/name for
  that module to wire against.

- **`lambda-enrichment`** (`iac/modules/lambda-enrichment/main.tf`) — the Enrichment Lambda function
  (`aws_lambda_function.enrichment`), same shape as `lambda-moderation`: its own
  `terraform_remote_state` read against `rentifyx-platform`'s state (identical bucket/key/region),
  its own VPC attachment and egress-only security group
  (`aws_security_group.enrichment_lambda`), and environment variables
  `ENRICHMENT_IDEMPOTENCY_TABLE_NAME`, `ENRICHMENT_FAILURE_DLQ_URL`, `BEDROCK_REGION`,
  `KAFKA_ENRICHMENT_SUGGESTED_TOPIC`, `KAFKA_BOOTSTRAP_SERVERS`. Does **not** define the Kafka event
  source mapping — that's `kafka-event-source-mapping`'s job.

- **`kafka-event-source-mapping`** (`iac/modules/kafka-event-source-mapping/main.tf`) — one
  `aws_lambda_event_source_mapping` wiring the Enrichment Lambda to consume
  `AssetMediaModerated` from the self-managed (non-MSK) Kafka broker: uses
  `self_managed_event_source` (bootstrap servers endpoint) and
  `self_managed_kafka_event_source_config` (consumer group id, defaulted via
  `coalesce(var.consumer_group_id, "${var.prefix}-enrichment-consumer")`), plus one
  `source_access_configuration` of type `VPC_SUBNET` per subnet id and one of type
  `VPC_SECURITY_GROUP` — no SASL/auth entry, since the broker is PLAINTEXT.

## How `iac/terraform/main.tf` composes these

The root config wires all seven modules together in this order (read directly from
`iac/terraform/main.tf`):

1. `module.media_bucket` — bucket named `${prefix}-media-${account_id}` (account-id suffix for
   S3's global uniqueness requirement).
2. `module.moderation_idempotency_table` and `module.enrichment_idempotency_table` — two separate
   instantiations of the generic `dynamodb-table` module, named
   `${prefix}-moderation-idempotency` / `${prefix}-enrichment-idempotency`.
3. `module.review_queue` — the SQS queues/DLQs/alarm.
4. `module.iam_roles` — takes the media bucket ARN, both idempotency table ARNs, the review queue
   ARN, both failure DLQ ARNs, and `var.bedrock_model_arn` as inputs; produces the three role ARNs.
5. `module.lambda_moderation` — takes the moderation role ARN, `var.lambda_package_path`, the
   moderation idempotency table name, review queue URL, and moderation failure DLQ URL.
6. `module.s3_trigger` — wires the media bucket to the Moderation Lambda's ARN/name, passing
   `var.filter_prefix`/`var.filter_suffix` through unmodified.
7. `module.lambda_enrichment` — takes the enrichment role ARN, `var.enrichment_lambda_package_path`,
   the enrichment idempotency table name, and the enrichment failure DLQ URL.
8. `module.kafka_event_source_mapping` — takes the Enrichment function name plus
   `kafka_bootstrap_servers`/`subnet_ids`/`security_group_id`, all three read as **outputs of
   `module.lambda_enrichment`** (that module exposes them; the root config doesn't read
   `terraform_remote_state` itself for this — see Cross-repo dependency below).

The AWS provider (`iac/terraform/main.tf`) is pinned to `~> 6.0`, region `var.aws_region`
(default `sa-east-1`), profile `rentifyx-admin`, with `default_tags` applying `Application`,
`Environment`, `ManagedBy=terraform` to every resource.

## Backend (`iac/terraform/backend.tf`)

`backend.tf` declares an empty `backend "s3" {}` skeleton on purpose — real values are supplied via
`-backend-config` flags at `terraform init` time, not hardcoded, per the comment in that file:

- `bucket = rentifyx-tfstate-166613156216`
- `key = ai-services/terraform.tfstate`
- `region = us-east-1`
- `dynamodb_table = rentifyx-tflock`

Same convention as `rentifyx-identity-api`'s `iac/terraform/backend.tf`. `required_version >= 1.7`;
the `aws` provider is pinned `~> 6.0` (bumped from `~> 5.0` because Lambda's `dotnet10` managed
runtime, added 2026-01-08, isn't a valid enum value under the older provider schema).

## Variables (`iac/terraform/variables.tf`) and `terraform.tfvars`

| Variable | Default | Required in `terraform.tfvars`? |
|---|---|---|
| `aws_region` | `sa-east-1` | No |
| `environment` | `production` | No |
| `app_name` | `rentifyx` | No |
| `filter_prefix` | `""` | No — deliberately no baked-in convention (G-001 unconfirmed cross-repo with `asset-registry-api`) |
| `filter_suffix` | `""` | No |
| `lambda_package_path` | *(none)* | **Yes** — path to the built moderation Lambda zip (`dotnet lambda package` / CI build output) |
| `bedrock_model_arn` | `arn:aws:bedrock:us-east-1:166613156216:inference-profile/us.anthropic.claude-sonnet-5` | No, but note it's pinned to `us-east-1` even though everything else defaults to `sa-east-1` — Claude Sonnet 5's cross-region inference profile has no `sa-east-1` presence |
| `enrichment_lambda_package_path` | *(none)* | **Yes** — path to the built enrichment Lambda zip, a separate package from moderation's |

A minimal `terraform.tfvars` for this repo only needs the two package paths, since everything else
has a working default:

```hcl
lambda_package_path            = "../../src/Functions/Moderation/bin/Release/net10.0/publish.zip"
enrichment_lambda_package_path = "../../src/Functions/Enrichment/bin/Release/net10.0/publish.zip"
```

(Exact publish output paths depend on the actual `dotnet lambda package` invocation — adjust to
match your build step.)

## Outputs (`iac/terraform/outputs.tf`)

`moderation_role_arn`, `enrichment_role_arn`, `dedupe_role_arn`, `review_queue_url`,
`review_dlq_arn`, `moderation_failure_dlq_url`, `moderation_function_arn`,
`moderation_function_name`, `moderation_security_group_id`. Note there is no `enrichment_function_*`
or `enrichment_security_group_id` output yet, even though `module.lambda_enrichment` exists and
exposes those values internally to `module.kafka_event_source_mapping`.

## Event-only, Lambda-only — no EC2/Docker/Kubernetes

This service provisions **only** AWS Lambda functions, an S3 bucket, DynamoDB tables, SQS
queues/DLQs, a CloudWatch alarm, IAM roles, and a Kafka event source mapping. There is no
`aws_instance`, no ECS/EKS resource, no container image, and no Dockerfile anywhere in `iac/`. Both
Lambdas are **event-triggered only** (S3 `ObjectCreated` for Moderation, a Kafka event source
mapping for Enrichment) — neither exposes an HTTP endpoint, and there is no API Gateway, ALB, or
any other synchronous entrypoint defined in this Terraform.

## Cross-repo dependency on `rentifyx-platform`

Confirmed directly in code (not assumed): both `iac/modules/lambda-moderation/main.tf` and
`iac/modules/lambda-enrichment/main.tf` each declare their own
`data "terraform_remote_state" "platform"` block, reading:

```hcl
backend = "s3"
config = {
  bucket = "rentifyx-tfstate-166613156216"
  key    = "platform/terraform.tfstate"
  region = "us-east-1"
}
```

They pull `vpc_id`, `private_subnets[0]`, and `kafka_ssm_parameter_path` (the last wrapped in
`try(..., "")` so a plan/apply doesn't hard-fail before `rentifyx-platform`'s own `module.kafka` has
been applied) from that remote state. This is read independently inside each Lambda module — the
root `iac/terraform/main.tf` itself never touches `terraform_remote_state` directly. The Kafka
broker being consumed is self-hosted (EC2, KRaft, PLAINTEXT, port 9092) — not Amazon MSK — so both
Lambdas must be VPC-attached to reach it; reachability is VPC/security-group based, not an IAM
concern (no `kafka-cluster:*` actions appear anywhere in `iam-roles`).

## Local module validation

`iac/modules/*` each carry their own `.terraform.lock.hcl` / `.terraform/` (already initialized
standalone). To validate the root composition:

```bash
cd iac/terraform
terraform init -backend-config="bucket=rentifyx-tfstate-166613156216" \
  -backend-config="key=ai-services/terraform.tfstate" \
  -backend-config="region=us-east-1" \
  -backend-config="dynamodb_table=rentifyx-tflock"
terraform validate
terraform fmt -check
```
