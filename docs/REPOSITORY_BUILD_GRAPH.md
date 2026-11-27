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
