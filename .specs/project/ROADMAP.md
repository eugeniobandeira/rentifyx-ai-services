# Roadmap

## Phase 1

- [x] Foundation scaffold and repository structure aligned with existing RentifyX conventions
- [x] Solution and initial projects created and build-verified
- [x] E-01 closed: test scaffold, CI (test-pass gate), least-privilege IAM Terraform module, ADR-AI-001/002
- [x] E-02 closed: Moderation pipeline with Rekognition, ADR-AI-003/004 (LocalStack/Kafka integration test verified against real Docker 2026-07-22)
- [x] Event contracts and integration baseline (`iac/terraform/` root config composes `iam-roles`/`review-queue`/`lambda-moderation`/`s3-trigger`, `terraform validate` clean; real `apply` still blocked on media bucket, deployment package, and idempotency table not existing yet)

## Phase 2

- [x] Enrichment pipeline with Bedrock (E-03 closed: Converse API, Kafka-triggered via `Amazon.Lambda.KafkaEvents`, idempotent, cost/safety guardrails — ADR-AI-005/006 written and accepted. LocalStack/Kafka integration test verified against real Docker 2026-07-23.)
- [x] Enrichment pipeline IaC (E-03b closed 2026-07-24: `lambda-enrichment`, `kafka-event-source-mapping`, generic `dynamodb-table` module, enrichment failure DLQ, `iam-roles` enrichment policy extended to S3/DynamoDB/SQS. `terraform validate` clean across all modules and root config.)
- [x] Deploy readiness pass (E-03c closed 2026-07-24: media bucket provisioned in this repo, not `rentifyx-platform`; Moderation's idempotency table migrated onto the shared `dynamodb-table` module; real Bedrock inference-profile ARN confirmed via live AWS API; CI packages both Lambda zips)
- [x] **Real AWS deploy performed and largely verified, 2026-07-24 (user-authorized, temporary — see STATE.md's teardown section):** `rentifyx-platform`'s VPC/Kafka applied for real, this repo's full 23-resource stack applied for real in `sa-east-1`. Fixed six real bugs surfaced only by live deploy (stale provider pin rejecting the real `dotnet10` runtime, SG description charset, missing `AWSLambdaVPCAccessExecutionRole`/Kafka-ESM EC2 Describe permissions, missing `[assembly: LambdaSerializer]` on both handlers, Lambdas VPC-attached into a public subnet with zero AWS API egress). Confirmed end-to-end: S3 upload → Rekognition → DynamoDB idempotency write → Kafka publish, all against real AWS. **Not yet confirmed**: Enrichment's Kafka consumption — the event source mapping can't connect, traced to apparent Kafka broker instability on `rentifyx-platform`'s side (SSM Agent lost connection at boot and never recovered), a cross-repo follow-up.
- [ ] Manual review and observability hardening
- [ ] Production readiness and release gate

## Deferred

- [ ] Duplicate/fraud detection implementation (DEF-AI-001)
