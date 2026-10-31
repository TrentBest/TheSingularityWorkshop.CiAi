<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowEvidencePacket

**User moniker:** Exact-Revision Evidence Envelope

## Purpose

`CiWorkflowEvidencePacket` v2 is the frozen compatibility envelope for one GitHub Actions run attempt. Its historical `workflowRef` / `workflowSha` fields are sourced from the run observation and must not be treated as independently validated workflow-definition provenance. V2 remains readable for compatibility, but a standalone v2 `IsSuccessful` result is not authoritative checkout or workflow-trust evidence. New trust-sensitive evaluation must use `CiWorkflowEvidencePacketV3`.

## Provenance roles and schema versions

V2 preserves its original serialized meaning. Do not reinterpret v2 run-head fields as workflow-definition identity, and do not silently upgrade v2 packets to v3.

V3 takes its authoritative provenance from a separately retrieved and validated `ciai.workflow-provenance.v1` artifact:

| V3 field | Meaning |
|---|---|
| `Provenance.WorkflowRef` / `WorkflowSha` | Exact workflow file ref and workflow-definition commit from `github.workflow_ref` / `github.workflow_sha` |
| `Provenance.EventName` / `EventRef` / `EventSha` | Triggering event identity from `github.event_name` / `github.ref` / `github.sha` |
| `Provenance.PullRequest` | PR number, source head ref/SHA, and base ref/SHA, when the event is a pull request |
| `Provenance.CheckoutRef` / `CheckoutSha` | Actual tested checkout, verified by the trusted workflow after checkout |

For a pull request, the event SHA and tested checkout normally identify the synthetic merge commit; the PR source head SHA is a separate fact. For a trusted `workflow_dispatch` run, the workflow definition can remain on `development` while the actual candidate checkout is a feature-branch ref/SHA.

## V2 candidate checkout verification (compatibility semantics)

The following rules describe the frozen v2 packet only. They do not establish trusted workflow-definition provenance. V3 uses the independent artifact rules in the next section.

`CandidateCheckoutVerified` in v2 is derived from the raw job/step observations; it is not a caller-supplied success flag. It is true for a normal run where the candidate is the workflow event commit. For a separately checked-out candidate, it is true only when:

- exactly one job matches the registered job name and belongs to the packet's exact run/attempt;
- exactly one branch-resolution step with the registered name completed successfully; and
- exactly one checkout-verification step with the registered name completed successfully.

A missing, skipped, failed, duplicate, or ambiguous step makes candidate checkout unverified. Checkout verification is independent of later build/test outcome: if those two steps succeeded and a later test fails, the packet still proves which candidate was checked out, while `IsSuccessful` remains false because the workflow/job failed. `IsSuccessful` requires `CandidateCheckoutVerified`; `BlockingReasons` then includes `candidate.checkout-unverified` only when checkout proof itself is missing or failed. A successful workflow run on `development` alone is not evidence that a feature-branch candidate passed.

The workflow-definition ref for separate candidate evidence must be the trusted `refs/heads/development` ref. The host must additionally validate the workflow SHA against its trusted workflow revision policy. `IsSuccessful` is a CI outcome, not a workflow-trust verdict: even a successful packet must be rejected as authoritative CODE RED evidence when its workflow-definition ref/SHA is not approved by the host. A candidate-controlled workflow must never be allowed to evaluate itself.

## V3 validated evidence envelope

`CiWorkflowEvidencePacketV3.FromCollections` is the provenance-required factory. It combines one exact run/attempt and its job collection with a `CiWorkflowProvenanceRecord` already parsed from the exact run-attempt artifact. The factory rejects repository, run, attempt, workflow-path, or event mismatches.

For ordinary event runs, V3 marks checkout verified only when the actual checkout ref/SHA equals the event ref/SHA. For a separate `workflow_dispatch` candidate, V3 additionally requires `CiWorkflowCandidateCheckoutEvidence` to match the provenance artifact's checkout ref/SHA and requires the named resolution and verification steps to have completed successfully in the exact observed job attempt.

V3 success additionally requires the host to set `workflowDefinitionTrusted` only after independently matching the full workflow-definition ref/SHA against protected control-plane policy. This is a host assertion, not a self-certifying packet field. The trust decision is deliberately omitted from `ToJson`; `FromJson` requires the trusted durable-store SHA-256, exact repository/run/attempt identity, and a fresh host workflow-policy decision. A packet digest must come from the trusted store, not be computed from the untrusted JSON being loaded.

The v3 envelope retains the v2 packet only as run/job outcome observations. Its v2 workflow/candidate aliases are not authoritative in V3. GitHub's artifact metadata digest covers the downloaded ZIP archive, not the extracted JSON file: the trusted host retriever must verify the ZIP digest, safely extract only the expected JSON entry, compute the extracted JSON content digest, and pass those exact bytes and digest to `CiWorkflowProvenanceRecord.ParseAndValidate`.

