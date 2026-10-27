# CiAi Release Readiness

**Status: alpha candidate; not approved for publication.** Readiness is tracked by explicit gates rather than percentage estimates without a defined scoring model.

## Candidate

- Package: `TheSingularityWorkshop.CiAi`
- Version: `0.1.0-alpha.1`
- Target: `.NET 8`
- License: MIT
- External dependencies in the core library: none
- Release branch: `development`; the release candidate must be reviewed before any merge or publication.

## Next design direction — recipe-style CI and governed publication

The [declarative CI builder design](CI_BUILDER_DESIGN.md) describes repository-owned, readable pipeline recipes; typed step outcomes; explicit trigger/condition semantics; mapping observed evidence to declared checks; and a separate, candidate-bound owner approval gate for privileged actions such as NuGet publication. The implementation has progressed beyond definition-only models: `CiPipelineExecutor` runs registered in-process delegates sequentially with bounded capability checks and recorded outcomes, while `CiRequiredEvidenceEvaluator` checks required facts against an exact repository and full source SHA. These are in-process building blocks, not yet a provider-hosted end-to-end CI policy system; there is no publishing adapter.

The next progression is deterministic reporting and host integration that connects declared policy to actual consumer build/test/coverage/docs/benchmark outcomes, followed by a separately reviewed opt-in publishing adapter only if needed. A green Observer report, passing evaluation, or custom step returning true must never substitute for owner authorization.

## Implemented

- Initial release-artifact inventory verifier for NuGet candidates: checks the exact package ID/version's `.nupkg` and `.snupkg` relative paths, produces deterministic missing-output diagnostics, and rejects rooted/traversal paths. Seven contract checks cover complete inventories, missing symbols, version mismatches, nested artifact folders, portable case comparison, unsafe paths, and unsafe ID/version segments. The verifier is a local inventory contract; a manual inspection of the candidate artifact now confirms both expected files and the symbol package's PDB, but automated artifact download/content inspection is not yet implemented in CiAi.

