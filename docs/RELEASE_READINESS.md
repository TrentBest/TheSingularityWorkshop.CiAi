# CiAi Release Readiness

**Implementation readiness: 42%. Package preparation: 70%.** This is an alpha package candidate, not a publication approval.

## Candidate

- Package: `TheSingularityWorkshop.CiAi`
- Version: `0.1.0-alpha.1`
- Target: `.NET 8`
- License: MIT
- External dependencies in the core library: none
- Release branch: `development`; the release candidate must be reviewed before any merge or publication.

## Implemented

- Typed, provider-neutral `CiRunSnapshot` and finite status/conclusion vocabulary.
- Typed `ReleaseEvidenceRecord` and `ReleaseEvidenceReport` now model normalized evidence states while retaining raw provider status/conclusion, exact repository+SHA identity, run/attempt/workflow/job, PR, diagnostics, retrieval timestamp, artifact name/digest scope, and direct-verification flag. The aggregator rejects mixed repository/SHA collections, keeps absent categories `Missing`, and never treats skipped or unverified evidence as passing. Seven new offline contract checks cover these invariants. This is the aggregation model only; the separate exact-SHA observer described below discovers workflow runs and collects attempt-specific job/step evidence. A current live observation of the intended FSM_COS alpha.7 candidate has not yet been verified.
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
- Added `GitHubActionsWorkflowJobCollector`: read-only, bounded pagination against the explicit run-attempt endpoint; preserves raw job/step facts, rejects mismatched run/attempt metadata and malformed evidence URLs, and detects duplicate job IDs across pages.
- Added `CommitObservationMarkdownRenderer` and the `ObserveRun` sample to emit paired JSON and Markdown observations for a requested repository + full commit SHA. The report distinguishes observation completeness from release success and carries an explicit non-authorization warning.
- Added regression coverage for job pagination, page-cap truncation, duplicate job IDs, run/attempt mismatches, evidence URL validation, and step ordering on branch `fix/observer-job-pagination-contract`. Both [CiAi Contract Verification](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40147706515) and [Cog Ecosystem Integration](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40147706490) passed on commit `1f5bc179f250a25a3d5be1e78b4be861f3025775`.
- The 2026-10-24 pilot report above is historical evidence for its older SHA, not a live report for the currently intended candidate. The observer workflow still needs to be dispatched for the exact candidate SHA and its uploaded artifact reviewed; the available GitHub connector actions in this session cannot dispatch workflow runs.

Readiness estimates remain **42% implementation / 70% package preparation**; these are provisional and have not been fully re-audited. The latest observer pagination patch has passed both CiAi CI gates on commit `1f5bc179f250a25a3d5be1e78b4be861f3025775`. The live observation workflow has not been dispatched for the intended FSM_COS candidate SHA `3c0ca60a0baae665b8871b2a2aacacf8827976c9`; the historical pilot is not a substitute. NuGet publication remains unapproved and must not be triggered.
