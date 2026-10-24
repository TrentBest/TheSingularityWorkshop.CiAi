# CiRunEvidenceVerifier

**User moniker:** The Singularity Workshop

## Purpose

Reconcile a validated, provider-fetched `CiRunSnapshot` with coordinator-owned expectations before treating it as verified success.

## UML class card

```text
+------------------------------------------------------+
| CiRunEvidenceVerifier                                |
+------------------------------------------------------+
| + VerifySuccessfulRun(snapshot, repository, runId,   |
|   headSha) : IReadOnlyList<string>                   |
+------------------------------------------------------+
| Checks expected repository/run/SHA identity and      |
| requires completed + successful provider evidence.   |
+------------------------------------------------------+
```

## Trust boundary

The verifier does not fetch GitHub data, authorize operations, or infer that a callback is truthful. The host must first obtain the snapshot through the read-only provider client, then supply expected identity values from trusted coordination state. Any mismatch or non-success conclusion yields a stable error code; an empty error list means this narrow reconciliation check passed.

See [CiRunObservationUML](../../docs/UML/CiRunObservationUML.md) for the read-only observation use case.
