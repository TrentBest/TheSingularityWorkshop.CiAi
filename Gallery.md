# CiAi Gallery

CiAi exposes continuous-integration facts to applications and LLMs without handing the model execution authority.

## Visitor experience

- **[Live CiAi Pages site](https://trentbest.github.io/TheSingularityWorkshop.CiAi/)** — deployed from `development`; the page includes live workflow-status badges and an animated evidence-flow diagram. Badge state reports workflow execution only, not release authorization.

- **[Preview the interactive CiAi experience source](index.html)** — a browser-based introduction to observation, verification, commit-bound correlation, and fail-closed evidence. The site deploys from `development` through the repository's Pages Actions workflow; `master` remains the release-aligned branch.
- **[Evidence walkthrough](index.html#explore)** — synthetic scenarios demonstrate complete evidence versus missing required evidence. They are explicitly labeled as illustrative, not live GitHub results.
- **[Ecosystem integration plan](docs/ECOSYSTEM_INTEGRATION.md)** — how CiAi can support Learning, Physics, GUI, and FSM_API without adding unnecessary dependencies or taking ownership of their CI.

## Verified implementation paths

- [Observe a real run](README.md#02--see-the-implementation-in-a-minute) — run the .NET sample against a repository and run ID.
- [Cross-Cog vertical proof](integration/CogEcosystem.ContractTests/README.md) — pinned integration of catalog, policy, request, result correlation, artifact verification, receipt provenance, and lifecycle projection.
- [Cross-Cog integration CI](.github/workflows/cog-ecosystem.yml) — repeatable build and six-check contract run against pinned Cog commits.
- [Release readiness](docs/RELEASE_READINESS.md) — candidate package metadata, safety boundary, and publication gate.
- [Inter-repository model](docs/INTER_REPOSITORY_MODEL.md) — planned request/result workflow and integration gaps.

The browser walkthrough, run-observation sample, and cross-Cog tests are distinct proofs. None is a remote LLM-driven round trip. Keep the gallery synchronized with behavior demonstrated by source and CI.
