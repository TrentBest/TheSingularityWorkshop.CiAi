<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowProvenanceRecord

**Contract:** `ciai.workflow-provenance.v1`

## Purpose

The provenance record captures GitHub's workflow-definition, event, pull-request, and checkout identities for one exact run attempt. It is a separate object from `ciai.workflow-evidence.v1` so the source of workflow authority cannot be confused with the code-under-test identity.

## Required identity

The record binds:

- Canonical `owner/repository`
- Positive `runId` and `runAttempt`
- Workflow file path, full workflow-definition ref, and full 40-character workflow SHA
- Event name, ref, and full event SHA
- Optional pull-request number, head ref/SHA, and base ref/SHA
- Actual checkout ref and full checkout SHA

## Trust rules

`ParseAndValidate` accepts the exact UTF-8 payload bytes, expected repository, expected run ID/attempt, and expected SHA-256 digest. It rejects unsupported schema versions, malformed or incomplete identity, duplicate JSON properties, invalid refs/SHAs, and mismatched run identity.

The workflow SHA is the revision that supplied the workflow definition. The event SHA is the event's commit identity. The checkout SHA is what the runner actually checked out. They are stored separately and compared under event-specific rules; none is inferred from another.

For pull requests, the event SHA can identify GitHub's merge ref. The PR head SHA and base SHA are retained as separate context. For a dispatch workflow that checks out an isolated candidate, the candidate checkout must be independently proven by the trusted workflow's resolution and checkout-verification steps.

## Digest semantics

The digest is calculated over the exact supplied bytes. The digest is not embedded in the record itself. Callers must preserve the original bytes and pass the expected digest from the trusted transport or provider metadata; recomputing a digest from parsed-and-reserialized JSON is not equivalent.

## Related implementation

- [CiWorkflowEvidencePacket](CiWorkflowEvidencePacket.md)
- [CiTestResultReport](CiTestResultReport.md)

---

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="240" /></p>
