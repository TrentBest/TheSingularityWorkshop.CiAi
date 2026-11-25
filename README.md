<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# The Singularity Workshop CiAi

[![Sith Policy](assets/sith-policy.svg)](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/sith-policy.yml)

**CiAi exposes CI to AI. It is not an AI-powered CI runner.** The lowercase `i` is intentional: CiAi supplies bounded capabilities and verified facts that an LLM can use.

CiAi is the application-owned boundary between continuous-integration systems and an LLM. CI providers remain authoritative for run state and evidence. CiAi normalizes provider data into typed contracts; model output does not grant permissions or turn arbitrary text into executable commands.

## 01 — See CiAi

**[Preview the interactive CiAi experience source](index.html)** — explore the evidence-observation flow, test an illustrative fail-closed gate, and follow the links to the Workshop capabilities that support it. The page and Pages deployment workflow are staged on `development`; the hosted experience will be available after the reviewed promotion to `master`. The browser walkthrough uses clearly labeled synthetic data; it does not claim to connect to GitHub.

## 02 — See the implementation in a minute

Requires the .NET 10 SDK.

Observe a real GitHub Actions run:

```bash
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- TrentBest/TheSingularityWorkshop.CiAi 40027841199
```

Replace the example with a run ID that exists in the target repository. Public repositories can be queried without a token; set `CIAI_GITHUB_TOKEN` only when the target requires authentication. The client performs GET only.

Observe every matching workflow run, its exact attempt, jobs, and steps for one full commit SHA, then write JSON and Markdown evidence summaries:

```bash
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- TrentBest/TheSingularityWorkshop.CiAi <full-40-character-commit-sha> artifacts/commit-observation.json
```

Use a real 40-character SHA in place of the placeholder. The observer uses bounded pagination and writes raw run/job evidence. To evaluate a policy, configure `CIAI_REQUIRED_WORKFLOW_PATHS` as a semicolon-separated list of exact workflow paths. Missing, duplicate, non-terminal, unsuccessful, or incomplete required evidence fails closed. The report separates API discovery completeness, required-evidence completeness, terminality, and success. Without a configured required-workflow policy, `complete` is false. The report is a raw, read-only observation—not a release verdict or publication approval.

## 02 — Agentic development: CODE RED

**The primary product direction is CiAi-driven, turn-stepped AI development.** An LLM is the developer under test: it receives bounded task context, proposes a typed repository operation, and must earn a fresh CiAi verdict for the exact candidate commit. A green CI result is evidence, not permission to mutate a repository.

The intended responsibility split is:

- **CiAi:** compose repository-development operations over CommandAi's generic proposal model; define required CI policy; validate exact-run provenance and test diagnostics; issue the authoritative evidence verdict.
- **CommandAi:** own the generic versioned atomic command representation and codec. CiAi must consume its public types rather than copy them.
- **GrammarAi / ProtocolAi:** validate structural form and carry interoperable correlation, provenance, and idempotency. Neither grants authority.
- **HeadlessAi:** own bounded, resumable LLM/repair turns. Model output remains untrusted input.
- **FSM_Foundry / host:** independently authenticate and authorize each proposed operation, enforce branch/base-SHA/path/capability/budget limits, and retain human approval at the host boundary.
- **GitHub Actions:** remain the source of truth for workflow runs, exact checked-out commits, jobs, and test artifacts.

The first proof target is the manifest-driven Minesweeper Experience: a failing exact-SHA test artifact must become a provenance-bound diagnostic report, drive a bounded repair proposal for the same task, pass independent host authorization before any isolated-branch change, and then produce a new exact-candidate green verdict. Only after that loop works should the local manifest be loaded and played as an Experience.

**Implementation status:** the repository currently has draft provenance/evidence and TRX diagnostic slices; CommandAi has a separate generic proposal contract; FSM_Foundry has draft ingress, report-consumer, and secure-transport foundations. These are not yet a live connected pipeline. The AiCi proposal builder and clean-CI CommandAi source dependency are still being established. No end-to-end red → repair → green CODE RED run is claimed.

