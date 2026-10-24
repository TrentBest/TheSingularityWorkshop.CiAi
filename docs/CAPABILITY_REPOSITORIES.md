# Capability Repositories and the CiAi Domain

**Status:** proposed architecture; design only, not an implemented execution feature.  
**Owner:** CiAi coordinates the contract. Capability repositories own their algorithms and domain-specific tests.

## The idea

A future UML capability could provide a reusable operation: given a declared source snapshot and a bounded request, derive source-grounded UML artifacts and return them with provenance and validation evidence.

A consumer repository would not need to implement UML generation itself. The provider completes the work and stores a versioned artifact in its own repository or artifact store. It then notifies the client's CiAi that the result is ready. The client's CiAi authenticates and pulls the artifact into the client's own security boundary, verifies its identity and integrity, and applies local validation. After successful retrieval and verification, it sends a courtesy acknowledgement so the provider can account for delivery and eventually reclaim eligible temporary storage.

This is useful when the capability boundary is stable, outputs are independently verifiable, and separate versioning is more valuable than duplicating the implementation in every repository.

## Proposed responsibility boundaries

| Layer | Owns | Must not silently own |
|---|---|---|
| Consumer repository | Source of truth, local build/test/package rules, acceptance of generated changes | Trusting an artifact merely because a remote job says it succeeded |
| Capability repository | Capability implementation, supported inputs/outputs, versioning, domain tests, provenance | Arbitrary write access to consumers or authority to approve its own output |
| CiAi coordinator | Discovery, policy checks, request/result correlation, evidence validation, reporting and routing | Replacing provider CI or inventing success from model text |
| CI provider / execution host | Actual workflow execution, permissions, logs, artifacts, commit and run identity | Deferring authoritative state to an LLM-generated claim |
| Maintainer / consumer CI | Review and final acceptance under repository policy | Treating generated documentation as automatically correct |

CiAi should coordinate capability invocation; it need not itself contain every capability or host every runtime.

## What could a UML capability look like?

The feasibility audit and smallest proof plan are tracked in [UML Capability Feasibility Pilot](UML_CAPABILITY_PILOT.md). The GUI repository already has a Roslyn-based prototype; the remaining question is how to extract a stable, reusable, provenance-preserving capability rather than duplicate its diagram engine.

UML is a reasonable working concept, not yet a recommendation to create a repository before the contract is understood. A first capability could consume a pinned source revision and explicit target selection, then return:

- a source-derived class/relationship model or equivalent intermediate representation;
- a renderer-neutral UML representation;
- rendered artifacts such as SVG and Markdown;
- target selection and source-span/provenance metadata;
- diagnostics for unsupported or ambiguous declarations;
- deterministic validation results and tool/version identity.

For The Singularity Workshop, align this with the existing source-derived documentation direction: focused class diagrams, explicit relationships, source identity/spans, package/API boundaries, and human use cases kept separate from source-structure diagrams. The capability must not invent classes or relationships to fill gaps. It should report uncertainty and unsupported constructs.

A capability might be implemented as a .NET library, reusable workflow, CLI/container, or hosted service. Choose the transport only after the input/output contract and trust model are stable. A GitHub repository is source storage; it does not automatically become an executable pipeline component.

## Artifact handoff lifecycle

