<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowEvidencePacket

**Contract:** `ciai.workflow-evidence.v1`

## Purpose

`CiWorkflowEvidencePacket` is the canonical envelope for one exact GitHub Actions workflow run attempt. It binds provider-observed run/job facts to a separately validated `ciai.workflow-provenance.v1` record. It is not a release authorization and it does not turn an LLM proposal into permission to write to a repository.

## Identity boundaries

The packet preserves three distinct identities:

- **Workflow definition:** `workflowPath`, `workflowDefinitionRef`, and `workflowDefinitionSha` identify the trusted workflow that executed.
- **Event:** `eventName`, `eventRef`, `eventSha`, and optional PR head/base fields identify the triggering event.
- **Candidate checkout:** `candidateRef` and `candidateSha` identify the code actually tested. For ordinary push/PR runs the checkout must match the event commit. A separately checked-out feature candidate requires explicit, verified checkout evidence from the trusted dispatch path.

The PR source head is not silently substituted for the PR merge-event SHA. The workflow-definition SHA is never treated as the candidate SHA.

## Construction and validation

Use `FromCollections` when run and job facts were collected from GitHub. Use `FromValidatedProvenance` only after the provenance bytes have been independently parsed and digest-checked. `FromJson` validates the exact repository, run ID, attempt, payload digest, and host workflow-trust decision.

A packet is successful only when discovery and job collection are complete, the run is terminal and successful, every observed job succeeded, the workflow definition is trusted, and candidate checkout identity is verified. Missing, duplicate, stale, mismatched, or incomplete evidence fails closed.

## Durable reload

The serialized packet contains evidence, not authority. Reloading from the outbox deliberately sets `WorkflowDefinitionTrusted = false`; the host must re-evaluate trust from current policy before acting on it. A valid SHA-256 digest proves byte integrity, not that the sender or workflow is trusted.

## Related implementation

- [CiWorkflowProvenanceRecord](CiWorkflowProvenanceRecord.md)
- [CiTestResultReport](CiTestResultReport.md)
- [CiWorkflowEvidenceOutbox](CiWorkflowEvidenceOutbox.md)

---

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="240" /></p>