Track the coordinated acceptance path:
- [CODE RED umbrella and acceptance criteria](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/27)
- [AgentDevelop product path](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/26)
- [Provenance-bound test diagnostics](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/34)
- [CommandAi proposal composition through AiCi](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/37)
- [Experiences Minesweeper pilot](https://github.com/TrentBest/TheSingularityWorkshop.Experiences/issues/37)

## 03 — First real consumer: AnyApp

AnyApp is CiAi's first source-based consumer pilot. It keeps GitHub Actions as the authoritative build/test/publish executor and uses CiAi to observe the exact AnyApp commit, run attempt, jobs, and steps. The integration is read-only and advisory; its first real runs are still exercising the failure-evidence path.

- [AnyApp consumer walkthrough and evidence-lane map](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/Samples/AnyApp.md)
- [AnyApp CiAi evidence workflow](https://github.com/TrentBest/AnyApp/blob/development/.github/workflows/ciai-evidence.yml)

## 04 — New consumer: SingularityUniversalRoleplayingSystem

SURPS is the Workshop's shared roleplaying ontology. Its root AgentOnboarding contract now passes CiAi v1 at an exact commit, and a pinned read-only observer reports its Pages workflow with separate completeness, terminality, and success facts. The first Pages build passed but deployment failed; that failure remains visible and unresolved.

- [SURPS CiAi integration and evidence](docs/Samples/SingularityUniversalRoleplayingSystem.md)
- [SURPS onboarding issue](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/issues/1)
- [CiAi onboarding issue](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/84)

## 05 — Next consumer: ServerSC2

ServerSC2 is the next CODE RED evidence integration. ServerSC2 owns authoritative SC2 match/replay evidence; CiAi owns exact GitHub workflow/artifact provenance, test-result normalization, and the CI verdict. This integration is coordinated but not yet implemented or verified.

- [ServerSC2 evidence integration contract](docs/Samples/ServerSC2.md)
- [ServerSC2 domain implementation issue](https://github.com/TrentBest/TheSingularityWorkshop.ServerSC2/issues/1)
- [CiAi integration issue](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/67)
- [Shared CODE RED / HeadlessAi handoff](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/34)

- [Await and Gate contracts: durable dependency declarations, exact-source evidence, adjudication, and event receipts](docs/AWAIT_GATE_CONTRACTS.md)

- [CI matrix workflow domain](docs/CI_MATRIX_DOMAIN.md) — deterministic bounded expansion and cell identity contracts.

- [AiCi repository command proposal contract](docs/AI_CI_REPOSITORY_COMMANDS.md) — deterministic CommandAi/1 proposal identity and bounded repository-operation intent.

- [CI semantic stack](docs/CI_SEMANTIC_STACK.md) — typed workflow vocabulary and protocol/grammar cross-reference validation.

- [Test-result evidence v1](docs/TEST_RESULT_EVIDENCE_V1.md) — exact-run TRX provenance, required-job policy, bounded archive verification, and diagnostics.
- [Development capability evidence v1](docs/DEVELOPMENT_CAPABILITY_EVIDENCE_V1.md) — source-bound capability claims and verification.
- [Composite pipeline gates](docs/COMPOSITE_PIPELINE_GATES.md) and [dependency readiness policy](docs/DEPENDENCY_READINESS_POLICY.md) — fail-closed dependency and gate composition.

## 06 — Architecture and proof

- [Pages publication contract](docs/PAGES_PUBLICATION_CONTRACT.md) — destination-specific approval policy, failure-safe last-known-good experience, operational dashboard, and current implementation status.
- [Repository composition](docs/REPOSITORY_COMPOSITION.md) — the single immutable fluent builder for badges, dependency/fallback declarations, development pipelines, optional ADP, and LLM allocation budgets.
- [Repository manifest v1](docs/REPOSITORY_MANIFEST.md) — strict JSON declarations, built-in observer profiles, deterministic validation, and the pinned reusable workflow.
- [Development progress v1](docs/DEVELOPMENT_PROGRESS.md) — weighted, evidence-referenced readiness declarations with explicit unassessed states and a GUI.SVG rendering boundary.

- [Gallery](Gallery.md) — current demonstrations and evidence.
- [Cross-Cog integration](integration/CogEcosystem.ContractTests/README.md) — pinned in-process contract proof.
- GrammarAi is consumed from immutable source pin `028b1d1f707e3e0614c680d6fc4a556424e2e02a`; ProtocolAi is consumed from immutable source pin `13e86c23c8eab6a33f3f124342b54598748e8756`. CiAi owns CI vocabulary and workflow semantics, ProtocolAi owns stable category/member identity primitives, and GrammarAi validates the typed workflow/evidence graph. Structural validity remains separate from CI policy/evidence verdicts and host authorization.
- [Inter-repository model](docs/INTER_REPOSITORY_MODEL.md) — request/result workflow and integration gaps.
- [AgentOnboarding admission v1](docs/AGENT_ONBOARDING_ADMISSION_V1.md) — exact-commit onboarding validation, deterministic outcomes, and the host-enforcement boundary.
- [Release readiness](docs/RELEASE_READINESS.md) — package and authorization boundaries.
- [FSM_COS coordination notes](FSM_COSLLM.md) — read-only evidence pilot and cross-repository contract.

The visitor experience is an introduction, not a substitute for source, tests, CI runs, or the implementation status recorded by each owning repository.
