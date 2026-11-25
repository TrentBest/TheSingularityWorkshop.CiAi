# Sith Policy Remediation — CiAi

> Snapshot recorded 2026-11-25 against development commit `7eb9727e8854255064f2bfb674ff40cedc0db24b`. This is an owner-action checklist, not a live inventory.

## Required steady state

Exactly two permanent branches:

- `development` — active source and integration branch.
- `master` — released / NuGet-aligned state.

No third permanent branch is permitted. The repository owner performs branch deletion. The assistant has not deleted any branch.

## Current inventory

The latest verified inventory still contains **22 branches**: `master`, `development`, and the 20 excess refs below. All 20 associated PRs are now closed after their useful implementation was reconciled into `development` or their remaining intent was recorded in [CODE RED implementation status and next steps](CODE_RED_NEXT_STEPS.md).

**READY FOR OWNER DELETION — 20 branches**

| Branch | PR | Disposition |
|---|---:|---|
| `chore/replace-foundry-docs-with-execution` | #88 | Foundry docs corrected to the canonical `FSM_Foundry` name |
| `docs/ciai-code-red-primary-path` | #41 | CODE RED primary path and AnyApp provenance docs integrated |
| `experiment/http-projection-architecture` | #2 | Projection model proposal integrated into docs |
| `experiment/read-only-ci-evidence-pilot` | #1 | Read-only evidence pilot integrated |
| `feature/await-gate-contracts-31` | #32 | Await/Gate stores, contracts, outbox and tests integrated |
| `feature/ciai-external-observer-and-composition` | #77 | Trust, PR identity, Foundry notices and exact artifact observer integrated; competing composition/stamper API deferred and documented |
| `feature/ciai-fluent-ecosystem-builder` | #79 | Superseded by the richer canonical `CiAiRepositoryBuilder` on development |
| `feature/ciai-trusted-trx-archive-reader` | #45 | Digest-verified TRX archive capability integrated |
| `feature/ciai-trx-diagnostic-verdict` | #36 | Structured TRX diagnostics and required-job policy integrated |
| `feature/ciai-versioned-ci-evidence-packet` | #28 | Versioned evidence packet integrated; unique rerun/dispatch ledger intent documented for later |
| `feature/ciai-workflow-checkout-provenance` | #30 | Exact workflow provenance integrated; competing V3 packet/outbox line not retained |
| `fix/commandai-v1-deterministic-encoding-pin` | #50 | CommandAi/1 deterministic proposal contract, tests and upstream pin integrated |
| `fix/live-provider-pr-head-sha` | #82 | Superseded by the current-development exact-head fix |
| `fix/live-provider-pr-head-sha-current-dev` | #89 | Exact PR-head candidate identity fix integrated |
| `integration/ciai-v1-evidence-contracts` | #65 | Core evidence, provenance, TRX, gate and artifact contracts integrated |
| `work/ciai-executor-result-contract` | #47 | Fail-closed executor results and workflow scheduler integrated |
| `work/ciai-matrix-contracts` | #49 | Deterministic bounded matrix expansion integrated |
| `work/ciai-self-observation-lane` | #53 | Superseded by the exact-run provenance/TRX observer workflow |
| `work/ciai-semantic-stack-foundation` | #60 | GrammarAi/ProtocolAi typed catalogs and package-consumer validation integrated |
| `work/ecosystem-graph-performance-contract` | #55 | Dependency/performance architecture contract and docs/** CI path coverage integrated |

## Why these refs are safe to remove

- All associated PRs are closed.
- Their required code, tests, and documentation were integrated into `development`, or their distinct remaining intent was deliberately captured as a future task.
- No open PR depends on any of these branches.
- The stacked evidence chain (#28 → #30 → #36 → #45) was consolidated into the current V2 evidence, provenance, report, and archive implementation; the unique rerun/dispatch ledger is explicitly deferred.
- PR #77 was retargeted to development before selected trust, provider identity, Foundry notice, and observer work was reconciled. The competing repository-composition/stamper API was not imported because development already has a canonical builder.

## Owner cleanup sequence

1. Delete the 20 listed branches in one owner-controlled batch.
2. Re-run the Sith Policy workflow.
3. Confirm the inventory is exactly `master` + `development`.
4. Only then should the Sith badge become compliant. Do not weaken the workflow or manually override the result.

## Enforcement interpretation

- `COMPLIANT`: exact inventory `development` + `master`.
- `NON-COMPLIANT`: inventory succeeded and found any other branch set.
- `UNVERIFIED`: inventory could not be obtained.

The latest policy run still reports `NON-COMPLIANT` because the 20 refs remain. This is the correct fail-closed result. No NuGet package was published and `master` was not changed.
