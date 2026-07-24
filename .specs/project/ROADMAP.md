# Roadmap

## Phase 1

- [x] Foundation scaffold and repository structure aligned with existing RentifyX conventions
- [x] Solution and initial projects created and build-verified
- [x] E-01 closed: test scaffold, CI (test-pass gate), least-privilege IAM Terraform module, ADR-AI-001/002
- [x] E-02 closed: Moderation pipeline with Rekognition, ADR-AI-003/004 (LocalStack/Kafka integration test verified against real Docker 2026-07-22)
- [x] Event contracts and integration baseline (`iac/terraform/` root config composes `iam-roles`/`review-queue`/`lambda-moderation`/`s3-trigger`, `terraform validate` clean; real `apply` still blocked on media bucket, deployment package, and idempotency table not existing yet)

## Phase 2

- [x] Enrichment pipeline with Bedrock (E-03 closed: Converse API, Kafka-triggered via `Amazon.Lambda.KafkaEvents`, idempotent, cost/safety guardrails — ADR-AI-005/006 written and accepted. LocalStack/Kafka integration test verified against real Docker 2026-07-23.)
- [x] Enrichment pipeline IaC (E-03b closed 2026-07-24: `lambda-enrichment`, `kafka-event-source-mapping`, generic `dynamodb-table` module, enrichment failure DLQ, `iam-roles` enrichment policy extended to S3/DynamoDB/SQS. `terraform validate` clean across all modules and root config; `apply`/`plan` still blocked, same pre-existing gaps as Moderation's root config.)
- [ ] Manual review and observability hardening
- [ ] Production readiness and release gate

## Deferred

- [ ] Duplicate/fraud detection implementation (DEF-AI-001)
