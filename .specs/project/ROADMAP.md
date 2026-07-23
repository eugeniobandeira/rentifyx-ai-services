# Roadmap

## Phase 1

- [x] Foundation scaffold and repository structure aligned with existing RentifyX conventions
- [x] Solution and initial projects created and build-verified
- [x] E-01 closed: test scaffold, CI (test-pass gate), least-privilege IAM Terraform module, ADR-AI-001/002
- [x] E-02 closed: Moderation pipeline with Rekognition, ADR-AI-003/004 (LocalStack/Kafka integration test verified against real Docker 2026-07-22)
- [x] Event contracts and integration baseline (`iac/terraform/` root config composes `iam-roles`/`review-queue`/`lambda-moderation`/`s3-trigger`, `terraform validate` clean; real `apply` still blocked on media bucket, deployment package, and idempotency table not existing yet)

## Phase 2

- [ ] Enrichment pipeline with Bedrock
- [ ] Manual review and observability hardening
- [ ] Production readiness and release gate

## Deferred

- [ ] Duplicate/fraud detection implementation (DEF-AI-001)
