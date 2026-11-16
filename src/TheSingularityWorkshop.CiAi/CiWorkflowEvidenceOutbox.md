<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowEvidenceOutbox

## Purpose

CiAi has one canonical workflow-evidence outbox and one matching receipt ledger for the `ciai.workflow-evidence.v1` delivery family. The outbox persists exact packet bytes before transport; the ledger records receiver acknowledgements against the same delivery identity.

The Foundry artifact-ready notice outbox is a separate transport queue for the outer metadata-only notice. It does not create a second workflow-evidence packet schema or a parallel v2/v3 evidence ledger.

## Delivery identity

A delivery stores:

- Stable delivery ID
- Canonical v1 packet
- Exact compact packet payload
- SHA-256 payload digest
- Structured SHA-256 idempotency key

The idempotency key is derived from repository, run ID, attempt, workflow ref/SHA, and checkout ref/SHA. It is not derived by delimiter-joining untrusted text. Repeating the same attempt and bytes is a duplicate; changing the payload for the same identity is a conflict.

## Persistence and reload

The file outbox is append-only JSONL with exclusive file locking and disk flush. It rejects incomplete trailing records, malformed records, invalid digests, duplicate delivery IDs, and duplicate idempotency identities.

On reload, the exact payload is reparsed and its digest and identity are revalidated. Host workflow trust is deliberately unset. A matching payload digest is not a substitute for current host policy.

## Receipt semantics

The receipt ledger binds the receiver's receipt ID, delivery ID, idempotency key, payload digest, outcome, and receive time. Only an authenticated `Accepted` or `Duplicate` receipt that matches the exact delivery acknowledges it and suppresses replay. A `Rejected` receipt is recorded for diagnosis but does not acknowledge the delivery. Conflicting or mismatched receipts fail closed.

## Operational boundary

The queue is a durable local-filesystem journal intended for a single host process. It does not itself authenticate the remote receiver, schedule timers, or grant repository-write authority. Transport retries must reuse the same delivery and idempotency identity.

---

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="240" /></p>
