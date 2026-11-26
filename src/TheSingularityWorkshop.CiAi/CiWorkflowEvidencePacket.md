<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowEvidencePacket

**Contract:** `ciai.workflow-evidence.v1`  
**Schema:** [`schemas/ciai.workflow-evidence.v1.schema.json`](../../schemas/ciai.workflow-evidence.v1.schema.json)

## Purpose

`CiWorkflowEvidencePacket` is the single canonical envelope for one exact GitHub Actions workflow run attempt. It binds unversioned provider-observed run/job facts, a separately validated `ciai.workflow-provenance.v1` record, and the trusted host-registered required-check observations. It is not a release authorization and it does not turn an LLM proposal into permission to write to a repository.

## Identity boundaries

The packet preserves three distinct identities:

- **Workflow definition:** `workflowPath`, `workflowDefinitionRef`, and `workflowDefinitionSha` identify the trusted workflow that executed.
- **Event:** `eventName`, `eventRef`, `eventSha`, and optional PR head/base fields identify the triggering event.
- **Candidate checkout:** `candidateRef` and `candidateSha` identify the code actually tested. For ordinary push/PR runs the checkout must match the event commit. A separately checked-out feature candidate requires explicit, verified checkout evidence from the trusted dispatch path.

The PR source head is not silently substituted for the PR merge-event SHA. The workflow-definition SHA is never treated as the candidate SHA.

## Construction and validation

Use `FromCollections` when run and job facts were collected from GitHub, supplying the exact required-check bindings registered by the trusted host. Use `FromValidatedProvenance` only after the provenance bytes have been independently parsed and digest-checked. `FromJson` validates the exact repository, run ID, attempt, payload digest, host-registered required-check set, and host workflow-trust decision.

`EvidenceComplete` requires complete run/job discovery and exactly one terminal provider observation for each registered required check, including its job ID, status, conclusion, and start/completion timestamps. `IsSuccessful` additionally requires a successful run, success for every required check, a trusted workflow definition, and verified candidate checkout identity. Missing, duplicate, stale, mismatched, or incomplete evidence fails closed. Unregistered jobs remain raw observations but do not silently become required checks.

## Required-check binding

The host supplies stable `CiWorkflowRequiredCheckBinding` values before dispatch and supplies the same expected set again when reloading the packet. CiAi matches each exact provider job name: one match is `observed`, no match is `missing`, and multiple matches are `ambiguous`. Missing/ambiguous checks remain explicit and make the packet incomplete. `HasActionableDiagnostics` and `DiagnosticsFingerprint` summarize a complete trusted failed run at the required-job level; the separate `CiTestResultReport` retains normalized test-case diagnostics and its own stable verdict.

`PayloadSha256` is the SHA-256 of the exact compact UTF-8 v1 envelope.

## Durable reload

The serialized packet contains evidence, not authority. Reloading from the outbox deliberately sets `WorkflowDefinitionTrusted = false`; the host must re-evaluate trust from current policy and re-supply its registered required-check set before acting on it. A valid SHA-256 digest proves byte integrity, not that the sender or workflow is trusted.

## Related implementation

- [CiWorkflowProvenanceRecord](CiWorkflowProvenanceRecord.md)
- [CiTestResultReport](CiTestResultReport.md)
- [CiWorkflowEvidenceOutbox](CiWorkflowEvidenceOutbox.md)

---

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="240" /></p>
