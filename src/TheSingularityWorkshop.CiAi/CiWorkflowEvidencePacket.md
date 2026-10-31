<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowEvidencePacket

**User moniker:** Exact-Revision Evidence Envelope

## Purpose

`CiWorkflowEvidencePacket` v2 is a versioned, immutable JSON envelope for one exact GitHub Actions workflow run attempt. It separates the commit that executed the trusted workflow definition from the candidate commit that the workflow actually checked out and tested.

## Two SHA roles

| Field | Meaning |
|---|---|
| `workflowRef` / `workflowSha` | GitHub run-level event ref and head SHA: the workflow-definition execution provenance |
| `candidateRef` / `candidateSha` | The exact repository ref and commit whose code was checked out and tested |

For ordinary CI runs, candidate provenance defaults to the workflow event ref/SHA. For trusted-ref dispatch, the workflow runs from `refs/heads/development` and checks out a separate `refs/heads/feature/...` candidate. In that case the packet must include `CiWorkflowCandidateCheckoutEvidence` with the candidate ref/SHA, the exact job name, the branch-resolution step name, and the checkout-verification step name.

## Candidate checkout verification

`CandidateCheckoutVerified` is derived from the raw job/step observations; it is not a caller-supplied success flag. It is true for a normal run where the candidate is the workflow event commit. For a separately checked-out candidate, it is true only when:

- exactly one job matches the registered job name;
- exactly one job matches the registered job name and belongs to the packet's exact run/attempt;
- exactly one branch-resolution step with the registered name completed successfully; and
- exactly one checkout-verification step with the registered name completed successfully.

A missing, skipped, failed, duplicate, or ambiguous step makes candidate checkout unverified. Checkout verification is independent of later build/test outcome: if those two steps succeeded and a later test fails, the packet still proves which candidate was checked out, while `IsSuccessful` remains false because the workflow/job failed. `IsSuccessful` requires `CandidateCheckoutVerified`; `BlockingReasons` then includes `candidate.checkout-unverified` only when checkout proof itself is missing or failed. A successful workflow run on `development` alone is not evidence that a feature-branch candidate passed.

The workflow-definition ref for separate candidate evidence must be the trusted `refs/heads/development` ref. The host must additionally validate the workflow SHA against its trusted workflow revision policy.

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
- A packet is not a release verdict, merge approval, publication authorization, or deployment authorization.

## JSON contract

- Schema: `ciai.workflow-evidence.v2`
- Contract version: `2`
- Serialization uses camelCase property names and string enum values.
- `FromJson` rejects v1 and unknown versions; callers must not silently reinterpret v1 run-head SHA as candidate SHA.
- The legacy source aliases `HeadRef` and `HeadSha` refer only to workflow execution provenance and are not serialized in v2.

## Durable evidence outbox

`CiWorkflowEvidenceDelivery` wraps one packet with a caller-supplied delivery ID, a stable run-attempt idempotency key (`repository:runId:runAttempt:workflowSha:candidateSha`), and a SHA-256 digest of the serialized packet.

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
