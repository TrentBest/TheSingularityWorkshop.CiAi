# Capability Repositories and the CiAi Domain

**Status:** proposed architecture; design only, not an implemented execution feature.  
**Owner:** CiAi coordinates the contract. Capability repositories own their algorithms and domain-specific tests.

## The idea

A future UML capability could provide a reusable operation: given a declared source snapshot and a bounded request, derive source-grounded UML artifacts and return them with provenance and validation evidence.

A consumer repository would not need to implement UML generation itself. Its CiAi integration would discover a compatible capability, construct a request, arrange for the capability to run in an approved environment, validate the result, and publish a reviewable artifact or proposed change back to the consumer.

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

UML is a reasonable working concept, not yet a recommendation to create a repository before the contract is understood. A first capability could consume a pinned source revision and explicit target selection, then return:

- a source-derived class/relationship model or equivalent intermediate representation;
- a renderer-neutral UML representation;
- rendered artifacts such as SVG and Markdown;
- target selection and source-span/provenance metadata;
- diagnostics for unsupported or ambiguous declarations;
- deterministic validation results and tool/version identity.

For The Singularity Workshop, align this with the existing source-derived documentation direction: focused class diagrams, explicit relationships, source identity/spans, package/API boundaries, and human use cases kept separate from source-structure diagrams. The capability must not invent classes or relationships to fill gaps. It should report uncertainty and unsupported constructs.

A capability might be implemented as a .NET library, reusable workflow, CLI/container, or hosted service. Choose the transport only after the input/output contract and trust model are stable. A GitHub repository is source storage; it does not automatically become an executable pipeline component.

## Invocation models, from safest to most complex

1. **Artifact consumer:** capability runs in its own CI and publishes versioned artifacts; the consumer downloads a pinned artifact and verifies it. Simple to pilot, but may not support arbitrary source revisions without a request mechanism.
2. **Reusable workflow / action / CLI:** consumer CI invokes a version-pinned capability against its checked-out source. This keeps execution inside the consumer CI boundary, but action and dependency pinning must be enforced.
3. **CiAi-mediated dispatch:** CiAi sends a bounded request to a capability repository or trusted runner, then polls authoritative provider APIs for the run and artifacts. This requires dispatch permissions, idempotency, authentication, request/result correlation, and protection against confused-deputy behavior.
4. **Hosted capability service:** CiAi calls a service endpoint. This adds service identity, tenant isolation, retention, availability, and operational-security requirements.

Start with model 1 or 2 to prove the artifact contract. Do not build distributed dispatch merely because it is architecturally possible.

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
- artifact names, content hashes, media types, and locations;
- diagnostics and validation outcome, including unknown or partial states;
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
- **Make retries safe:** duplicate delivery must not create duplicate writes or silently replace newer artifacts.
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
5. Store the artifact with hashes and source/tool provenance; have consumer CI validate links, package contents, and style requirements.
6. Only after this works, test a CiAi-created request/result envelope and authoritative run correlation.
7. Consider a separate capability repository only if independent versioning/reuse materially helps. Otherwise, keep the implementation where it is and publish a stable reusable package/workflow.

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
