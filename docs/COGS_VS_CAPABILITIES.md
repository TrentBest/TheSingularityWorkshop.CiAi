# Cogs and Capability Providers

**Status:** proposed architecture. This document describes a target model, not implemented runtime behavior.

CiAi needs two kinds of modularity.

## Cogs: control-plane responsibilities

- **CogA — catalog:** identify declared repositories, capabilities, versions, and supported contracts.
- **CogB — request:** create a bounded, correlated request for an allowed operation.
- **CogC — result:** correlate a returned result with its request and validate its structure.
- **CogD — policy:** decide whether an operation is permitted for a caller and target.
- **CogE — projection:** present validated topology and evidence.

These responsibilities concern identity, policy, correlation, evidence, and presentation. They do not implement every domain operation.

The parent CiAi coordinator must connect the contracts and verify provider-owned facts. Passing isolated Cog tests does not prove the end-to-end lifecycle.

## Capability providers: domain work

Potential capabilities include source-to-UML documentation, package/API compatibility analysis, dependency conflict analysis, documentation validation, test-evidence summaries, benchmark comparison, and package-content inspection.

These are candidates, not a commitment to create a separate repository for every idea. A provider may be a package, reusable workflow, CLI, container, hosted service, or an existing repository with a stable interface.

## Target lifecycle

1. CogA identifies a declared capability and its contract.
2. CogD evaluates the requested operation and target.
3. CogB creates a bounded request with stable identity and pinned inputs.
4. A trusted execution host invokes the capability.
5. CogC correlates and structurally validates the response.
6. The parent coordinator checks authoritative CI state and artifact provenance.
7. CogE presents only validated facts, labelling partial or unknown information.
8. Consumer CI and maintainers decide whether to accept the output.

This is a target architecture. The current CiAi alpha provides read-only GitHub Actions observation and normalization; it does not yet execute this lifecycle.

## Example: UML

A UML provider could accept a pinned source revision and explicit target selection, then return source-grounded diagrams, diagnostics, source identity/spans, tool version, and artifact hashes. A consumer can validate and review those outputs without copying the UML implementation.

Before creating a separate UML repository, audit the existing GUI source-derived UML and documentation workflows. A separate provider is justified when multiple consumers need it, it has an independent release lifecycle, or isolation and testing materially improve the design. Otherwise, keep the implementation with its current owner and expose a reusable, versioned interface.

## Trust boundaries

- Pin provider code and dependencies to immutable revisions.
- Start in read-only, non-blocking mode.
- Never treat a request acknowledgement as proof of execution or success.
- Keep generated, validated, accepted, and merged as distinct states.
- Capture source SHA, dependency SHAs, provider version, run/attempt, and artifact hashes.
- Treat repository source and generated artifacts as untrusted input.
- Make retries safe and preserve cancelled, skipped, unavailable, partial, and unknown states.
- Keep merge, publish, deploy, and destructive actions behind separate explicit authorization.

## Design rule

**Cogs coordinate; capabilities do domain work; provider CI supplies execution evidence; the consumer decides acceptance.**

Keep the control plane small and generic. Add capability providers behind stable contracts rather than adding a new Cog for every new feature. Before remote dispatch, reconcile the CogA–CogE contracts, verify provider runs authoritatively, and test correlation, replay handling, permissions, and failure semantics.
