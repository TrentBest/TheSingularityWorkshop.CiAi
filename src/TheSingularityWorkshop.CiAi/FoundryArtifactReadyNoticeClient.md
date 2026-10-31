<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# FoundryArtifactReadyNoticeClient

**User moniker:** Durable Notice, Not Artifact

## Purpose

**FoundryArtifactReadyNoticeClient** sends the metadata-only **ArtifactReadyNotice v1** contract to Foundry's host-owned HTTPS ingress:

POST /api/v1/integrations/ciai/artifact-ready

It implements the sender side of [Foundry's normative callback contract](https://github.com/TrentBest/TheSingularityWorkshop.Foundry/blob/work/code-red-task-lifecycle/docs/CIAI_ARTIFACT_CALLBACK_CONTRACT.md). It does not send evidence bytes, retrieve artifacts, authorize repository operations, or decide whether CI passed.

## Authentication boundary

- The host supplies an **IFoundryIngressCredentialProvider** backed by its approved secret or workload-identity mechanism.
- A short-lived bearer token is requested for each delivery attempt and placed only in the HTTP Authorization header.
- CiAi does not mint JWTs, persist credentials, log token values, or place them in notices or model-visible evidence.
- The injected **HttpClient** must disable automatic redirects. The client rejects redirect responses, but cannot prevent an injected handler from following a redirect before returning the response.

## Receipt and retry behavior

Only a valid HTTP 202 **accepted** receipt or HTTP 200 **duplicate** receipt acknowledges the notice. The response must identify the exact contract, notice ID, idempotency key, stable receipt ID, and acceptance time. Mismatched, malformed, oversized, or unsupported responses are not acknowledgements.

| Response | Client outcome |
|---|---|
| 202 / matching accepted | Accepted |
| 200 / matching duplicate | Duplicate |
| 401 | AuthenticationRequired; refresh credentials securely |
| 403 | AuthorizationDenied; stop and escalate |
| 409 | Conflict; stop and preserve the original identity |
| 400 / 413 / 415 / 422 | InvalidRequest; do not retry unchanged input |
| 429 | RateLimited; preserve Retry-After |
| 5xx / transport interruption / incomplete receipt | RetryableFailure; retry the exact same notice |

The client performs one HTTP attempt. **FileFoundryArtifactReadyNoticeOutbox** persists the exact serialized notice and digest before sending, then replays those same bytes after restart. **FileFoundryArtifactIngressReceiptLedger** binds a validated remote receipt to the exact notice ID, idempotency key, and local payload digest. Only accepted/duplicate receipts suppress replay; rejected, missing, or mismatched receipts leave the notice pending. The caller still owns retry scheduling, expiry/budget enforcement, credential refresh, and calling the receipt ledger only after the authenticated sender validates the response. It must reuse the same notice ID, manifest, and idempotency key on retry; it must not create a new identity to escape a failed delivery.

## Current boundary

This is a durable sender-side notice pipeline, not yet a complete remote artifact handoff. CiAi still needs an immutable artifact publisher/hosting adapter, a concrete host-managed credential integration, and the provider-side authenticated receipt acknowledgement sender. The credential provider remains an injected host boundary; CiAi does not mint or store JWTs. Foundry separately owns ingress hosting, JWT validation, durable receiver receipts, safe artifact retrieval, evidence reconciliation, and task orchestration.

A 202 receipt means only that Foundry durably recorded the notice. It does not prove artifact retrieval, digest verification, packet acceptance, successful CI, review readiness, or authorization to change a repository.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
