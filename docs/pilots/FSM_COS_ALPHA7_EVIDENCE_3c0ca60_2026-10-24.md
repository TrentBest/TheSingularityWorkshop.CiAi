# FSM_COS Alpha.7 — exact-SHA evidence report

**Evidence collected:** 2026-10-24 05:48–06:16 UTC  
**Repository:** `TrentBest/TheSingularityWorkshop.FSM_COS`  
**Candidate branch:** `development`  
**Candidate SHA:** `3c0ca60a0baae665b8871b2a2aacacf8827976c9`  
**Release-gate PR:** [#35](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/pull/35)  
**Pack/build/test run:** [40067625575](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575)  
**CodeQL run:** [40067625528](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625528)

> Read-only evidence snapshot. This does not authorize merge, branch replacement, publication, or release. All CI evidence below is tied to the exact SHA above.

## Exact-SHA results

| Gate | Result | Direct evidence |
|---|---|---|
| Markdown branding preflight | PASS | `build-and-test`, step “Verify Workshop branding on every Markdown document” |
| UML companion coverage | PASS | `build-and-test`, step “Verify FSM_COS UML file-level companion coverage” |
| Generated GUI.SVG validation visual | PASS | Visual generation, currency verification, and artifact upload succeeded |
| Development dependency restore/build | PASS | Development dependency restore and build succeeded |
| Runtime assembly security analyzers | PASS | Analyzer step succeeded |
| Minimal consumer sample | PASS | Consumer verification succeeded |
| Tests and coverage | PASS | Test-with-coverage and coverage preparation succeeded; test artifact uploaded |
| NuGet package contents and metadata | PASS | Pack and package-content/metadata inspection succeeded |
| Complete development-source integration | PASS | [Job 120794085164](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575/job/120794085164): source graph restore/build, consumer verification, tests all succeeded |
| Public NuGet dependency compatibility | PASS | [Job 120794085284](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575/job/120794085284): public-feed restore, dependency audit, consumer build/test, pack, and package inspection succeeded |
| CodeQL/security | PASS | [Job 120794085005](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625528/job/120794085005): C# analysis and SARIF retention succeeded |
| NuGet publication | WITHHELD / SKIPPED | `publish_nuget` job 120794255110 was skipped; no publication is implied |
| Branch reconciliation / merge readiness | OWNER DECISION REQUIRED | PR #35 remains open, draft, and non-mergeable; histories are intentionally not being routine-merged |
| Explicit owner release authorization | NOT PROVIDED | Must remain a separate gate |

## Artifacts observed

All were unexpired at collection time and came from the exact candidate's workflow runs.

| Workflow run | Artifact | ID | Size |
|---|---|---:|---:|
| [Pack/build/test](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575) | `fsm-cos-test-results` | 12550126607 | 41,328 bytes |
| [Pack/build/test](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575) | `fsm-cos-nuget-package` | 12550091701 | 133,750 bytes |
| [Pack/build/test](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575) | `fsm-cos-nuget-package-public-feed` | 12550071690 | 133,751 bytes |
| [Pack/build/test](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625575) | `fsm-cos-generated-visuals` | 12549648677 | 1,937 bytes |
| [CodeQL](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40067625528) | `codeql-sarif-results` | 12550071756 | 40,191 bytes |

Artifact digests are not asserted here because this observation did not independently download and re-hash these artifacts.

## Assessment and remaining gates

The current candidate has fresh green build/test, development-source integration, public-package compatibility, package inspection, and CodeQL evidence. The earlier branding preflight issue is resolved for this SHA. Package creation is verified; publication remains skipped.

Before release:
1. Reconcile and preserve useful `master`-only work in the repository's branch reconciliation ledger.
2. Refresh PR #35's stale candidate metadata to reflect live refs; do not routine-merge the divergent histories.
3. Obtain explicit owner review and release authorization.
4. Only after explicit authorization may the NuGet publishing gate be changed for this specific release.

No branch was changed or deleted, no PR was mutated, no workflow was dispatched or rerun, and no NuGet package was published to produce this report.
