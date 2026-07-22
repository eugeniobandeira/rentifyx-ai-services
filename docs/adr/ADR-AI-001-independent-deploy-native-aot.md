# ADR-AI-001: Independent Deploy per Lambda, Native AOT as Default

- **Date:** 2026-07-22
- **Status:** Accepted

## Context

`rentifyx-ai-services` packages three (eventually four) independent Lambda functions — Moderation, Enrichment, Dedupe (deferred) — from a single repo. Two decisions were open per the project plan (T-004, T-011):

1. Deploy as one package per function, or one shared package/pipeline for all functions?
2. Build each function as a Native AOT executable, or as a managed .NET runtime zip?

## Options Considered

**Deploy model:**
- **A — One `aws-lambda-tools-defaults.json` + one deploy step per function.** Each function versions and ships independently.
- **B — Single build/deploy pipeline that packages all functions together.** Simpler pipeline, but couples unrelated release cadences (a Dedupe change would force a Moderation redeploy).

**Runtime model:**
- **A — Native AOT executable (`provided.al2023` custom runtime).** Faster cold start, smaller package, but AOT compatibility must be verified per dependency (reflection-heavy libraries can break) and build times are slower.
- **B — Managed .NET runtime zip (`dotnet10` managed runtime).** Simpler build, broader library compatibility, but slower cold start — relevant since these Lambdas are invoked async off S3/Kafka events, not on a latency-sensitive request path.

## Decision

**Deploy model: Option A.** Independent deploy per Lambda, matching this repo's whole reason for existing (ADR-AI-007 — event-only, decoupled release cadence). Each function keeps its own `aws-lambda-tools-defaults.json` and its own CI job/matrix entry.

**Runtime model: managed runtime zip as the default, Native AOT evaluated per function once real handler code exists.** Cold start matters less here than in a synchronous API — these functions react to S3/Kafka events, not user-facing requests — so the managed runtime's simpler build and broader AWSSDK/Confluent.Kafka compatibility outweighs Native AOT's cold-start win for v1. Native AOT stays open as a per-function optimization (most likely Moderation, the highest-volume trigger) once E-02/E-03 land and cold start is measured against real traffic, not assumed.

## Consequences

- CI matrix builds and packages each function independently (T-007); a broken Enrichment build never blocks a Moderation deploy.
- No AOT-compatibility audit needed before writing the first real handler — Confluent.Kafka and AWSSDK clients run under the managed runtime without reflection workarounds.
- Revisiting this ADR is expected once cold-start numbers exist in production (see plan's watch items) — if SQS/Kafka backlog builds up during cold starts, Native AOT becomes the next lever, function by function, not repo-wide.
