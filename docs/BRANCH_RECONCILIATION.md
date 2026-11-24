# CiAi Branch Reconciliation

**Policy:** active ecosystem repositories retain exactly `master` and `development`.  
**Audit snapshot:** 2026-11-24 UTC  
**Observed inventory:** 22 branches — 2 canonical and 20 integration/work branches.  
**Open pull requests:** 20.  
**Deletion result:** no branch is currently marked ready to delete.

## Canonical branches

| Branch | Role |
|---|---|
| `master` | Last released / NuGet-aligned state |
| `development` | Active source and integration branch |

## Noncanonical branches and open PR ownership

Every noncanonical branch in this snapshot has an open draft PR. An open PR is not by itself proof that the branch is safe to delete; first preserve or deliberately close/reconcile its work.

| Branch | PR | PR base | Reconciliation note |
|---|---:|---|---|
| `chore/replace-foundry-docs-with-execution` | #88 | `development` | Documentation migration; review against current Execution naming before disposition |
| `docs/ciai-code-red-primary-path` | #41 | `development` | Documentation-only candidate; preserve until content is reconciled |
| `experiment/http-projection-architecture` | #2 | `development` | Old experiment; review whether any unique design content remains |
| `experiment/read-only-ci-evidence-pilot` | #1 | `development` | Old experiment; review whether its evidence pilot was superseded |
| `feature/await-gate-contracts-31` | #32 | `development` | Await/Gate contracts; preserve until reviewed |
| `feature/ciai-external-observer-and-composition` | #77 | `integration/ciai-v1-evidence-contracts` | Child integration; depends on PR #65 |
| `feature/ciai-fluent-ecosystem-builder` | #79 | `development` | Fluent ecosystem composition; preserve until reviewed |
| `feature/ciai-trusted-trx-archive-reader` | #45 | `feature/ciai-trx-diagnostic-verdict` | Nested dependency; reconcile after #36 |
| `feature/ciai-trx-diagnostic-verdict` | #36 | `feature/ciai-workflow-checkout-provenance` | Nested dependency; reconcile after #30 |
| `feature/ciai-versioned-ci-evidence-packet` | #28 | `development` | Parent of provenance branch #30 |
| `feature/ciai-workflow-checkout-provenance` | #30 | `feature/ciai-versioned-ci-evidence-packet` | Nested dependency; reconcile after #28 |
| `fix/commandai-v1-deterministic-encoding-pin` | #50 | `development` | CommandAi deterministic encoding pin; preserve until reviewed |
| `fix/live-provider-pr-head-sha` | #82 | `development` | Overlaps newer current-development follow-up #89; compare before disposition |
| `fix/live-provider-pr-head-sha-current-dev` | #89 | `development` | Newer current-development follow-up; compare against #82 before selecting canonical fix |
| `integration/ciai-v1-evidence-contracts` | #65 | `development` | Current CODE RED evidence integration candidate |
| `work/ciai-executor-result-contract` | #47 | `development` | Fail-closed executor result contracts; preserve until reviewed |
| `work/ciai-matrix-contracts` | #49 | `development` | Bounded matrix expansion and aggregation; preserve until reviewed |
| `work/ciai-self-observation-lane` | #53 | `development` | Self-observation workflow; preserve until reviewed |
| `work/ciai-semantic-stack-foundation` | #60 | `development` | ProtocolAi/GrammarAi workflow foundation; preserve until reviewed |
| `work/ecosystem-graph-performance-contract` | #55 | `development` | Dependency graph/performance contracts; preserve until reviewed |

## Dependency order

The nested PR chain must be reconciled parent-first:

1. #28 `feature/ciai-versioned-ci-evidence-packet`
2. #30 `feature/ciai-workflow-checkout-provenance`
3. #36 `feature/ciai-trx-diagnostic-verdict`
4. #45 `feature/ciai-trusted-trx-archive-reader`

PR #77 is based on the active PR #65 integration branch and must be reconciled after its parent. The two live-provider fixes (#82 and #89) overlap and need a commit-level comparison before either branch can be retired.

## Deletion readiness

**Ready to delete: none.**

The old experiments (#1 and #2) and the older live-provider fix (#82) are *review candidates*, not deletion-ready. Their PRs are still open and their unique commits have not been reconciled into `development`.

No branches were deleted during this audit. After each PR is merged or deliberately closed and its unique work is preserved or rejected, the surplus branches should be removed together in one owner-approved cleanup batch. Do not delete a branch merely because its PR is old, draft, or behind.
