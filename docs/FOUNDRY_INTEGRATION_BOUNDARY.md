# Foundry and CiAi responsibility boundary

**Trent Best · The Singularity Workshop**

**Status: contract guidance for the current integration; runtime integration is not yet implemented.**

## Purpose

Foundry owns the Hub's general workflow-execution lifecycle. CiAi owns CI-specific orchestration, CI evidence, release-candidate evaluation, and CI authorization contracts. The two systems may cooperate, but CiAi must not become a second general-purpose Hub runtime.

GUI is an optional presentation surface. Neither headless CI policy evaluation nor CI execution-host adapters may require GUI.

## Responsibility split

| Concern | Owner | Boundary |
|---|---|---|
| General Hub workflow lifecycle | Foundry | Scheduling and coordinating general workflows, lifecycle state, and workflow execution under Hub policy |
| CI recipe and CI-specific policy | CiAi | Declared CI checks, required evidence, CI candidate evaluation, and CI-specific release gates |
| Provider facts | CI provider / execution host | Authoritative workflow, job, step, source revision, and artifact state |
| Presentation | GUI | Optional visualization and interaction; not a runtime dependency of headless CI |
| Privileged publication/deployment | Explicit host authorization plus bounded adapter | Candidate-bound permission and post-action verification; never inferred from successful CI |

## Integration contract

When Foundry invokes CiAi, the integration should be an explicit, versioned contract rather than a project-reference cycle or implicit shared runtime:

- Foundry supplies a stable workflow/request identity and the bounded CI operation it wants evaluated or coordinated.
- CiAi returns typed CI-specific status, evidence references, diagnostics, and unresolved gates. Unknown, missing, stale, mismatched, or incomplete evidence remains non-success.
- CI provider facts remain attributable to the exact repository, full source commit, workflow run, and attempt wherever applicable.
- Foundry retains ownership of the wider Hub workflow lifecycle. CiAi does not create a parallel scheduler or silently take over unrelated workflows.
- Each side depends only on the narrow contracts it consumes. Avoid circular project dependencies; a small neutral contract package or host-owned adapter is preferable if a shared contract is needed.
- GUI may visualize plans and results through a separate adapter, but headless execution and CI contract tests must work without GUI.

The exact request/response types and package boundary must be confirmed against Foundry's current public API before implementing a reference or adapter. This document does not claim that such an adapter exists.

## Safety invariants

1. Defining or rendering a CI recipe does not execute it.
2. Observing a successful provider run is evidence, not owner authorization.
3. A passing CI evaluation is not permission to publish or deploy.
4. Any authorization must be explicit and bound to the exact candidate and target.
5. Publishing/deployment remains disabled unless the owner explicitly authorizes the candidate; successful execution must be verified afterward.
6. If Foundry/CiAi integration, provider evidence, or a required adapter is unavailable, report the capability as unavailable or blocked. Do not silently bypass a required gate.

## Implementation status

CiAi currently has a declarative pipeline builder, a bounded in-process executor, a required-evidence evaluator, and read-only GitHub Actions observation on `development`. Those pieces do not yet form a complete Foundry-hosted CI workflow. A GitHub Actions host adapter and publishing adapter are not implemented. This is design guidance, not proof of live Foundry integration.

Related work: [issue #19 — Treat Foundry as the Hub execution pipeline; keep CiAi CI-specific](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/19).
