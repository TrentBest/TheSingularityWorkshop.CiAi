# Cogs and Capability Providers

**Status:** first standalone CogA–CogE slices and a pinned in-process vertical contract proof are implemented; remote handoff and runtime orchestration remain incomplete.

CiAi needs two kinds of modularity.

## Cogs: control-plane responsibilities

- **CogA — catalog:** identify declared repositories, capabilities, versions, and supported contracts.
- **CogB — request:** create a bounded, correlated request for an allowed operation.
- **CogC — result:** correlate a returned result with its request and validate its structure.
- **CogD — policy:** decide whether an operation is permitted for a caller and target.
- **CogE — projection:** present validated topology and evidence.

These responsibilities concern identity, policy, correlation, evidence, and presentation. They do not implement every domain operation.

## First implementation slices

| Cog | Implemented slice | Current boundary | Source and verification |
|---|---|---|---|
| CogA | Immutable capability descriptors; in-memory catalog; exact lookup, duplicate rejection, stable snapshots, removal | No remote discovery or authorization | [Repository](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogA), [CI](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogA/actions) |
| CogB | Pinned request contract; bounded output; input digest; in-memory idempotent request book | No durable idempotency or dispatch | [Repository](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogB), [CI](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogB/actions) |
| CogC | Provider result contract; request/provider/source/contract correlation | Parent manifest and authoritative CI evidence still need adapters | [Repository](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogC), [CI](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogC/actions) |
| CogD | Exact-match, time-bounded, default-deny policy rules | No authenticated identity source or durable policy store | [Repository](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogD), [CI](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogD/actions) |
| CogE | Timestamped lifecycle facts; ordered projection; identity and contradiction diagnostics | No durable event ingestion or live reporting UI | [Repository](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogE), [CI](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogE/actions) |

These are intentionally small, dependency-free first slices. Their separate CI checks prove each package's build, offline contract suite, and package-candidate creation. The [pinned cross-Cog integration workflow](../.github/workflows/cog-ecosystem.yml) additionally proves the in-process mapping across all five cogs and the parent CiAi artifact contracts. Neither level proves remote retrieval, authenticated notification/acknowledgement, provider-run verification, or end-to-end network orchestration.

The parent CiAi coordinator must connect the contracts and verify provider-owned facts. Passing isolated Cog tests does not prove the end-to-end lifecycle.

## Capability providers: domain work

Potential capabilities include source-to-UML documentation, package/API compatibility analysis, dependency conflict analysis, documentation validation, test-evidence summaries, benchmark comparison, and package-content inspection.

These are candidates, not a commitment to create a separate repository for every idea. A provider may be a package, reusable workflow, CLI, container, hosted service, or an existing repository with a stable interface.

## Target lifecycle

1. CogA identifies a declared capability and its contract.
2. CogD evaluates the requested operation and target.
3. CogB creates or records a bounded request with stable identity and pinned inputs.
4. The capability completes work in its own environment and publishes an immutable, versioned artifact.
5. The provider sends a ready notice containing an artifact reference and integrity/provenance metadata—not the payload itself.
6. Client CiAi authenticates and pulls the artifact read-only into the client's security boundary, then verifies identity, digest, contract, and provenance.
7. CogC correlates the artifact and acknowledgement with the request; the parent coordinator verifies authoritative provider CI facts where applicable.
8. CogE presents verified lifecycle facts, distinguishing published, notified, retrieved, hash-verified, locally validated, accepted, and unknown/failed states.
9. Client CiAi sends a courtesy acknowledgement after retrieval and integrity verification. Provider retention policy—not the acknowledgement alone—determines when temporary payloads can be reclaimed.
10. Consumer CI and maintainers decide whether to accept the output.

This is a target architecture. The current CiAi alpha provides read-only GitHub Actions observation and normalization plus local handoff contract primitives; it does not yet execute the complete lifecycle.

## Example: UML

A UML provider could accept a pinned source revision and explicit target selection, then return source-grounded diagrams, diagnostics, source identity/spans, tool version, and artifact hashes. A consumer can validate and review those outputs without copying the UML implementation.

Before creating a separate UML repository, audit the existing GUI source-derived UML and documentation workflows. A separate provider is justified when multiple consumers need it, it has an independent release lifecycle, or isolation and testing materially improve the design. Otherwise, keep the implementation with its current owner and expose a reusable, versioned interface.

## Trust boundaries

- Pin provider code and dependencies to immutable revisions.
- Start in read-only, non-blocking mode.
- Never treat a ready notice or transport acknowledgement as proof of execution, integrity, or acceptance.
- Prefer provider-published immutable artifacts that client CiAi pulls into its own trust boundary; do not grant provider write access to the client repository.
- Verify content hashes and provenance before local use. A retrieval acknowledgement is distinct from local validation and acceptance.
- Make notifications and acknowledgements authenticated, retry-safe, and idempotent; apply explicit expiry/retention rules before reclaiming temporary storage.
- Keep generated, validated, accepted, and merged as distinct states.
- Capture source SHA, dependency SHAs, provider version, run/attempt, and artifact hashes.
- Treat repository source and generated artifacts as untrusted input.
- Make retries safe and preserve cancelled, skipped, unavailable, partial, and unknown states.
- Keep merge, publish, deploy, and destructive actions behind separate explicit authorization.

## Design rule

**Cogs coordinate; capabilities do domain work; providers publish immutable artifacts; client CiAi pulls and verifies within its own security boundary; the consumer decides acceptance.**

Keep the control plane small and generic. Add capability providers behind stable contracts rather than adding a new Cog for every new feature. Before remote dispatch, reconcile the CogA–CogE contracts, verify provider runs authoritatively, and test correlation, replay handling, permissions, and failure semantics.