## Building from observed GitHub collections

`CiWorkflowEvidencePacket.FromCollections` remains the v2 compatibility adapter. New trusted evaluation should call `CiWorkflowEvidencePacketV3.FromCollections` and supply the independently validated provenance record. V3 carries collection truncation/completeness forward and refuses to infer workflow-definition or checkout identity from the legacy run-head fields.

## Remaining evidence facts

The packet also carries repository, workflow path, run ID/attempt, event, workflow name, run URL, observation timestamp, job/step facts, discovery completeness, job completeness, and artifact references.

## Trust boundary

- The packet preserves observed CI facts; it does not execute repairs or grant repository capabilities.
- Every job must belong to the packet's exact run ID and attempt.
- Every run/job URL must be an HTTPS GitHub URL for that exact run.
- Missing or truncated discovery/job evidence remains incomplete.
- A successful workflow summary is not enough: every observed job must be completed successfully before `IsSuccessful` is true.
- Step outcomes are retained verbatim; a host policy decides which steps are required, so a skipped or continue-on-error step is not silently reclassified as a workflow failure.
- Artifact references are references only. This contract does not claim that artifact bytes were downloaded, hashed, retained, or independently verified.
- Workflow-definition trust is external policy. The host must verify the workflow ref/SHA against protected control-plane policy; the packet records those facts but cannot certify its own workflow as trusted.
- A packet is not a release verdict, merge approval, publication authorization, or deployment authorization.

## JSON contract

- V2 compatibility schema: `ciai.workflow-evidence.v2` (contract version `2`); semantics are frozen.
- V3 trust-sensitive schema: `ciai.workflow-evidence.v3` (contract version `3`).
- V3 embeds the v2 run/job observation plus the distinct workflow/event/checkout provenance record.
- Serialization uses camelCase property names and string enum values.
- V2 `FromJson` rejects unsupported versions; callers must not silently reinterpret old run-head SHA values as workflow-definition or checkout provenance. No implicit in-place migration is performed.
- V3 `FromJson` requires the expected repository/run/attempt, a trusted durable-store packet SHA-256, and a fresh host workflow-definition trust decision. It does not restore trust from serialized JSON.
- V2 `FromJson` recomputes its compatibility candidate fields from nested checkout evidence and raw job/step observations, then rejects JSON whose flattened workflow/candidate refs, SHAs, or `candidateCheckoutVerified` value disagree with that recomputation.
- The legacy v2 source aliases `HeadRef` and `HeadSha` expose only the historical v2 workflow/candidate values; they must not be interpreted as `github.workflow_ref` / `github.workflow_sha`.

## Durable evidence outbox

`CiWorkflowEvidenceDelivery` and the current file outbox remain v2 compatibility paths. They have not yet been migrated to accept and persist V3 envelopes. Do not use a v2 outbox delivery's success bit as authoritative workflow-definition or checkout provenance. A future v3 outbox integration must bind the exact provenance and packet digest.

`FileCiWorkflowEvidenceOutbox` provides an append-only local journal:

- `Enqueue` persists a new delivery and flushes it to disk before returning.
- Re-enqueueing the same delivery ID and exact payload returns `Duplicate`.
- The workflow-attempt idempotency key also deduplicates an identical packet submitted under a different delivery ID; reusing that attempt key with different packet content returns `Conflict`.
- Reusing a delivery ID for different packet content returns `Conflict`; the original evidence is not overwritten.
- `ReadPending` rehydrates and revalidates packet schema, payload digest, and run-attempt identity after restart.
- The caller supplies acknowledged delivery IDs when reading pending entries. `FileCiWorkflowEvidenceReceiptLedger.ReadAcknowledgedDeliveryIds()` can supply this set after a remote receipt has been authenticated and reconciled by the host. Until a durable accepted/duplicate receipt exists, the packet remains replayable.
- A malformed or torn final record fails closed rather than silently discarding potentially valuable evidence.

The file lock serializes outbox writers across processes. `FileCiWorkflowEvidenceReceiptLedger` separately journals remote receipt records and binds each receipt to the exact delivery ID, idempotency key, and payload digest. Accepted and duplicate receipts acknowledge a delivery; rejected receipts remain visible and retryable. Exact receipt retries deduplicate, conflicting reuse of a receipt ID is rejected, and torn receipt journals fail closed. Receipt reads and writes share a process-wide file lock so readers do not observe an in-progress append.

This is still a local filesystem outbox and receipt journal, not a network transport, authenticated webhook receiver, distributed consensus store, or guarantee of remote delivery. **The host must authenticate and validate the remote response before calling `Record`; the ledger verifies correlation, not cryptographic authenticity.** Hosts must protect both journal paths and retain the remote transport's authentication and endpoint policy outside model-visible evidence.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
