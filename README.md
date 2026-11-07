<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# The Singularity Workshop CiAi

[![Sith Policy](assets/sith-policy.svg)](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/sith-policy.yml)

**CiAi exposes CI to AI. It is not an AI-powered CI runner.** The lowercase `i` is intentional: CiAi supplies bounded capabilities and verified facts that an LLM can use.

CiAi is the application-owned boundary between continuous-integration systems and an LLM. CI providers remain authoritative for run state and evidence. CiAi normalizes provider data into typed contracts; model output does not grant permissions or turn arbitrary text into executable commands.

## 01 — See CiAi

**[Preview the interactive CiAi experience source](index.html)** — explore the evidence-observation flow, test an illustrative fail-closed gate, and follow the links to the Workshop capabilities that support it. The page and Pages deployment workflow are staged on `development`; the hosted experience will be available after the reviewed promotion to `master`. The browser walkthrough uses clearly labeled synthetic data; it does not claim to connect to GitHub.

## 02 — See the implementation in a minute

Requires the .NET 8 SDK.

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

## 03 — First real consumer: AnyApp

AnyApp is CiAi's first source-based consumer pilot. It keeps GitHub Actions as the authoritative build/test/publish executor and uses CiAi to observe the exact AnyApp commit, run attempt, jobs, and steps. The integration is read-only and advisory; its first real runs are still exercising the failure-evidence path.

- [AnyApp consumer walkthrough and evidence-lane map](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/Samples/AnyApp.md)
- [AnyApp CiAi evidence workflow](https://github.com/TrentBest/AnyApp/blob/development/.github/workflows/ciai-evidence.yml)

## 04 — Architecture and proof

- [Gallery](Gallery.md) — current demonstrations and evidence.
- [Cross-Cog integration](integration/CogEcosystem.ContractTests/README.md) — pinned in-process contract proof.
- [Inter-repository model](docs/INTER_REPOSITORY_MODEL.md) — request/result workflow and integration gaps.
- [Release readiness](docs/RELEASE_READINESS.md) — package and authorization boundaries.
- [FSM_COS coordination notes](FSM_COSLLM.md) — read-only evidence pilot and cross-repository contract.

The visitor experience is an introduction, not a substitute for source, tests, CI runs, or the implementation status recorded by each owning repository.
