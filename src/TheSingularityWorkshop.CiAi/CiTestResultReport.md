<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiTestResultReport

**Contract:** `ciai.test-results.v1`

## Purpose

`CiTestResultReport` is CiAi's canonical, provenance-bound summary of one test job's TRX result. It normalizes test records and diagnostics while preserving exact workflow/run/job/candidate lineage. Foundry consumes this versioned JSON report; HeadlessAi receives accepted opaque bytes from Foundry and does not reinterpret TRX.

## Input validation

The producer requires a trusted workflow definition, verified candidate checkout, complete exact-run evidence, and exactly one matching job. The artifact source is bound to repository, run ID, attempt, numeric provider job ID, configured job key, and provider artifact metadata.

Provider artifact names are labels supplied by the provider. They are not required to follow a CiAi-generated naming convention. The trusted host must map the provider artifact ID/name to the numeric job ID and configured job key before constructing the source descriptor. A sidecar value is never accepted as a substitute for provider-authenticated job identity.

The archive reader is bounded and fail-closed. It verifies the provider archive digest and size, accepts only the expected TRX/provenance entries, and rejects unsafe paths, duplicate or missing entries, symlinks/reparse points, oversized payloads, and malformed archives. TRX parsing disables DTDs and external entities.

## Verdicts

- `CompletePass`: the required job completed successfully and all counters/results reconcile.
- `ActionableFailure`: a trusted, complete report contains actionable failed-test diagnostics.
- `IncompleteOrUntrusted`: required evidence, provenance, artifact, or result reconciliation is incomplete or untrusted.
- `NonActionableFailure`: the run failed, but the report does not contain an actionable test failure.

Unknown outcomes, missing diagnostics where required, inconsistent counters, and invalid provenance do not become repair instructions. Diagnostic messages and stack traces are bounded and credential-like values are redacted.

## Wire bytes and identity

The public schema is `ciai.test-results.v1`. `ToCanonicalUtf8Bytes()` returns compact UTF-8 JSON without a BOM or trailing newline. These exact bytes are the bytes to hash, persist, publish, and pass downstream.

- Raw TRX payload limit: 16 MiB.
- Canonical report handoff limit: 1 MiB.
- `ReportSha256`: lowercase SHA-256 of the exact canonical bytes.
- `ReportIdentity`: `sha256:` followed by that lowercase digest; it is metadata and is not embedded in the report JSON.

The 1 MiB handoff limit matches the Foundry and HeadlessAi consumer contracts.

## Related implementation

- [CiWorkflowEvidencePacket](CiWorkflowEvidencePacket.md)
- [CiWorkflowEvidenceOutbox](CiWorkflowEvidenceOutbox.md)
- [CiTestResultFoundryNoticeEnqueuer](CiTestResultFoundryNoticeEnqueuer.md)

---

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="240" /></p>
