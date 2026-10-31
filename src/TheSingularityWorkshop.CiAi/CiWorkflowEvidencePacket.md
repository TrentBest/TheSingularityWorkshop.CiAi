# CiWorkflowEvidencePacket

**User moniker:** Exact-Revision Evidence Envelope

## Purpose

`CiWorkflowEvidencePacket` is a versioned, immutable JSON envelope for one exact GitHub Actions workflow run attempt. It carries repository, workflow, branch/ref, full source SHA, event, run/attempt identity, provider run URL, observation timestamp, job/step facts, discovery completeness, job completeness, and artifact references.

## Trust boundary

- The packet preserves observed CI facts; it does not execute repairs or grant repository capabilities.
- Every job must belong to the packet's exact run ID and attempt.
- Every run/job URL must be an HTTPS GitHub URL for that exact run.
- Missing or truncated discovery/job evidence remains incomplete.
- A successful workflow summary is not enough: every observed job must be completed successfully before `IsSuccessful` is true. Step outcomes are retained verbatim; a host policy decides which steps are required, so a skipped or continue-on-error step is not silently reclassified as a workflow failure.
- `BlockingReasons` uses stable machine-readable codes; it does not convert diagnostics into instructions.
- Artifact references are references only. This contract does not claim that artifact bytes were downloaded, hashed, retained, or independently verified.
- A packet is not a release verdict, merge approval, publication authorization, or deployment authorization.

## JSON contract

- Schema: `ciai.workflow-evidence.v1`
- Contract version: `1`
- Serialization uses camelCase property names and string enum values.
- Unknown schema versions are rejected by `FromJson`; callers must not silently reinterpret a future packet.

## Current boundary

The existing GitHub Actions run and job collectors remain read-only and retain each run attempt independently. This packet is the normalized handoff layer above those observations. Durable delivery, dispatch/rerun requests, authenticated callback processing, artifact inventory/digest verification, and Foundry lifecycle integration remain separate work items; this type does not claim those systems exist.


## Durable evidence outbox

`CiWorkflowEvidenceDelivery` wraps one packet with a caller-supplied delivery ID, a stable run-attempt idempotency key (`repository:runId:runAttempt:headSha`), and a SHA-256 digest of the serialized packet.

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

![The Singularity Workshop](../../Avatar.jpg)