- Declarative CI definition and execution slices: immutable pipeline/trigger/step metadata, generic `Add<T>` registration, deterministic recipe rendering, duplicate step-ID rejection, and a single-use builder; plus `CiPipelineExecutor` for sequential in-process delegate execution, bounded capability checks, and recorded step outcomes. `CiRequiredEvidenceEvaluator` evaluates required facts against exact repository/full-SHA identity. Contract tests protect builder non-invocation, stable rendering/ordering, duplicate IDs, privileged-step metadata, builder lifecycle, executor behavior, and fail-closed evidence. These components do not yet make a GitHub Actions host execute the policy or provide end-to-end consumer CI governance, and no publication adapter exists.
- Typed, provider-neutral `CiRunSnapshot` and finite status/conclusion vocabulary.
- Typed `ReleaseEvidenceRecord` and `ReleaseEvidenceReport` now model normalized evidence states while retaining raw provider status/conclusion, exact repository+SHA identity, run/attempt/workflow/job, PR, diagnostics, retrieval timestamp, artifact name/digest scope, and direct-verification flag. The aggregator rejects mixed repository/SHA collections, keeps absent categories `Missing`, and never treats skipped or unverified evidence as passing. Seven offline contract checks cover these invariants. The separate read-only observer now discovers matching workflow runs by exact repository + full SHA, collects jobs and steps for each run attempt with bounded pagination, emits JSON and Markdown reports, and fails closed when discovery or job evidence is incomplete. This automates evidence collection, not release interpretation or authorization.
- Read-only GitHub Actions REST client with request cancellation support.
- Returned repository and run-ID correlation.
- Shared bounded canonical repository identity validation used by both the HTTP client and snapshot model; rejects malformed path segments and overlong components before network access.
- Regression test for overlong repository identities; current client suite contains 13 offline integration checks.
- Repository identity class companion and read-only observation UML/use-case documentation.
- `CiRunEvidenceVerifier` reconciles a provider-fetched snapshot against coordinator-owned expected repository, run ID, and commit SHA; it rejects mismatches, incomplete runs, and non-success conclusions.
- Offline regression checks cover exact reconciliation plus wrong repository, run ID, SHA, and failed-run rejection. See the [verifier class companion](../src/TheSingularityWorkshop.CiAi/CiRunEvidenceVerifier.md) and [UML/use case](UML/CiRunEvidenceVerifierUML.md).
- Full 40-character hexadecimal commit SHA validation.
- Exact HTTPS GitHub Actions evidence path validation; rejects query strings, fragments, non-default ports, and user-info.
- Unknown future provider conclusions remain explicitly unknown and cannot be successful.
- Offline contract tests and a console observation sample.
- Typed `ArtifactManifest`, `ArtifactReadyNotice`, and `ArtifactReceiptAcknowledgement` contracts; validate request/artifact correlation fields, SHA-256 digest shape, HTTPS retrieval URI shape, recipient binding, contract version, expiry, and acknowledgement state consistency.
- Fourteen offline handoff-contract checks cover manifest/notice/receipt invariants and receipt-to-manifest reconciliation.
- `ArtifactPayloadVerifier` checks actual caller-supplied bytes against manifest size and SHA-256, with a default 16 MiB cap, caller-configurable stricter limits, fixed-time digest comparison, and stable failure codes. See [verifier class companion](../src/TheSingularityWorkshop.CiAi/ArtifactPayloadVerifier.md).
- Five payload-verifier checks cover valid bytes, digest mismatch, size mismatch, maximum-size rejection, and invalid maximum size. `ArtifactPayloadReader` adds bounded stream reads, cancellation, short-stream rejection, and no ownership transfer for caller streams.
- Receipt evidence now reconciles provider repository, provider run ID, and source revision as well as request/artifact/version/digest/client identity.
- `ArtifactRetrievalDestinationPolicy` adds a preflight allowlist for configured DNS hostnames, HTTPS/default-port requirements, no user-info/fragments/IP literals, and artifact expiry. Offline checks cover allowed and denied hosts, insecure schemes, unusual ports, expired artifacts, IP literals, and trailing-dot hostnames. This is not a standalone SSRF defense: the future transport must also enforce DNS/IP restrictions, redirect policy, provider-scoped authentication, timeouts, and byte bounds.
- `FileArtifactReceiptLedger` persists receipt records, suppresses exact retries across ledger reopen, rejects conflicting reuse of a deduplication key, and refuses receipts whose manifest provenance does not match. Its file write is flushed to disk and cross-process writers are serialized by an exclusive lock file; it does not perform provider cleanup.
- The package-content candidate SHA `82b685d88b4f44947061d356855eed201b6dadeb` passed 46 offline CiAi contract checks plus 13 GitHub Actions client integration checks, package packing, metadata validation, and artifact upload in [workflow run 40056723549](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40056723549). The pinned cross-Cog vertical proof passed seven checks in [workflow run 40056549849](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40056549849) on commit `33476c46baca395a3132273d0555970fc2a72d64`.
- Source companion docs and UML/use-case diagram for the three handoff contracts.
- MIT license, package metadata, embedded README, symbol-package settings, pinned GitHub Actions workflow.
- CI builds, runs offline contract checks, packs the NuGet candidate, and validates the packed artifact.
- Standalone CogA–CogE slices now have independent contract suites and package-candidate packing checks. The [cross-Cog integration workflow](../.github/workflows/cog-ecosystem.yml) checks out each Cog at a pinned commit and proves catalog lookup → default-deny policy decision → bounded idempotent request → result correlation → parent payload/receipt verification → lifecycle projection.
- The [external-source UML pilot workflow](../.github/workflows/uml-external-source-pilot.yml) successfully generated and validated a syntax-only structural model and SVG from CogA at pinned source revisions in [run 40059565443](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40059565443), [run 40059572421](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40059572421), and [run 40059675484](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40059675484). The artifact includes source provenance and output hashes. This is a bounded external-source pilot, not yet a reusable renderer-neutral UML package, multi-project semantic analysis, or AI-generated use-case capability; no LLM is invoked.

## Must pass before publication

- [x] Baseline build, offline contract checks, pack, and packed-artifact validation passed on commit `f207fc028a79a19305c7296a9e930c06b64ad40d` in [workflow run 40040017424](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40040017424).
- [x] Package-content candidate `82b685d88b4f44947061d356855eed201b6dadeb` passed build, 46 offline contract checks, pack, packed-artifact validation, and artifact upload in [workflow run 40056723549](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40056723549).
- [x] The pinned cross-Cog vertical contract proof passed seven integration checks in [workflow run 40056549849](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40056549849). This proves the in-process contract mapping only, not remote network handoff.
- [x] Independently inspected the `.nupkg` and `.snupkg` from candidate workflow run 40056723549 (artifact ID `12545093556`). The package contains the library DLL/XML docs, embedded README, and nuspec with ID `TheSingularityWorkshop.CiAi`, version `0.1.0-alpha.1`, MIT license, README declaration, and repository commit `82b685d88b4f44947061d356855eed201b6dadeb`. Embedded README includes all five Cog links and the vertical proof link. Package SHA-256: `7c12ed9057b0f629f749efe794cf8b92fac6bdacccd13e27256cd200103001d9`; symbols SHA-256: `aaff3e985dca9f853eb4566b97a5c2389836d7e86e1b64f9425be4e6eff08534`.
- [x] Verified the latest `development` candidate from [contract workflow run 40164496646](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40164496646), commit `3c2cb7db305514d1e4424df786d0dff540f7853b`: restore, build, contract checks, pack, package validation, and artifact upload all passed. Artifact ID `12587945917` (`ciai-package-candidate`, 69,255 bytes; ZIP SHA-256 `38c81797e07921bfd179da44358bad157207ce1c22e5f1e67061992b0812de16`) was downloaded and inspected. It contains `TheSingularityWorkshop.CiAi.0.1.0-alpha.1.nupkg` (51,771 bytes) and `.snupkg` (17,913 bytes); the `.nupkg` includes the library DLL/XML docs, README, and nuspec declaring the expected ID/version/MIT/repository commit, and the `.snupkg` contains `lib/net8.0/TheSingularityWorkshop.CiAi.pdb`. The cross-Cog integration workflow also passed in [run 40164496613](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40164496613). This validates the candidate and artifact contents, not publication readiness or owner authorization.
- [ ] Review README and public API claims against actual implementation.
- [ ] Confirm package ID and version are intended and not already published.
- [ ] Review NuGet account/credential setup and release permissions outside source control.
- [ ] Obtain explicit owner approval to publish.

