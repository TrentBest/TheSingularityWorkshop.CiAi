# CODE RED — implementation status and next steps

> Status snapshot: 2026-11-25. Verified implementation commit: `7eb9727e8854255064f2bfb674ff40cedc0db24b`. This document distinguishes implemented contracts from the next capabilities to build.

## Implemented path

The current CiAi self-hosting path now has a complete, independently observed CI evidence loop:

1. A development push resolves to one exact source SHA and workflow revision.
2. The authoritative CI workflow restores and builds with the pinned .NET 10 SDK, verifies GrammarAi/ProtocolAi source pins, runs the contract harness, and writes genuine TRX results.
3. CI emits a workflow-provenance artifact and a test-result artifact for the exact run/attempt. It packages the current CiAi NuGet candidate into an isolated local feed and builds an external package consumer against that feed.
4. The separate observer resolves the authoritative CI run and attempt for the same source SHA, downloads the exact provenance and TRX artifacts, builds a pinned observer, validates workflow provenance, provider identity, required-job status, archive digest and test diagnostics, then retains JSON and Markdown evidence.
5. The observer does not turn model output or candidate-controlled booleans into a passing verdict. Missing, mismatched, stale, incomplete, or untrusted evidence remains non-passing.

The verified run set for the implementation commit:

- Contract verification: run `45536719071` — success.
- Exact-source evidence observer: run `45536719066` — success.
- Cog ecosystem integration checks: run `45536719218` — success.

The CI workflow produced a package candidate artifact for inspection. **It did not publish to NuGet.**

## Implemented capabilities

- **Workflow provenance and trust:** exact workflow/run/attempt identity, protected workflow-definition comparison, and distinct PR head/base/merge identities.
- **Test-result evidence:** required-job policy, provider artifact binding, digest-verified bounded TRX archive reading, structured test diagnostics, and evidence receipts/outboxes.
- **Foundry handoff contracts:** Foundry artifact-ready notice, delivery coordination, and outbox contracts. These are contracts for the `FSM_Foundry` host boundary; a live host adapter is still future work.
- **Execution planning:** typed workflow plans, deterministic scheduling, fail-closed in-process execution-result contracts, and Pages publication-plan composition.
- **Await/Gate:** durable file-backed registrations and gate contracts plus an event outbox.
- **CI Matrix:** deterministic bounded expansion, stable cell identity, and explicit exclusions.
- **Semantic stack:** typed GrammarAi/ProtocolAi workflow catalogs and cross-reference validation.
- **CommandAi/1 proposals:** deterministic command identity, idempotency keys, and explicit outcomes.
- **Repository foundation:** the existing `CiAiRepositoryBuilder` and repository manifest remain the canonical composition API.
- **Branch policy:** Sith enforces the exact `master` + `development` inventory. The last compliant baseline was run `45539466277`, before this temporary work began. The active PR #92 branch and one superseded, incorrectly based branch currently add temporary refs; the latter is ready for owner deletion, while PR #92's branch must remain until its review disposition.

The 22-family workflow catalog remains a vocabulary inventory. A family appearing in the catalog is not evidence that its runtime, provider adapter, or release gate is implemented.

The pre-work CiAi branch inventory was independently reported compliant by Sith Policy run `45539466277` at `2026-11-25T03:22:15Z`: exactly `master` and `development`. This feature work temporarily adds two refs; the superseded `feature/durable-ci-rerun-requests` branch was mistakenly based on the master snapshot and is ready for owner deletion. The active `feature/durable-ci-rerun-requests-development` branch carries draft [PR #92](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/92) and remains until its review disposition.

## Deferred intent — rebuild from this plan, not from parallel branches

### 1. Durable rerun and dispatch requests

**Implemented on the isolated candidate branch** `feature/durable-ci-rerun-requests-development` in draft [PR #92](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/92); not yet integrated into `development`. The v1 slice adds `CiWorkflowDispatchInputContract`, `CiWorkflowRerunRequest`, and `FileCiWorkflowRerunRequestLedger`, with contract tests and [the request-boundary specification](WORKFLOW_RERUN_REQUEST_V1.md). It binds the canonical repository, workflow YAML path, trusted `refs/heads/development` workflow-definition ref and exact workflow SHA, isolated candidate feature ref and exact candidate SHA, source run/attempt, correlation ID, idempotency key, expiry, and a maximum of three additional attempts. The ledger is append-only, digest-verified, bounded, and idempotent.

This is still request intent only. It does not dispatch, retry, authenticate the requester, or authorize a provider call. The host must independently verify that the trusted workflow revision is approved, that the workflow is registered for manual dispatch, and that the candidate feature ref still resolves to the candidate SHA before each dispatch. The next slice is the FSM_Foundry host adapter and durable correlation to actual provider run IDs/attempts.

### 2. Repository enrollment and README badges

The competing `CiAiRepositoryComposition` API and its README-stamper CLI were intentionally not imported. Extend the existing `CiAiRepositoryBuilder` and manifest rather than introducing a second public composition model.

The intended feature is a deterministic, idempotent managed README region with check-only CI validation. It should support Sith and Autonomous Development status badges, derive status from evidence, and report state rather than perform repository mutations. Enrollment must be explicit per repository; no repository is enrolled merely because it is visible to GitHub.

### 3. External observer profiles

The `CiWorkflowObserverProfile` contract is present, but repository-manifest enrollment and rollout are not complete. Start with CiAi, then the proven raWWar workflow, then AnyApp. Each profile must bind the exact workflow path, required job, artifact templates, provider metadata, and protected workflow revision. A green repository-contract badge is not trusted test evidence and does not authorize autonomous repair.

### 4. FSM_Foundry integration

Connect the Foundry notice contracts to an explicit `FSM_Foundry` adapter. The host owns execution, resource isolation, credentials, cleanup, and authorization. CiAi owns evidence validation and gate policy. HeadlessAi may assess validated evidence, but model output cannot change the verdict. Human approval remains authoritative for merge, NuGet publication, release, and deployment.

### 5. Gauntlet and adversarial execution

Gauntlet remains an explicit opt-in design. Do not execute candidate-controlled code in the CI orchestration job. Production disposable-VM/strong-isolation, resource limits, replay protection, and independent qualification must exist before candidate-supplied probes are enabled.

### 6. Dependency graph and performance aggregation

The dependency-readiness and ecosystem-performance documents describe intended gates. A runtime graph evaluator, bounded fan-out, performance aggregation, and provider-specific required-check policy remain separate implementation tasks.

## Deliberate non-actions

- No NuGet package was published.
- No release or deployment was created.
- No canonical branch was deleted or rewritten.
- No branch was deleted by the assistant.
- No automatic merge, provider rerun/dispatch, or candidate-code execution was authorized by a passing evidence report.
