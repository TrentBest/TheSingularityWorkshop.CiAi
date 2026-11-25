# Sith Policy Remediation — CiAi

> Snapshot recorded 2026-11-25. This is an audit aid, not a live inventory. Re-run the Sith Policy workflow and refresh this document before acting on any branch.

## Required steady state

The repository must contain exactly these two permanent branches:

- `development` — active source and integration branch.
- `master` — released / NuGet-aligned state.

No third permanent branch is permitted. The repository owner performs branch deletion; this document does not authorize or perform deletion, merging, closing PRs, or changing branch protection.

## Verified inventory

The Sith Policy workflow ran against commit `a3fdc85a265d2298a85cb5172b8d32a63db0fbb1` on 2026-11-25. Its GitHub branch API request succeeded. It found **22 branches**, so the policy result was correctly **NON-COMPLIANT** (20 excess branches).

The 20 excess branches were each compared with `development`. Every one was divergent and had commits ahead of `development`; none is safe to classify as deletable from age or branch count alone. Each also has an open draft PR at the time of this snapshot.

| Excess branch | Open PR | PR base | Commits ahead of development | Behind development |
|---|---:|---|---:|---:|
| `chore/replace-foundry-docs-with-execution` | [#88](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/88) | `development` | 4 | 52 |
| `docs/ciai-code-red-primary-path` | [#41](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/41) | `development` | 5 | 169 |
| `experiment/http-projection-architecture` | [#2](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/2) | `development` | 1 | 478 |
| `experiment/read-only-ci-evidence-pilot` | [#1](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/1) | `development` | 1 | 482 |
| `feature/await-gate-contracts-31` | [#32](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/32) | `development` | 51 | 172 |
| `feature/ciai-external-observer-and-composition` | [#77](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/77) | `integration/ciai-v1-evidence-contracts` | 274 | 106 |
| `feature/ciai-fluent-ecosystem-builder` | [#79](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/79) | `development` | 7 | 58 |
| `feature/ciai-trusted-trx-archive-reader` | [#45](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/45) | `feature/ciai-trx-diagnostic-verdict` | 332 | 172 |
| `feature/ciai-trx-diagnostic-verdict` | [#36](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/36) | `feature/ciai-workflow-checkout-provenance` | 317 | 172 |
| `feature/ciai-versioned-ci-evidence-packet` | [#28](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/28) | `development` | 204 | 172 |
| `feature/ciai-workflow-checkout-provenance` | [#30](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/30) | `feature/ciai-versioned-ci-evidence-packet` | 276 | 172 |
| `fix/commandai-v1-deterministic-encoding-pin` | [#50](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/50) | `development` | 17 | 166 |
| `fix/live-provider-pr-head-sha` | [#82](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/82) | `development` | 2 | 58 |
| `fix/live-provider-pr-head-sha-current-dev` | [#89](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/89) | `development` | 2 | 52 |
| `integration/ciai-v1-evidence-contracts` | [#65](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/65) | `development` | 334 | 52 |
| `work/ciai-executor-result-contract` | [#47](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/47) | `development` | 13 | 167 |
| `work/ciai-matrix-contracts` | [#49](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/49) | `development` | 11 | 167 |
| `work/ciai-self-observation-lane` | [#53](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/53) | `development` | 6 | 166 |
| `work/ciai-semantic-stack-foundation` | [#60](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/60) | `development` | 28 | 161 |
| `work/ecosystem-graph-performance-contract` | [#55](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/pull/55) | `development` | 2 | 166 |

## Stacked PR dependencies

These PRs are not independent branch-cleanup candidates. Their base branches are themselves excess branches:

1. `integration/ciai-v1-evidence-contracts` → PR #65 targets `development`; PR #77 is based on that integration branch.
2. `feature/ciai-versioned-ci-evidence-packet` → PR #28 targets `development`; PR #30 is based on it.
3. `feature/ciai-workflow-checkout-provenance` → PR #30; PR #36 is based on it.
4. `feature/ciai-trx-diagnostic-verdict` → PR #36; PR #45 is based on it.

The live-provider branches are related but not interchangeable based on the available compare evidence: #89 has a different commit set from #82. Treat #89 as a proposed reconciliation, not proof that #82 can be discarded.

## Remediation sequence

1. Keep `master` and `development` as the only permanent branches.
2. Review the open PR stack from its root toward its leaves. For each PR, decide whether its unique work is required, already present in `development`, superseded, or intentionally abandoned.
3. Reconcile required work onto `development` in coherent slices; do not blind-merge old, heavily diverged branches.
4. Close superseded/abandoned PRs only after confirming their unique work is either preserved or intentionally discarded.
5. Once a branch has no required unique work, no dependent PR, and no remaining consumer, mark it **READY FOR OWNER DELETION**. The repository owner performs the deletion.
6. Re-run Sith Policy after each cleanup batch. The target is exactly 2 branches, not merely a passing workflow run.

## Enforcement interpretation

The existing Sith Policy workflow correctly distinguishes:

- `COMPLIANT`: exact inventory `development` + `master`.
- `NON-COMPLIANT`: inventory succeeded and found any other branch set.
- `UNVERIFIED`: inventory could not be obtained.

The 2026-11-25 run was **NON-COMPLIANT**, not an API failure. Preserve that fail-closed distinction. Never convert an unavailable inventory into a compliant result.
