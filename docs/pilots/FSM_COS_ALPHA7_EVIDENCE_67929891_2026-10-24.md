# FSM_COS Alpha.7 — exact-SHA evidence report

**Evidence collected:** 2026-10-24  
**Repository:** `TrentBest/TheSingularityWorkshop.FSM_COS`  
**Candidate branch:** `development`  
**Candidate SHA:** `67929891e141598ca5c7f17afeb2feb60612e18c`  
**Workflow run:** [Pack and publish #40064639400](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/actions/runs/40064639400)  
**Run result:** completed; publication job skipped.

> This report is a read-only evidence snapshot. It does not authorize merge, publication, or release. Evidence is scoped to the exact SHA above.

## Category results

| Category | Result | Evidence |
|---|---|---|
| Markdown branding preflight | PASS | Job `build-and-test`, step 3 |
| File-level UML companion coverage | PASS | Job `build-and-test`, step 4 |
| Generated GUI.SVG validation visual | PASS | Generation, committed-visual currency check, and artifact upload succeeded |
| Development dependency restore/build | PASS | `build-and-test` steps 11–15 |
| Runtime assembly security analyzers | PASS | `build-and-test`, step 16 |
| Minimal consumer sample | PASS | `build-and-test`, step 17 |
| Tests and coverage | PASS | `build-and-test`, steps 18–19; test/coverage artifact uploaded |
| NuGet package contents and metadata | PASS | `build-and-test`, steps 21–23 |
| Complete development-source integration | PASS | `source-integration`: source graph restore/build, consumer verification, and tests all succeeded |
| Public NuGet dependency compatibility | PASS | `public-package-compatibility`: public-feed restore, dependency audit, build, consumer verification, tests, pack, package inspection, and artifact upload succeeded |
| CodeQL/security workflow | NOT VERIFIED HERE | The inspected workflow run includes runtime assembly security analyzers. Combined status returned no status checks, and the connector's commit-workflow query only returns PR-triggered runs; neither establishes a separate CodeQL result for this SHA. |
| NuGet publication | WITHHELD / SKIPPED | The `publish_nuget` job was skipped. No publication authorization is implied. |
| Branch inventory | VERIFIED: TWO BRANCHES | Read-only branch listing returned exactly `development` and `master`. No branch was changed or deleted. |
| Branch reconciliation / merge readiness | DIVERGED; OWNER DECISION REQUIRED | Compare of `master` to candidate `67929891...`: status `diverged`; candidate is 1,070 commits ahead and 51 behind. Merge base: `e9c9968b9cdbec0478e77d9f89b4e74a0e404d3d`. Current `master` base ref resolved to `43892fde0703f27146dd81ea33dd8ce4a9ad53b2`. PR [#35](https://github.com/TrentBest/TheSingularityWorkshop.FSM_COS/pull/35) remains open, draft, and non-mergeable. Its body still cites older branch heads and must be refreshed by the repository owner; do not treat those older SHAs as current. |
| Owner release authorization | NOT PROVIDED | Explicit approval remains a separate gate. |

## Artifacts from the exact candidate run

All listed artifacts report the same candidate SHA and run ID.

| Artifact | Artifact ID | Size | Recorded SHA-256 |
|---|---:|---:|---|
| `fsm-cos-nuget-package` | 12549106239 | 133,746 bytes | `f5a3a8a5a71c8f0913bef35754b28658e01b0a6ed36df143f8e9c157bb63bdc7` |
| `fsm-cos-nuget-package-public-feed` | 12548842239 | 133,747 bytes | `5028c3f986b43712985734c53fca3a0eb1c5118f1ed232a68584a233680ea4bc` |
| `fsm-cos-test-results` | 12548349820 | 41,476 bytes | `19186ba707f806fbc871ffd5c4419cd82e1248c8c363590497c153488b3d5f52` |
| `fsm-cos-generated-visuals` | 12548218546 | 1,937 bytes | `86d85f538d9e339ecf39597b4780a5e4367b97ac7e9b7c147c3e6bf2bb5aa29f` |

## Assessment

The exact-SHA Pack and publish workflow is green for build/test, development-source integration, public-package compatibility, and package inspection. The previous Markdown branding preflight blocker is resolved on this candidate.

This is a substantial CI milestone, but **not a release authorization**. Remaining gates are: obtain direct CodeQL workflow evidence for this exact SHA (or explicitly record policy-based disposition), reconcile and preserve useful master-only work under the existing ledger, refresh PR #35's stale branch metadata, and obtain explicit owner approval. Do not routine-merge the divergent histories, force-reset either branch, or delete branches to tidy the list. Do not infer success from missing or skipped evidence.

No FSM_COS files or workflows were changed or triggered by this report. No merge, branch deletion, or NuGet publication was performed.
