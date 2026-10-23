# FSM_API_Unity: CiAi Integration Sample

## Intended role

Unity-specific FSM API integration; keep Unity dependencies isolated from the dependency-free FSM_API core.

This is an integration proposal based on the repository's known name and observed ecosystem role, not a claim that the repository has already implemented a CiAi client or a complete workflow inventory.

## What CiAi should consume

- **Repository identity:** canonical owner/name, default branch, commit SHA, and the exact workflow/run URL.
- **Workflow evidence:** workflow name and file, event, branch/ref, head SHA, run ID/attempt, status/conclusion, timestamps, and job/step results where available.
- **Dependency provenance:** every sibling checkout or package dependency should record its resolved commit SHA or package version; a moving branch name alone is not reproducible evidence.
- **Declared capabilities:** build/test/package/deploy/docs/benchmark or host-specific lanes, each explicitly classified rather than inferred from a green overall run.
- **Limitations:** distinguish failed, cancelled, skipped, not triggered, unavailable, and unknown evidence. A missing workflow is not a failure, and a successful build is not proof of every capability.

## How this repository should consume CiAi

1. Keep GitHub Actions (or the repository's existing provider CI) as the authoritative executor. CiAi observes and correlates; it does not replace the workflow.
2. Declare this repository's purpose, packages, capabilities, dependency constraints, sample entry points, and required evidence in a machine-readable declaration when that contract is finalized.
3. Emit or expose a run-evidence record after CI completes. Prefer a stable identity of `repository#run-id#attempt`, plus the tested commit SHA and workflow/job identities.
4. Let CiAi normalize provider facts, connect them to this repository and its dependencies, and produce a traceable compatibility/evidence view.
5. Keep repository-specific validation in this repository's workflow. CiAi should report the exact failed job/step and link to provider logs rather than reimplementing the tests.
6. Start in **read-only, advisory mode**. Do not make CiAi a required merge gate until evidence semantics and failure handling have been exercised against real runs.

## Repository-specific evidence to prioritize

- Host/runtime version, platform prerequisites, adapter-level tests, and explicit skipped/unavailable host checks.

## Proposed adoption stages

- **Stage 0 — Inventory:** confirm repository purpose, active branches/workflows, package outputs, and dependency edges.
- **Stage 1 — Evidence:** expose normalized run metadata without changing CI behavior.
- **Stage 2 — Correlation:** connect test/package/docs/deploy lanes to the same commit and exact dependency revisions.
- **Stage 3 — Compatibility:** evaluate declared constraints against consumer/provider evidence; label missing evidence as unknown.
- **Stage 4 — Advisory reporting:** provide traceable summaries and actionable links. Keep privileged actions, publishing, and deployment outside CiAi's authority unless separately designed and explicitly approved.

## Acceptance criteria

- Every displayed claim links to an authoritative run, job, artifact, or commit.
- A run is successful only when provider status is completed and conclusion is success.
- Unknown, skipped, cancelled, and missing evidence are not silently converted to success or failure.
- Dependency compatibility includes resolved dependency identity, not only a branch name.
- Duplicate deliveries do not create duplicate evidence; retries/attempts remain distinguishable.
- CiAi remains read-only and cannot publish packages or deploy software.
