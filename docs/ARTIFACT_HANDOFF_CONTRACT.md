# Capability Artifact Handoff Contract

**Status:** typed contracts, bounded caller-supplied stream reading, local payload-integrity verification, destination preflight policy, exact provider provenance reconciliation, durable local artifact receipts, outbound Foundry ready-notice transport, and durable notice/ingress-receipt journals are implemented; immutable remote artifact publication, provider-authenticated URI retrieval, durable staging, provider acknowledgement transport, and end-to-end handoff remain unimplemented.  
**Design owner:** CiAi defines the cross-provider lifecycle; each capability owns its output semantics.

## Purpose

A capability provider completes work in its own environment, publishes a versioned artifact, and notifies the requesting client's CiAi. The client CiAi retrieves the artifact using read-only access, verifies it inside the client's security boundary, and acknowledges receipt. The provider may later reclaim eligible temporary payloads according to explicit retention policy.

The provider never needs write access to the client's repository. A notification does not transfer the artifact or authorize a client-side change.

## Parties and authority

| Party | Responsibility | Authority |
|---|---|---|
| Requesting client CiAi | Creates/correlates request, authenticates retrieval, verifies content, stores locally, runs client-side checks, acknowledges receipt | Owns local acceptance and any application to client source |
| Capability provider | Executes capability, validates provider-owned output, publishes immutable artifact, sends ready notice, records acknowledgements | Owns provider artifact lifecycle under its retention policy |
| Provider CI / execution host | Supplies authoritative run, commit, attempt, and artifact evidence | Source of truth for execution facts |
| Client CI / maintainer | Validates artifact in its intended context and reviews proposed changes | Source of truth for consumer acceptance |
| CiAi coordinator and Cogs | Enforce contract, identity, policy, correlation, state transitions, and reporting | Must not manufacture evidence or bypass either side's policy |

## Lifecycle states

These are separate facts, not a single boolean success flag.

| State | Meaning |
|---|---|
| Requested | A bounded operation request exists and has a stable ID |
| Prepared | Provider completed its work and has a candidate output |
| Published | Immutable artifact and manifest are available from provider-controlled storage |
| Notified | Client CiAi was sent a ready notice; this does not prove delivery or retrieval |
| Retrieved | Client CiAi downloaded the payload |
| IntegrityVerified | Client verified the content digest and expected artifact identity |
| LocallyValidated | Client-side checks accepted the artifact's structure and applicable policy |
| Acknowledged | Client sent an authenticated receipt acknowledgement |
| Accepted | Consumer CI or maintainer accepted the output for use |
| Reclaimed | Provider removed an eligible temporary payload under retention policy |

Failure, partial, expired, unavailable, cancelled, rejected, and unknown outcomes must remain explicit. An acknowledgement may report successful retrieval and integrity verification even when local validation later rejects the artifact; do not conflate those events.

## CI evidence artifact provenance

For GitHub Actions evidence payloads using `ciai.workflow-evidence.v2`, keep the two SHA roles distinct:

- `workflowRef` / `workflowSha` identify the trusted workflow execution revision recorded by GitHub.
- `candidateRef` / `candidateSha` identify the isolated feature-branch code that the trusted workflow resolved and checked out.
- `CandidateCheckoutVerified` is derived from the exact registered job's branch-resolution and checkout-verification steps; it must be true before the packet can be successful.
- `ArtifactManifest.SourceSha` binds to the packet's `candidateSha`; `ProviderRunId` binds to the packet's `runId`; `ProviderRepository` binds to the packet's `repository`.

The consumer must validate workflow ref/SHA against its trusted workflow registration and candidate ref/SHA against the exact task/request. A successful run on the trusted workflow ref is not proof that a candidate commit passed unless candidate checkout provenance is present and verified. Packet v1 is not sufficient for trusted-ref dispatch and must not be silently reinterpreted as candidate evidence.

## Ready notice

The notice should be small and contain metadata only. Its fields are a conceptual contract, not a prescribed wire format:

- Contract version and notice type.
- Stable request ID, operation ID, and idempotency key.
- Provider identity, capability identity, capability version, and immutable implementation revision.
- Artifact ID and immutable artifact version.
- Read-only retrieval reference with expected media type and bounded size.
- Content digest algorithm and digest value.
- Input/source revision and dependency/tool provenance references.
- Provider CI workflow/run ID and attempt, when CI executed the work.
- Created-at and expires-at timestamps.
- Retention class and a provider-defined acknowledgement endpoint or channel.

Never put credentials, tokens, secrets, or arbitrary executable instructions in the notice. The client must validate the retrieval destination against configured provider identity/policy rather than blindly following an arbitrary URL from an untrusted message.

## Client retrieval and verification

1. Authenticate the provider and authorize the specific artifact read.
2. Validate notice schema/version, request correlation, provider identity, expiry, and expected capability.
3. Enforce payload size, media type, path, and decompression limits before processing.
4. Retrieve with read-only credentials and store in a client-controlled staging area.
5. Recompute the digest and compare it to the published manifest.
6. Verify provenance against authoritative provider run/artifact data when available.
7. Scan/parse as untrusted data. Do not execute code, shell commands, macros, links, or instructions embedded in generated output.
8. Run consumer-specific checks before accepting or applying any change.
9. Record an auditable local receipt with artifact ID, digest, request ID, timestamps, and validation outcome.

