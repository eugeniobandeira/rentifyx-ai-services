# E-01 Foundation Closeout Specification

## Problem Statement

`rentifyx-ai-services` has a build-verified solution scaffold (4 projects: Moderation, Enrichment, Dedupe, Shared) but E-01 (Project Foundation & Lambda Infrastructure) is not closed: no test projects, no CI, no least-privilege IAM in Terraform, no ADRs. E-02 (Rekognition pipeline) cannot start on a safe foundation until this closes.

## Goals

- [ ] Every `src` project has a matching xUnit test project wired into `RentifyxAiServices.slnx`
- [ ] CI runs build + test per function (matrix), gated on tests passing — no coverage threshold
- [ ] One least-privilege IAM role per Lambda defined in Terraform (`iac/modules/iam-roles`)
- [ ] ADR-AI-001 (deploy model) and ADR-AI-002 (IAM isolation) written and linked from `docs/adr/`

## Out of Scope

| Item | Reason |
|---|---|
| Coverage gate ≥80% (original plan T-008) | User decision: tests passing is the gate, not a coverage percentage |
| Actual Rekognition/Bedrock logic | Belongs to E-02/E-03 |
| Real deploy step (`dotnet lambda deploy-function`) | No AWS target account wired yet; out of scope until E-06 |
| ADR-AI-003 through 007 | Tied to moderation thresholds, enrichment, dedupe — written when those epics land |
| `iac/modules/s3-trigger`, `kafka-event-source-mapping`, `lambda-moderation`, `lambda-enrichment` content | Only `iam-roles` is in scope now; other modules stay scaffolded empty until their owning epic |

---

## User Stories

### P1: Test scaffold ⭐ MVP

**User Story**: As a dev, I want a test project per function so handler logic is verifiable from day one.

**Why P1**: Nothing else in the roadmap is trustworthy without a place to put tests.

**Acceptance Criteria**:

1. WHEN `dotnet build RentifyxAiServices.slnx` runs THEN it SHALL include 4 test projects (Moderation, Enrichment, Shared, Integration) with zero errors.
2. WHEN `dotnet test RentifyxAiServices.slnx` runs THEN it SHALL execute successfully with at least one placeholder test per project (proves wiring, not coverage).

**Independent Test**: `dotnet test` from repo root exits 0.

---

### P1: CI pipeline ⭐ MVP

**User Story**: As a tech lead, I want CI to build and test every function on push so broken code never lands on `main`.

**Why P1**: No safety net currently exists — CI directory is empty.

**Acceptance Criteria**:

1. WHEN a PR is opened THEN GitHub Actions SHALL run `dotnet build` then `dotnet test` for the whole solution.
2. WHEN any test fails THEN the workflow SHALL fail the check — no coverage percentage is evaluated.

**Independent Test**: Workflow file present, matches solution's actual project names, syntactically valid YAML.

---

### P2: Least-privilege IAM module

**User Story**: As a security engineer, I want one IAM role per Lambda scaffolded in Terraform so blast radius stays scoped per ADR-AI-002.

**Why P2**: Needed before any real Lambda deploy, but doesn't block local dev/test loop.

**Acceptance Criteria**:

1. WHEN `terraform validate` runs against `iac/modules/iam-roles` THEN it SHALL pass with no errors.
2. WHEN the module is inspected THEN it SHALL expose one role resource per function (moderation, enrichment, dedupe) with no shared execution role.

**Independent Test**: `terraform validate` (or `terraform fmt -check` if no AWS creds available for full validate).

---

### P2: ADR-AI-001 and ADR-AI-002

**User Story**: As a tech lead, I want the deploy model and IAM isolation decisions written down so future contributors don't relitigate them.

**Why P2**: Documentation debt, not a build blocker, but explicitly required by E-01's own task list (T-011, T-016).

**Acceptance Criteria**:

1. WHEN `docs/adr/ADR-AI-001-independent-deploy-native-aot.md` is read THEN it SHALL state the chosen deploy model (Native AOT vs managed runtime zip) and the tradeoff reasoning.
2. WHEN `docs/adr/ADR-AI-002-iam-isolation.md` is read THEN it SHALL state one-role-per-Lambda as the decision and reference the Terraform module.

**Independent Test**: Files exist, follow the repo's ADR template shape (context/decision/consequences).

---

## Edge Cases

- WHEN a test project has zero real tests yet THEN it SHALL still contain one trivial passing test (proves the project compiles and is discovered by the test runner, avoids a false-green "no tests found").
- WHEN Terraform has no AWS credentials in this environment THEN validation SHALL fall back to `terraform fmt -check` / `validate` without a real backend, not skipped silently.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
|---|---|---|---|
| E01-01 | P1: Test scaffold | Tasks | Pending |
| E01-02 | P1: CI pipeline | Tasks | Pending |
| E01-03 | P2: IAM module | Tasks | Pending |
| E01-04 | P2: ADR-AI-001 | Tasks | Pending |
| E01-05 | P2: ADR-AI-002 | Tasks | Pending |

**Coverage:** 5 total, 5 mapped to tasks, 0 unmapped

---

## Success Criteria

- [ ] `dotnet build RentifyxAiServices.slnx` — 0 errors, includes test projects
- [ ] `dotnet test RentifyxAiServices.slnx` — 0 failures (gate = pass/fail, no % threshold)
- [ ] `terraform fmt -check` / `validate` passes on `iam-roles` module
- [ ] ADR-AI-001, ADR-AI-002 exist and are linked from README/plan
