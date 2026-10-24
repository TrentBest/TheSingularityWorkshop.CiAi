# CiAi Release Readiness

**Implementation readiness: 35%. Package preparation: 70%.** This is an alpha package candidate, not a publication approval.

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
- Fourteen additional offline handoff-contract checks, including exact receipt-to-manifest reconciliation and mismatch rejection. The console contract suite now has 28 main contract checks plus 13 read-only GitHub client checks; CI is the execution authority for this count on the candidate SHA.
- Source companion docs and UML/use-case diagram for the three handoff contracts.
- MIT license, package metadata, embedded README, symbol-package settings, pinned GitHub Actions workflow.
- CI builds, runs offline contract checks, packs the NuGet candidate, and validates the packed artifact.

## Must pass before publication

- [x] Earlier build, offline contract checks, pack, and packed-artifact validation passed on commit `f207fc028a79a19305c7296a9e930c06b64ad40d` in [workflow run 40040017424](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40040017424).
- [ ] Re-run and verify build, offline checks, pack, and packed-artifact validation on the latest commit containing artifact handoff contracts before treating this as a current release candidate.
- [ ] Inspect the produced `.nupkg` and `.snupkg` contents, package metadata, and embedded README beyond automated CI assertions.
- [ ] Review README and public API claims against actual implementation.
- [ ] Confirm package ID and version are intended and not already published.
- [ ] Review NuGet account/credential setup and release permissions outside source control.
- [ ] Obtain explicit owner approval to publish.

## Deliberately not claimed

This alpha remains a read-only CI observation library. Typed handoff contracts now exist, but the contracts do not yet retrieve or hash payload bytes, authenticate providers/receipts, deduplicate acknowledgements durably, correlate authoritative provider CI facts end-to-end, or run retention/reclamation. The complete catalog-to-request-to-result vertical proof is still missing. HeadlessAi invocation, ProtocolAi vocabulary integration, host authorization, cross-repository dispatch, persistent idempotency/history, and a complete request/result round trip are not implemented in CiAi. No publish or deploy action is exposed by this library.

## Release gate

A green CI run proves that the candidate builds, passes its offline tests, and can be packed. It does not authorize publishing and does not prove an end-to-end LLM workflow. Do not publish until the checklist is reviewed and the owner explicitly approves.
