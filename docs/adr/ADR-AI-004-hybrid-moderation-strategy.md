# ADR-AI-004: Hybrid Auto-Moderation + Manual Review Strategy

- **Date:** 2026-07-22
- **Status:** Accepted

## Context

E-02's moderation pipeline needs to decide what happens to each of the three possible verdicts (see ADR-AI-003 for how a verdict is computed). The spec's Goals state moderation must run "without manual intervention" for the common case, but also that ambiguous cases must not be auto-approved or auto-rejected outright — false positives (rejecting legitimate content) and false negatives (approving policy-violating content) both carry real cost, and neither can be fully eliminated by a threshold alone.

## Options Considered

- **A — Fully automated (Approved/Rejected only, no review tier).** No manual workload, but forces every borderline image into either bucket, maximizing both false-positive and false-negative rates at the threshold edges.
- **B — Fully manual (every upload reviewed by a human).** Maximizes accuracy, but doesn't scale — defeats the entire purpose of an async Rekognition pipeline and contradicts the spec's "without manual intervention" goal for the common case.
- **C — Hybrid: auto-approve/auto-reject the unambiguous extremes, route only the ambiguous middle to a human-reviewed queue.** Scales for the common case (most uploads are clearly clean or clearly violating) while keeping a safety net for the cases automation is least confident about.

## Decision

Option C. `ModerationService` (E-02, MOD-01/02/03) publishes `AssetMediaModerated` directly to Kafka for `Approved`/`Rejected` verdicts — `asset-registry-api` acts on these immediately, no human in the loop. For `PendingReview`, it publishes `AssetPendingManualReview` to Kafka *and* enqueues the item to the SQS review queue (`iac/modules/review-queue`, MOD-04) with a CloudWatch alarm on queue depth exceeding threshold for over an hour, so a review backlog becomes visible before it becomes an SLA problem.

Ownership split: this repo (`rentifyx-ai-services`) computes the verdict and manages the review queue's existence/depth; the actual reviewer-facing UI and the action taken on a `PendingReview` item belong to `asset-registry-api`'s future `AdminReviewAsset` capability (out of scope here per the E-02 spec — see spec's Out of Scope table).

## Consequences

- `asset-registry-api` must handle three possible terminal states for an asset's media (`Approved`, `Rejected`, `PendingReview` followed eventually by a human-driven resolution) rather than a binary pass/fail — this is a cross-repo contract dependency, not something this repo can unilaterally simplify.
- Manual review capacity becomes an operational concern: if reviewers can't keep pace with the `PendingReview` rate, the CloudWatch alarm surfaces it, but resourcing the review process itself is outside this repo's control.
- If the threshold boundaries in ADR-AI-003 shift, the review queue's volume shifts with them — the two decisions are coupled and should be revisited together once real confidence-distribution data exists.
