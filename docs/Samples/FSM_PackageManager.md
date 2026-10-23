# FSM_PackageManager: CiAi Integration Sample

## Intended role
Package management tooling; test package discovery/version resolution without allowing unintended publication.

This is a proposed integration contract, not a claim that CiAi is already wired into this repository. Confirm purpose and workflow facts against the current source before enforcement.

## Evidence CiAi should consume
- Canonical repository, branch/ref, tested commit SHA, workflow file, trigger/event, run ID and attempt.
- Provider status/conclusion, timestamps, job/step outcomes, and direct links to authoritative logs/artifacts.
- Exact resolved SHA for each sibling source checkout; branch names alone are not reproducible.
- Declared capabilities and their evidence: build, test, package, docs, host-specific validation, deploy, or benchmark as applicable.
- Explicit states for failed, cancelled, skipped, not triggered, unavailable, and unknown; never collapse these into a generic pass/fail.

## How this repository should consume CiAi
1. Keep GitHub Actions or the existing CI provider as the executor and source of truth.
2. Declare repository purpose, package/API surface, supported runtime, dependencies, capabilities, and sample entry points.
3. Expose normalized evidence tied to the tested commit and workflow run; distinguish reruns using run attempt.
4. Use CiAi to correlate lanes and dependencies, produce compatibility summaries, and link back to provider logs.
5. Keep domain-specific tests here; CiAi reports their evidence rather than reimplementing them.
6. Start read-only and advisory. Do not make CiAi a required merge gate until the evidence model is proven.

## Repository-specific priority
Package identity/version parsing, dependency resolution, invalid input handling, and tests that prove no publish action occurs.

## Rollout and acceptance
- Stage 0: inventory workflows, triggers, permissions, package outputs, and dependency edges.
- Stage 1: observe and normalize evidence without changing CI behavior.
- Stage 2: correlate lanes to commit and resolved dependency revisions.
- Stage 3: evaluate declared compatibility constraints and label absent evidence as unknown.
- Stage 4: advisory reporting with traceable links; no implicit publish/deploy authority.
- Every claim links to a run, job, artifact, or commit. Successful means provider status is completed and conclusion is success. Duplicate deliveries are idempotent; retries remain distinguishable.
