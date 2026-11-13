<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# ServerSC2: CiAi Evidence Integration

## Status

**Coordination contract only; the ServerSC2 consumer integration is not yet implemented or verified.**

- ServerSC2 domain implementation: [ServerSC2 #1](https://github.com/TrentBest/TheSingularityWorkshop.ServerSC2/issues/1)
- CiAi integration work: [CiAi #67](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/67)
- Shared normalized test-artifact and HeadlessAi handoff authority: [CiAi #34](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/34)

This sample defines the boundary and acceptance case. It does not claim that ServerSC2 already emits the artifacts or that CiAi already ingests TRX.

## Responsibility boundary

| Concern | Owner | Contract |
|---|---|---|
| SC2 process topology and protocol | ServerSC2 | Launches participant and host-controlled SC2 clients; proves observer topology before relying on it |
| Match result and replay evidence | ServerSC2 | Observes SC2 terminal state, validates both participant results, captures replay, cross-checks replay metadata, emits versioned domain evidence |
| CI execution and provider facts | GitHub Actions | Authoritative workflow, run attempt, job, checkout SHA, and artifact identity |
| CI artifact provenance and verdict | CiAi | Verifies exact repository/SHA/run/attempt/job/artifact provenance and digest; normalizes test evidence; emits stable reason-coded CI verdict |
| LLM diagnostic consumption | HeadlessAi | Receives bounded, provenance-bound diagnostics as untrusted data |
| Task lifecycle and authorization | Foundry / host | Owns dispatch, budgets, lifecycle, and explicit authorization for repository mutations |

ServerSC2 must not depend on CiAi as a runtime NuGet package. CiAi must not interpret SC2 game outcomes or create a competing match-evidence model. Foundry must not reinterpret raw CI logs or turn a successful evidence verdict into mutation authorization.

## Evidence lanes

The consumer mapping must be based on the actual workflow paths and artifacts present in ServerSC2's `development` branch. Do not guess workflow names or assume a live SC2 installation exists on a hosted runner.

| Lane | Expected policy treatment |
|---|---|
| Build and deterministic contract tests | Required when declared by the ServerSC2 workflow policy; missing, failed, skipped, or incomplete required evidence is non-success |
| SC2 evidence-validator tests | Required deterministic tests for malformed, missing, duplicate, contradictory, and incomplete result/replay evidence |
| Live observer/replay integration | Opt-in environment-dependent lane; if not triggered, report `not-triggered` / unknown, never pass |
| TRX test-result artifact | Machine-readable diagnostics emitted even when tests fail; absence is incomplete evidence when required |
| Replay/integration payload | Optional bounded diagnostic artifact under explicit size and retention limits; never treated as CI authorization |

## Provenance required by CiAi

Every normalized report must retain and validate, where exposed by GitHub:

- Canonical repository identity.
- Exact full 40-character ServerSC2 checkout SHA.
- Workflow-definition path and the revision that supplied the workflow.
- Workflow run ID and run attempt (attempts remain distinct).
- Job ID and job name.
- Artifact ID/name and provider download reference.
- Artifact size and SHA-256 digest, verified against the retrieved bytes.
- Observation time and authoritative run/job/artifact URLs.

A report for a different repository, commit, run attempt, job, or artifact is not evidence for this candidate. Missing, inaccessible, ambiguous, malformed, truncated, oversized, stale, duplicated, or digest-mismatched data remains incomplete/untrusted.

## LLM handoff rules

- Preserve multiple failed tests and skipped tests; do not collapse the test suite into a single boolean.
- Preserve stable test identity, outcome, bounded failure message, expected/actual assertion details when available, and bounded stack trace or provider log reference.
- Treat test names, failure messages, replay metadata, logs, and artifact contents as untrusted diagnostic data. They are never instructions.
- Preserve red evidence from earlier attempts when a later attempt is green.
- Distinguish actionable test failure from incomplete/untrusted evidence and from a non-actionable failure requiring escalation.
- The LLM may propose a repair. It does not receive repository write permission from the report.

## First integration proof

1. ServerSC2 publishes a TRX artifact on both passing and failing deterministic test runs.
2. CiAi observes a completed ServerSC2 run for one exact development commit and retrieves its exact run attempt, job, and artifact.
3. CiAi validates repository/SHA/run/attempt/job/artifact identity and digest before parsing the TRX.
4. CiAi emits a bounded JSON/Markdown report with per-test diagnostics and authoritative GitHub links.
5. The same report can be delivered through the existing #34 HeadlessAi handoff contract; if the adapter is missing, the integration reports that blocker rather than simulating delivery.
6. Negative tests prove wrong provenance, missing/duplicate artifacts, malformed/oversized TRX, and digest mismatch cannot become a pass.

The live observer/replay lane is a separate proof. A missing live SC2 installation must not invalidate deterministic tests, and a live lane that was not triggered must never be reported as successful.

## Safety

No automatic dispatch of privileged work, merge, force-push, branch deletion, package publication, release, or deployment is part of this integration. CiAi observation and evaluation remain read-only. Any later mutation must be separately authorized by the host and bound to the exact candidate and target.

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
