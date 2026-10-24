# CiAi Gallery

CiAi exposes continuous-integration facts to applications and LLMs without handing the model execution authority. The current living proof is the read-only GitHub Actions run observation: a provider response becomes a validated, typed snapshot with explicit status, conclusion, commit SHA, and evidence URL.

- [Observe a real run](README.md#01--see-it-in-a-minute) — run the console sample against a repository and run ID.
- [Cross-Cog vertical proof](integration/CogEcosystem.ContractTests/README.md) — pinned integration of catalog, policy, request, result correlation, artifact verification, receipt provenance, and lifecycle projection.
- [Cross-Cog integration CI](.github/workflows/cog-ecosystem.yml) — repeatable build and six-check contract run against pinned Cog commits.
- [Release readiness](docs/RELEASE_READINESS.md) — candidate package metadata, safety boundary, and publication gate.
- [Inter-repository model](docs/INTER_REPOSITORY_MODEL.md) — planned request/result workflow and integration gaps.

The run-observation sample and cross-Cog test are console proofs, not a GUI or a remote LLM-driven round trip. Keep the gallery synchronized with behavior demonstrated by source and CI.
