# CiAi Release Readiness

**Implementation readiness: 38%. Package preparation: 70%.** This is an alpha package candidate, not a publication approval.

## Candidate

- Package: `TheSingularityWorkshop.CiAi`
- Version: `0.1.0-alpha.1`
- Target: `.NET 8`
- License: MIT
- External dependencies in the core library: none
- Release branch: `development`; the release candidate must be reviewed before any merge or publication.

## Implemented

- Typed, provider-neutral `CiRunSnapshot` and finite status/conclusion vocabulary.
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
- `FileArtifactReceiptLedger` persists receipt records, suppresses exact retries across ledger reopen, rejects conflicting reuse of a deduplication key, and refuses receipts whose manifest provenance does not match. Its file write is flushed to disk and cross-process writers are serialized by an exclusive lock file; it does not perform provider cleanup.
- The current contract suite has 46 offline CiAi contract checks plus 13 GitHub Actions client integration checks. Confirm those counts on the exact candidate workflow run; a green run on an earlier commit is not evidence for a newer tree.
- Source companion docs and UML/use-case diagram for the three handoff contracts.
- MIT license, package metadata, embedded README, symbol-package settings, pinned GitHub Actions workflow.
- CI builds, runs offline contract checks, packs the NuGet candidate, and validates the packed artifact.

## Must pass before publication

- [x] Baseline build, offline contract checks, pack, and packed-artifact validation passed on commit `f207fc028a79a19305c7296a9e930c06b64ad40d` in [workflow run 40040017424](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40040017424).
- [x] The exact source candidate passed build, expanded offline checks, pack, packed-artifact validation, and package-artifact upload in the [CiAi Contract Verification workflow](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/ci.yml). Re-run and confirm the exact SHA after any source or workflow changes.
- [x] Independently inspected the generated `.nupkg` and `.snupkg`, package contents, nuspec metadata, embedded README links, and SHA-256 hashes. The workflow uploads the pair as the `ciai-package-candidate` artifact for repeatable inspection.
- [ ] Review README and public API claims against actual implementation.
- [ ] Confirm package ID and version are intended and not already published.
- [ ] Review NuGet account/credential setup and release permissions outside source control.
- [ ] Obtain explicit owner approval to publish.

## Deliberately not claimed

This alpha remains read-only with respect to CI-provider operations. Typed handoff contracts, bounded caller-supplied stream reads, local byte-integrity verification, provider provenance reconciliation, and a durable local receipt deduplication ledger now exist. The client still does not fetch the manifest URI or authenticate the provider, and there is no end-to-end authoritative provider CI correlation. The complete catalog-to-request-to-result vertical proof is still missing. HeadlessAi invocation, ProtocolAi vocabulary integration, host authorization, cross-repository dispatch, persistent idempotency/history, and a complete request/result round trip are not implemented in CiAi. No publish or deploy action is exposed by this library.

## Release gate

A green CI run proves that the exact candidate builds, passes its offline tests, and can be packed; independent artifact inspection checks the package produced by that run. Neither authorizes publishing nor proves an end-to-end LLM workflow. Re-run CI and inspect the newly produced artifact after any source or workflow change. The remaining gates are public-package/version confirmation, external NuGet credential and permission review, API/README owner review, and explicit owner approval.