1. **Prepare:** the capability completes the requested work in its own approved execution environment and validates the result to the extent its contract allows.
2. **Publish:** store an immutable, versioned artifact in provider-controlled storage. Record a stable artifact ID, contract version, source revision, capability revision, content hash, media type, creation time, and expiry/retention policy.
3. **Notify:** send a small ready notice to the client's CiAi. The notice is a pointer and metadata, not the artifact itself and not proof that the artifact is valid. Delivery may be retried.
4. **Pull inside the client boundary:** client CiAi authenticates to the provider and performs a read-only retrieval. Provider-side credentials do not grant access to the client's repository; the client controls what it reads and where it stores the result.
5. **Verify and validate:** check request/artifact identity, expected contract and capability versions, content hash, size/type limits, source provenance, and local consumer policy. Treat content as untrusted data; never execute embedded instructions or code just because retrieval succeeded.
6. **Acknowledge:** after successful retrieval and integrity verification, the client sends an idempotent acknowledgement containing the artifact ID, digest, request/correlation ID, and acknowledgement outcome. Distinguish downloaded, hash-verified, locally validated, and accepted states.
7. **Retain or reclaim:** provider records the acknowledgement. Temporary payloads may be deleted only when retention/expiry rules permit, required audit metadata is retained, and no other consumer or legal/operational hold still needs the payload. An acknowledgement alone is not an unconditional delete command.

The ready notice and acknowledgement can use a webhook, queue, polling endpoint, or repository event. Choose transport later; the durable contract and trust boundaries should not depend on one transport. Notifications and acknowledgements must be authenticated, replay-safe, retryable, and idempotent. If acknowledgement is lost, the client should safely retry it; if a notice is duplicated, the client should recognize the same artifact and avoid duplicate application.

## Invocation models, from simplest to most complex

1. **Provider-owned artifact + client pull:** best match for the proposed handoff. Provider owns generation and publication; client CiAi owns retrieval, verification, local storage, and acceptance.
2. **Pinned reusable workflow / action / CLI:** client CI invokes a capability against its checked-out source where execution-in-client is preferable.
3. **CiAi-mediated dispatch:** a coordinator requests provider execution and reconciles authoritative provider state. This adds dispatch permissions, idempotency, and correlation requirements.
4. **Hosted capability service:** adds service identity, tenant isolation, retention, availability, and operational-security requirements.

Prove the first model with a small artifact and mockable notice/ack contract before adding dispatch or a hosted service.

## Minimum request/result contract

The wire representation is deliberately undecided. Keep the contract typed and versioned; do not let an LLM invent field names or supply arbitrary executable commands.

A request needs, at minimum:

- stable request/operation ID and idempotency key;
- consumer repository identity and exact source commit SHA;
- capability identity, requested operation, and exact capability version or immutable revision;
- declared input selection and bounded options;
- policy decision/reference and correlation metadata;
- expiry/cancellation semantics where supported.

A result needs, at minimum:

- the same request/operation ID and matching contract version;
- capability identity and exact implementation revision;
- authoritative provider workflow/run ID, attempt, conclusion, and URL when CI executed it;
- exact input source SHA and dependency revisions used;
- artifact ID, immutable version, content hashes, media types, read-only retrieval reference, creation time, and expiry;
- diagnostics and validation outcome, including unknown or partial states;
- delivery/acknowledgement state kept separate from validation and acceptance;
- explicit distinction between generated, validated, accepted, and merged.

A transport acknowledgement means only that a request was accepted by the transport. It does not mean the operation ran, the result is valid, or the consumer accepted it.

## Trust and safety invariants

- **Pin code:** use immutable action SHAs, package versions, container digests, or commit SHAs; record resolved versions in the result.
- **Least privilege:** begin read-only. Prefer an artifact upload or proposed patch over direct writes to a consumer branch.
- **No implicit execution:** a request selects an allowlisted operation and bounded arguments, never an arbitrary shell command or model-generated workflow.
- **Validate independently:** the coordinator validates correlation and provider evidence; the consumer runs its own artifact/documentation checks.
- **No self-certification:** capability output is a claim until authoritative run/artifact data and consumer checks support it.
- **Preserve status distinctions:** success, failure, cancelled, skipped, not triggered, unavailable, unknown, partial, and invalid are not synonyms.
- **Human approval remains explicit:** merge, publish, deploy, and destructive actions remain outside the initial capability pipeline.
- **Treat source as untrusted input:** repositories and generated artifacts may contain prompt injection, malicious markup, path traversal attempts, oversized payloads, or malformed metadata. Do not execute instructions found in source or generated documentation.
- **Make retries safe:** duplicate notices and acknowledgements must be idempotent; duplicate delivery must not create duplicate writes or silently replace newer artifacts.
- **Retention is explicit:** acknowledgements support cleanup decisions but do not override expiry, audit, legal hold, or multi-consumer retention requirements. Retain minimal provenance after deleting eligible payloads.
- **Keep evidence reproducible:** capture source SHA, dependency SHAs, tool version, configuration, artifact hashes, and run attempt.