## Deliberately not claimed

This alpha remains read-only with respect to CI-provider operations. Typed handoff contracts, bounded caller-supplied stream reads, local byte-integrity verification, provider provenance reconciliation, and a durable local receipt deduplication ledger now exist. The client still does not fetch the manifest URI or authenticate the provider, and there is no end-to-end authoritative provider CI correlation. The in-process catalog-to-request-to-result vertical proof now exists, but it does not prove remote retrieval, authenticated notification/acknowledgement, or provider run verification. HeadlessAi invocation, ProtocolAi vocabulary integration, host authorization, cross-repository dispatch, persistent idempotency/history, and a complete request/result round trip are not implemented in CiAi. No publish or deploy action is exposed by this library.

## Release gate

A green CI run proves that the exact candidate builds, passes its offline tests, and can be packed; independent artifact inspection checks the package produced by that run. Neither authorizes publishing nor proves an end-to-end LLM workflow. Re-run CI and inspect the newly produced artifact after any source or workflow change. The remaining gates are public-package/version confirmation, external NuGet credential and permission review, API/README owner review, and explicit owner approval.


## Exact-SHA workflow collector and first FSM_COS report — 2026-10-24

- Added GitHubActionsWorkflowRunCollector: read-only GET-based discovery using explicit repository + full SHA, bounded to 1–10 pages and 1–100 rows/page, with independent per-row repository/SHA validation and explicit truncation state. Run attempt, workflow path, event, PR numbers, raw status/conclusion, timestamp, and evidence URL are retained. It does not collapse attempts or mutate provider state.
- Added six offline collector checks for query identity, run-attempt/workflow/PR retention, wrong repository, wrong SHA, invalid attempt, page-cap truncation, and invalid request bounds.
- [Contract verification passed](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40063755622) on commit be145b1275492fc3478a2fbcf62c724774788b16. [Cross-Cog workflow also passed](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40063755601) on that commit. These are CiAi validation results, not evidence of FSM_COS readiness.
- First live report: [FSM_COS Alpha.7 evidence pilot](pilots/FSM_COS_ALPHA7_EVIDENCE_2026-10-24.md), targeting PR #35 candidate dcea776f40f507f1d95a6e184f475e57853d2baa. Package workflow failed at Markdown branding preflight; CodeQL, public-package compatibility, and source integration passed; build/test and package inspection were skipped; publication was skipped as intended; owner authorization remains missing.
- The console observer now combines exact-SHA workflow discovery with per-attempt job/step collection and generates both `commit-observation.json` and `commit-observation.md`; it treats zero matching runs, truncated run discovery, missing/empty job lists, or incomplete job pagination as an incomplete observation (non-zero exit). This removes the previously documented gap around automated job pagination/attempt reconciliation and report generation. The report remains a raw observation, not a normalized release-evidence verdict: category mapping against a target repository's required checks, interpretation of security findings, and owner approval are still separate gates. The sample's run-ID mode remains a simple single-run display path.

Readiness remains tracked by explicit release gates rather than unsupported percentage estimates. The live end-to-end report path is not yet fully automated. NuGet publication remains unapproved and must not be triggered.

## Job pagination correction — 2026-10-27

The attempt-specific GitHub Actions job collector now stops when the provider-reported `total_count` has been collected, even when the final page is exactly full. This avoids an unnecessary extra API request that could fail after all reported jobs have already been received. It still uses bounded pagination, marks the collection truncated when the page budget ends before the reported total, sorts returned jobs by stable job ID, and rejects duplicate job IDs across pages.

The pagination contract suite now covers multi-page collection, ascending page requests, provider-total completion, page-cap truncation, and duplicate IDs, alongside run/attempt/URL correlation and step-order preservation. The collector and regression tests are now on `development`; [CiAi Contract Verification](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40460505134) and [Cog Ecosystem Integration](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40460505108) both passed on the implementation/test commit `46328782a8feddf944e904608d441b91c10a6f21`. The subsequent documentation-only commit updates this readiness record. This is a collector correctness fix; it does not expand the observer into a release verdict or grant publication authority.
