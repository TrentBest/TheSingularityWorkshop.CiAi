# CiAi Release Readiness

**Implementation readiness: 30%. Package preparation: 70%.** This is an alpha package candidate, not a publication approval.

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
- Full 40-character hexadecimal commit SHA validation.
- Exact HTTPS GitHub Actions evidence path validation; rejects query strings, fragments, non-default ports, and user-info.
- Unknown future provider conclusions remain explicitly unknown and cannot be successful.
- Offline contract tests and a console observation sample.
- MIT license, package metadata, embedded README, symbol-package settings, pinned GitHub Actions workflow.
- CI builds, runs offline contract checks, packs the NuGet candidate, and validates the packed artifact.

## Must pass before publication

- [x] Build, offline contract checks, pack, and packed-artifact validation passed on commit `f207fc028a79a19305c7296a9e930c06b64ad40d` in [workflow run 40040017424](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40040017424). Re-run on the exact final candidate SHA after any further changes.
- [ ] Inspect the produced `.nupkg` and `.snupkg` contents, package metadata, and embedded README beyond automated CI assertions.
- [ ] Review README and public API claims against actual implementation.
- [ ] Confirm package ID and version are intended and not already published.
- [ ] Review NuGet account/credential setup and release permissions outside source control.
- [ ] Obtain explicit owner approval to publish.

## Deliberately not claimed

This alpha is a read-only CI observation library. HeadlessAi invocation, ProtocolAi vocabulary integration, host authorization, cross-repository dispatch, persistent idempotency/history, and a complete request/result round trip are not implemented in CiAi. No publish or deploy action is exposed by this library.

## Release gate

A green CI run proves that the candidate builds, passes its offline tests, and can be packed. It does not authorize publishing and does not prove an end-to-end LLM workflow. Do not publish until the checklist is reviewed and the owner explicitly approves.
