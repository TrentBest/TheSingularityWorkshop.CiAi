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
| CodeQL/security workflow | NOT VERIFIED HERE | The inspected workflow run includes runtime assembly security analyzers. This report does not establish a separate CodeQL workflow result for this SHA. |
| NuGet publication | WITHHELD / SKIPPED | The `publish_nuget` job was skipped. No publication authorization is implied. |
| Branch reconciliation | NOT VERIFIED | This report does not establish current `development`/ `master` ancestry or PR merge readiness. |
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

This is a substantial CI milestone, but **not a release authorization**. Before calling Alpha.7 fully release-ready, independently verify a separate CodeQL/security workflow for this SHA if required by the repository's release policy, inspect live branch/PR ancestry and reconciliation state, and obtain explicit owner approval. Do not infer success from missing or skipped evidence.

No FSM_COS files or workflows were changed or triggered by this report. No merge, branch deletion, or NuGet publication was performed.
