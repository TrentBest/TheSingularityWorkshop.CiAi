<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiTestResultFoundryNoticeEnqueuer

## Purpose

`CiTestResultFoundryNoticeEnqueuer` connects CiAi's canonical `ciai.test-results.v1` report to Foundry's metadata-only `ArtifactReadyNotice` transport.

It does not send test-result JSON inside the notice. A host-supplied `ICiTestResultArtifactPublisher` stores the exact canonical report bytes and returns an `ArtifactManifest` that points to the stored object. CiAi validates that manifest before it persists a notice in the Foundry notice outbox.

## Byte and lineage binding

The enqueuer passes a copy of the exact `ToCanonicalUtf8Bytes()` output to the publisher. Before enqueueing, it requires the returned manifest to match:

- Report identity as the manifest idempotency key
- Exact SHA-256 digest and byte length
- `application/json` media type
- Repository, candidate SHA, and provider run ID
- Capability `ciai.test-results` version `1`
- Intended recipient identity
- Creation, notification, and expiry time ordering

A mismatch prevents the notice from entering the durable outbox. The notice then carries the manifest and retrieval URI, not the report body.

## Delivery

`FoundryArtifactReadyNoticeDeliveryCoordinator` reads pending notices, sends them through `FoundryArtifactReadyNoticeClient`, and records only matching durable Accepted/Duplicate receipts. Transient failures leave the same notice pending for a later bounded pass. Retries reuse the same notice ID and idempotency key.

## Host responsibilities and limits

This repository defines the publisher interface; it does not contain the production artifact-store implementation, retrieval authorization policy, Foundry ingress credential provider, or deployed receiver. The host must implement those boundaries and verify the remote artifact bytes against the notice manifest.

Foundry owns persisted dispatch/run validation, report consumption, task lifecycle, and any accepted report context passed to HeadlessAi. CiAi does not directly invoke a model or grant repository operations.

---

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="240" /></p>