A matching digest proves that bytes match the declared digest; it does not by itself prove the producer is trusted or the content is correct. Identity, provenance, policy, and local validation still matter.

## Courtesy acknowledgement

The acknowledgement is an idempotent receipt, not a deletion command. It should include:

- Contract version and acknowledgement type.
- Request ID, artifact ID/version, and content digest.
- Client CiAi identity and a unique acknowledgement ID.
- Retrieved and integrity-verified outcomes, plus timestamps.
- Optional local-validation outcome, kept distinct from receipt status.
- A stable deduplication key so retrying after a timeout cannot create conflicting state.

Provider behavior: authenticate the acknowledgement, correlate it to the exact published artifact, record it idempotently, and return or expose the recorded receipt. Duplicate acknowledgements for the same artifact and digest should not create duplicate lifecycle events. An acknowledgement for an unknown artifact, wrong digest, expired request, or mismatched consumer must be rejected or quarantined for review.

## Retention and cleanup

- Each artifact has an explicit retention class and expiry.
- Keep durable metadata/audit provenance after a payload is reclaimed, subject to the provider's retention policy.
- A successful receipt acknowledgement may make a temporary payload eligible for cleanup; it does not force immediate deletion.
- Do not delete if another authorized consumer still needs the artifact, the artifact is designated durable, an audit/legal/operational hold applies, or verification/acknowledgement is ambiguous.
- If no acknowledgement arrives, retain until expiry and use bounded retries or a status query; do not assume that a lost response means the client failed to retrieve.
- Cleanup must be idempotent and must never delete a newer artifact version because an older receipt arrived late.
- Report Reclaimed only after deletion is confirmed by the storage provider.

## Security and reliability requirements

- Read-only client retrieval; no provider credentials for client repository writes.
- Authenticated notice and acknowledgement channels, with replay protection.
- Immutable artifact identity and cryptographic content digest.
- Pinned provider code and dependency revisions.
- Bounded payloads, strict schema validation, safe path handling, and no implicit execution.
- Stable request/artifact identity and idempotency for notifications, retrieval receipts, and cleanup.
- Explicit retry, expiry, cancellation, and unavailable states.
- Provider execution evidence and consumer acceptance evidence remain distinct.
- Logs contain correlation IDs and lifecycle facts but never secrets or full sensitive payloads by default.

## First implementation slice

Keep the first proof deliberately small and non-destructive:

1. Define typed C# contracts for a ready notice, artifact manifest, and receipt acknowledgement.
2. Add offline tests for required fields, malformed identities/digests, expiry, mismatched request/artifact IDs, duplicate acknowledgements, and state transitions.
3. [x] Verify supplied payload bytes against manifest size and SHA-256, with a configurable maximum size and stable rejection codes.
4. [x] Test valid fixture bytes, digest mismatch, size mismatch, caller-specified size limit, and invalid limit.
5. [x] Demonstrate that no cleanup happens merely because a notice was sent; a valid acknowledgement only makes a payload eligible for separate policy review.
6. [x] Reconcile receipt evidence against the exact provider repository, run ID, and source revision when present in the manifest.
7. [x] Persist receipts in a local append-only ledger, deduplicate exact retries across reopen, and reject conflicting reuse of a deduplication key.
8. [x] Keep the callback sender outbound and host-authenticated; do not add receiver hosting, repository dispatch, broad credentials, or publishing/deployment permissions to CiAi.

**Current implementation status:** `ArtifactManifest`, `ArtifactReadyNotice`, and `ArtifactReceiptAcknowledgement` are implemented as dependency-free typed contracts. `ArtifactPayloadVerifier` verifies supplied bytes against manifest size and SHA-256. `ArtifactPayloadReader` reads a caller-supplied readable stream incrementally with a configured memory bound and returns bytes only after successful verification. `FoundryArtifactReadyNoticeClient` now implements the outbound HTTPS POST to Foundry's normative `foundry.ciai-artifact-ingress.v1` route. It obtains a short-lived bearer token from a host-supplied credential provider, sends only metadata, validates the exact durable receipt identity, bounds response parsing, and classifies policy versus retryable outcomes. The caller must disable automatic redirects and owns retry scheduling. `FileFoundryArtifactReadyNoticeOutbox` persists exact notice bytes and replays them after restart; `FileFoundryArtifactIngressReceiptLedger` suppresses replay only after a matching accepted/duplicate receipt is recorded. The sender performs one attempt and obtains credentials through a host-supplied provider. See [the sender boundary](../src/TheSingularityWorkshop.CiAi/FoundryArtifactReadyNoticeClient.md) and [Foundry's host-owned callback contract](https://github.com/TrentBest/TheSingularityWorkshop.Foundry/blob/work/code-red-task-lifecycle/docs/CIAI_ARTIFACT_CALLBACK_CONTRACT.md). A destination preflight policy is not a standalone SSRF defense. Immutable remote artifact publication/hosting, concrete secure credential provisioning, provider-authenticated retrieval, durable staging, authoritative provider-run verification, the provider-side acknowledgement sender, end-to-end orchestration, and storage reclamation remain unimplemented. The sender's accepted receipt proves only durable notice recording, not artifact retrieval or CI success. CiAi remains read-only with respect to GitHub repository state; no repository dispatch, merge, release, or publication authority is introduced.