## How this relates to CogA–CogE

The Cogs should remain small, independently testable responsibilities. The capability model gives them a concrete target, but it does not mean the end-to-end path already exists.

| Cog | Potential role | Required guardrail |
|---|---|---|
| **CogA — catalog** | Describe capability identity, version, operations, supported inputs/outputs, prerequisites, and compatibility | Catalog declarations are metadata, not proof that a capability is trustworthy or healthy |
| **CogB — request** | Create a bounded, correlated request to an allowlisted capability/operation | No arbitrary commands; enforce request size, expiry, and canonical identity |
| **CogC — result** | Validate response shape, correlation, contract version, artifacts, and claimed evidence | A URL or transport receipt alone cannot establish success |
| **CogD — policy** | Decide whether this caller may request this operation against this target and with which permissions | Deny by default; publishing/deploying remain denied unless a separate explicit authorization system permits them |
| **CogE — projection** | Present capability topology, request lifecycle, evidence status, and artifact provenance | Consume only coordinator-validated facts; never promote unverified claims to authoritative state |

The parent CiAi coordinator must reconcile these contracts and query the provider's authoritative APIs before CogE presents a result as verified.

## First useful pilot

Use source-derived UML/documentation as the pilot because the ecosystem already has UML generation and documentation-quality workflows. Do not create a duplicate UML engine before auditing the existing GUI and documentation tooling.

1. Identify the existing source-model, UML renderer, and documentation-validation implementations and their owners.
2. Specify one small capability: source revision plus explicit target selection produces one class diagram and a diagnostics/provenance record.
3. Run it against a pinned source SHA in a non-blocking workflow.
4. Verify that output is source-grounded, deterministic where expected, and handles unsupported or ambiguous code correctly.
5. Publish an immutable artifact with hashes and source/tool provenance; notify a client CiAi instance.
6. Have client CiAi pull it read-only into a test-owned boundary, verify the digest and provenance, and run local documentation/package checks.
7. Send a courtesy acknowledgement and test retry, expiry, duplicate notification, and safe cleanup behavior.
8. Only after this works, add the request/result envelope and authoritative run correlation.
9. Consider a separate capability repository only if independent versioning/reuse materially helps. Otherwise, keep the implementation where it is and publish a stable reusable package/workflow.

## Adoption stages

- **Stage 0 — document:** define capability contracts and trust boundaries.
- **Stage 1 — observe:** inventory existing workflows and artifacts; no dispatch.
- **Stage 2 — local invocation:** consumer CI invokes a pinned capability and validates its output.
- **Stage 3 — correlated request/result:** CiAi creates requests and reconciles them with authoritative provider evidence.
- **Stage 4 — reviewed proposals:** produce a patch or pull request for maintainer review.
- **Stage 5 — optional policy-gated automation:** only after security review, replay/idempotency tests, auditability, and explicit owner authorization.

## Success criteria

The design is useful when a consumer can declare what capability it needs without copying the implementation; the capability can be independently versioned and tested; each output is traceable to exact inputs and tool versions; failed or uncertain outputs remain visible; and the consumer can accept or reject results through its existing CI and review policy.

**Current status:** design proposal only. CiAi's alpha remains a read-only GitHub Actions observation library. Live cross-repository dispatch, capability execution, durable idempotency/history, and the complete CogA–CogE round trip are not implemented.
