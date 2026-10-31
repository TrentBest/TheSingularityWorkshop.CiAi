<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# FoundryArtifactReadyNoticeDeliveryCoordinator

**User moniker:** Durable Notice Courier

## Purpose

`FoundryArtifactReadyNoticeDeliveryCoordinator` connects CiAi's durable metadata-only notice outbox, authenticated HTTPS sender, and local receipt ledger for one bounded delivery pass.

It does not create notices, transfer artifact bytes, schedule retries, or authorize repository actions.

## One bounded pass

1. Read the receipt ledger's acknowledged notice IDs.
2. Read the exact pending serialized notices from the outbox.
3. Attempt each selected notice once, sequentially, up to the configured maximum (1–100; default 20).
4. Persist only authenticated `Accepted` or `Duplicate` receipts returned by `FoundryArtifactReadyNoticeClient`.
5. Re-read pending notices and return attempted, acknowledged, and remaining counts with per-notice outcomes.

The coordinator stops the batch on authentication failure, authorization denial, rate limiting, retryable transport failure, or a rejected/malformed receiver response. It preserves `RetryAfter` for the caller and leaves unacknowledged notices pending. Notice-specific `InvalidRequest` and `Conflict` outcomes remain visible in the result but do not prevent later independent notices from being attempted. Each attempt result also preserves the HTTP status, receiver receipt, error code, retry metadata, and local receipt-ledger outcome so the host can retain diagnostics without treating a negative response as acknowledgement.

## Recovery and concurrency

- If the receiver accepts a notice but local receipt persistence fails, the notice remains pending. A later pass resends the exact same notice ID, idempotency key, payload, and digest; the receiver should return the same stable receipt as a duplicate.
- A duplicate receipt is persisted as a local acknowledgement without overwriting the original receipt.
- The coordinator does not hold a local file lock across network I/O. Multiple coordinator instances may race on the same pending notice; receiver-side idempotency and exact receipt correlation are required to make that safe.
- Cancellation propagates. Any notice not durably acknowledged remains pending for the next invocation.
- No timers, exponential backoff, or automatic retries run inside this class. The host schedules later passes and respects returned retry metadata.

## Host responsibilities

The host supplies:
- an `HttpClient` configured with automatic redirects disabled;
- a host-owned credential provider that obtains a narrow, short-lived bearer token for the registered Foundry endpoint;
- protected outbox and receipt-ledger paths; and
- a scheduler or operator that decides when to invoke the next bounded pass.

The sender transmits only `ArtifactReadyNotice` metadata. Artifact retrieval, digest/size verification, provider acknowledgement delivery, and Foundry's task orchestration are separate responsibilities.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
