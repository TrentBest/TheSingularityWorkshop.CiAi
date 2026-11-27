# Repository build graph

**Status:** the renderer-neutral configuration graph projection is implemented on `development`. SVG rendering and automatic Markdown embedding are the next integration slice, coordinated with [GUI #90](https://github.com/TrentBest/TheSingularityWorkshop.GUI/issues/90). This document does not claim that a generated SVG or live Pages graph exists yet.

## Purpose

The repository graph makes the effective CiAi fluent composition visible. It is generated from the immutable `CiAiRepositoryDefinition`, not from a separately maintained illustration.

The first contract is `ciai.repository-configuration-graph.v1`, produced by `CiAiRepositoryGraphSnapshot.FromDefinition(...)`.

## What must be visible

Every effective declaration represented by the current fluent builder has a graph representation:

| Fluent declaration | Graph representation |
|---|---|
| `WithBadge(...)` | Badge node linked to the repository |
| `WithDependency(...)` direct | Directed dependency edge to the upstream repository |
| `WithDependency(CiAi.Logic.Or(...))` | Fallback group plus individually ordered candidate edges |
| `ExtendWith(...)` / `WithGauntlet()` | Extension node with version |
| `WithNamedDevelopmentPipeline(...)` | Explicit pipeline node |
| No named pipeline | Implicit default pipeline, visibly marked as implicit |
| `WithRelationship(...)` | Relationship node plus source, target, and authority links |
| `WithADP(...)` | ADP settings node, including repair bound and write-approval requirement |
| `WithLLMAllocation(...)` | Provider/model node with concurrency, call, token, window, and priority limits |

The graph is a projection of the effective immutable definition. Repeated declarations that are rejected or are semantic no-ops do not appear as extra phantom nodes. Ordered fallback candidates retain their order.

## Federated dependency registration

A source repository can now produce a deterministic `ciai.dependency-registration.v1` snapshot from its effective `CiAiRepositoryDefinition` using `CiAiDependencyRegistrationSnapshot.FromDefinition(definition, exactSourceSha)`. The payload lists every direct dependency and every ordered fallback candidate, including capability, declaration order, and a stable registration identity. The exact 40-character source commit SHA binds the complete snapshot to the declaration revision.

This is an export contract, not a transport or receiver implementation. Creating the snapshot performs no network I/O and does not modify another repository. A trusted, explicit synchronization stage may deliver each registration to its target. The target must retain it as derived inbound information; the source remains authoritative for its outgoing declaration. A newer complete snapshot, including an empty one, is the reconciliation input from which removed registrations can be withdrawn. Receivers must still authenticate the sender, verify source provenance and revision ordering, and reject stale or conflicting updates. A receiver-side pure reconciliation contract is now implemented by `CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(...)`. The snapshot also exposes `FromJson(...)` for strict schema-aware payload intake; it validates the exact v1 object shape (rejecting missing, duplicate, or unknown properties at both envelope and registration levels), canonical identities, registration metadata, stable declaration-derived IDs, and unique declaration/candidate positions before producing the immutable snapshot. Parsing is not authentication: the caller must still authenticate the source and verify that the payload SHA is the exact source revision before treating it as eligible for reconciliation. It accepts a complete snapshot only when the integration supplies a positive monotonic source revision after authenticating the source and verifying the exact source SHA. It treats identical same-revision delivery as an idempotent duplicate, rejects stale revisions and same-revision conflicts, rejects a different source, and replaces the active set on a newer revision (so an empty complete snapshot withdraws all prior registrations). The method is deliberately pure: it does not authenticate, prove commit ancestry, persist records, or perform transport. The trusted integration remains responsible for those duties; a caller must not treat an unverified payload as verified merely because it can construct a snapshot.

The stable registration identity intentionally excludes the source commit SHA: a declaration keeps its identity across revisions, while the enclosing snapshot records which exact revision asserted it. This supports deduplication without confusing identity with freshness.

## Three distinct graph layers

Do not collapse these views:

1. **Configuration graph** — what the fluent declaration says should exist.
2. **Execution graph** — what CiAi expands into concrete operations and dependencies.
3. **Evidence graph** — what providers and trusted hosts actually report for exact commits, runs, attempts, and artifacts.

This first projection is configuration-only. A configured dependency remains visible when it has not been built. Future execution/evidence overlays may add waiting, failed, stale, missing, or successful states, but must not rewrite the declaration or invent readiness.

## Rendering and embedding contract

The target generated artifacts are:

- `REPOSITORY_BUILD_GRAPH.svg` — deterministic, accessible standalone SVG.
- `REPOSITORY_BUILD_GRAPH.md` — companion Markdown containing an image embed of that SVG, a short explanation, the source repository, and the graph schema version.

The Markdown should use a relative image reference so the same pair works in a repository, a downloaded artifact, and a Pages site. Both outputs must be generated from the same validated graph snapshot. The SVG renderer must not reinterpret dependencies or policy.

GUI owns generic SVG layout and serialization; CiAi owns the graph model and semantics. The generic directed-graph renderer is tracked in [GUI #90](https://github.com/TrentBest/TheSingularityWorkshop.GUI/issues/90). CiAi's dashboard integration remains coordinated with [CiAi #62](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/62).

## Automation and safety

The generator is read-only with respect to repository state. CI/Pages should generate the SVG and Markdown into the build output, validate that both are present and internally consistent, and publish only through the existing explicitly authorized Pages workflow. It must not push generated files back to a branch, merge, publish packages, or grant deployment authority.

A future README embed can point at the generated SVG served by the repository's Pages site. Until the renderer and Pages artifact path are verified, do not add a broken image link or describe the graph as live.


## Federated ecosystem graph projection

`CiAiEcosystemGraphSnapshot.FromLedgers(...)` projects a supplied set of accepted source ledgers into one deterministic, renderer-neutral directed graph. Each repository identity has one node, so shared dependencies, diamonds, and cycles do not duplicate nodes or require recursive traversal. Edges point from the declaring source repository to its declared dependency and retain registration identity, dependency kind, capability, declaration/candidate order, source revision, and exact source SHA.

The focal repository is explicit even when no ledger for it was supplied. The snapshot reports `CiAiEcosystemGraphCoverage.Partial` by design: a collection of discovered ledgers cannot establish that unobserved repositories or declarations do not exist. Complete coverage would require a separate authoritative repository inventory and verified source coverage for every inventory entry.

ADP clusters are supplied separately as `CiAiAdpClusterDefinition` values. Each cluster has an explicit identity, title, and member-repository set, projected as a cluster node and membership edges. Neither dependency adjacency nor visual proximity creates ADP membership. The graph model leaves dashed boundary geometry and title-gap rendering to GUI's generic renderer.
