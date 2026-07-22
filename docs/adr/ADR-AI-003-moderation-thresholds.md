# ADR-AI-003: Moderation Confidence Thresholds

- **Date:** 2026-07-22
- **Status:** Accepted

## Context

`ThresholdEvaluator` (E-02, MOD-02) maps Rekognition's `DetectModerationLabels` confidence score to a `Verdict` — `Approved`, `PendingReview`, or `Rejected`. Two boundary values had to be picked: the confidence above which content is rejected outright without human review, and the confidence below which content is approved outright.

## Options Considered

- **A — Two-tier (approve/reject only, no manual review).** Simplest, but any single threshold either lets borderline violations through (threshold too high) or auto-rejects legitimate content on a false positive (threshold too low). No safety net for the ambiguous middle.
- **B — Three-tier: auto-reject ≥90%, manual review [60%, 90%), auto-approve <60%.** Adds a `PendingReview` tier for the confidence band where Rekognition itself is least certain, routing only the unambiguous extremes through full automation.
- **C — Three-tier with a narrower review band (e.g. [80%, 95%)).** Reduces manual review volume further, but pushes more borderline content into auto-approve/auto-reject, increasing both false-negative and false-positive risk at the tier edges.

## Decision

Option B: `>=90%` → `Rejected`, `[60%, 90%)` → `PendingReview`, `<60%` → `Approved`, no labels at all → `Approved`. These are the exact boundaries the E-02 spec's acceptance criteria (MOD-02) and `ThresholdEvaluatorTests`' boundary cases (59/60/90/90.1/no-labels) encode.

The 90% floor for auto-reject reflects Rekognition's own moderation-label confidence being reliable at the extreme high end for the label categories in scope (explicit content, violence). The 60% floor for the review band keeps genuinely low-confidence, likely-benign content out of the manual queue — without it, every image with any detected label at all would need human eyes, defeating the point of an async auto-moderation pipeline (spec's Goals: "without manual intervention" for the common case).

## Consequences

- The manual review queue (MOD-04, `iac/modules/review-queue`) only receives the ambiguous 60–90% band — its expected volume is bounded by how often Rekognition lands in that range, not by total upload volume.
- These thresholds are unvalidated against real traffic — they're the spec's stated starting point, not a tuned value from production data. Revisit once `asset-registry-api`'s real upload volume and Rekognition's actual confidence distribution are observable (tracked as an open item, not blocking E-02's initial ship).
- Changing either boundary only requires editing the two constants in `ThresholdEvaluator` — the boundary tests will immediately show if a change shifts observed verdicts at the documented edge cases.
