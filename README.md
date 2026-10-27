# The Singularity Workshop CiAi

**CiAi exposes CI to AI. It is not an AI-powered CI runner.** The lowercase `i` is intentional: CiAi supplies bounded capabilities and verified facts that an LLM can use.

CiAi is the application-owned boundary between continuous-integration systems and an LLM. CI providers remain authoritative for run state and evidence. CiAi normalizes provider data into typed contracts; model output does not grant permissions or turn arbitrary text into executable commands.

## 01 — See it in a minute

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

Use a real 40-character SHA in place of the placeholder. The observer uses bounded pagination and writes raw run/job evidence. To evaluate a policy, configure `CIAI_REQUIRED_WORKFLOW_PATHS` as a semicolon-separated list of exact workflow paths, for example:

```bash
export CIAI_REQUIRED_WORKFLOW_PATHS=".github/workflows/ci.yml;.github/workflows/benchmarks.yml"
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- TrentBest/TheSingularityWorkshop.CiAi <full-40-character-commit-sha> artifacts/commit-observation.json
```

Only explicitly required workflow identities block the policy; unrelated runs remain visible as diagnostics. Selectors may bind a workflow to an exact run and attempt, for example `.github/workflows/ci.yml#123456789#2`. Missing, duplicate, non-terminal, unsuccessful, or incomplete required evidence fails closed. The report separates API discovery completeness, required-evidence completeness, terminality, and success. Without a configured required-workflow policy, `complete` is false. The report is a raw, read-only observation—not a release verdict or publication approval.

Run offline contract checks and pack the library:

```bash
dotnet run --project tests/TheSingularityWorkshop.CiAi.ContractTests --configuration Release
dotnet pack src/TheSingularityWorkshop.CiAi/TheSingularityWorkshop.CiAi.csproj --configuration Release
```

## 02 — Package

- Package ID: `TheSingularityWorkshop.CiAi`
- Current candidate: `0.1.0-alpha.1`
- Target framework: `.NET 8`
- Runtime dependency: [NuGet.Versioning 7.9.0](https://www.nuget.org/packages/NuGet.Versioning/), used for NuGet-native version parsing and precedence
- License: MIT

The package candidate is configured with repository metadata, release notes, symbols, and this README embedded in the NuGet package. The dependency-update evaluator uses Microsoft's `NuGet.Versioning` library rather than a hand-maintained approximation of NuGet version semantics. CI builds, runs contract checks, and packs the library. A passing pack job is not a NuGet publication.

## 03 — Architecture and trust boundary

- **CI provider:** source of truth for repository identity, workflow run state, commit SHA, conclusion, and run evidence.
- **CiAi:** validates and normalizes CI facts; the current GitHub Actions client is read-only.
- **HeadlessAi:** provider-neutral HTTP/LLM integration is a future CiAi integration, not yet called by this package.
- **ProtocolAi:** controlled vocabulary integration is not yet wired into CiAi.
- **FSM_API:** optional lifecycle coordination; not CI transport, persistence, or a distributed scheduler.
- **SimpleDemos:** contains a separate live provider-ingestion proof; it does not prove an end-to-end LLM-driven CiAi workflow.

The read-only GitHub Actions client validates provider identity and preserves unknown conclusions instead of treating them as success. Separately, `CiPipelineExecutor` on `development` can execute registered in-process delegates sequentially, blocks privileged capabilities, and records step outcomes; `CiRequiredEvidenceEvaluator` checks required facts against an exact repository and full commit SHA. These are bounded building blocks, not yet an end-to-end CI policy host: the executor report includes wall-clock timestamps, `bool false` is the only returned value interpreted as a failed result (other values are outputs, not status codes), and no GitHub Actions adapter yet makes this executor the authority for consumer build/test/coverage/docs/benchmark gates. Do not use the prototype executor for operations that report failure through integer exit codes or other non-boolean values. The observer client itself remains read-only and does not dispatch work, cancel runs, publish packages, or deploy applications.

## 04 — Source-derived contract documentation

- [Workflow domain map: 22 workflow families and cross-cutting contracts](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_WORKFLOW_DOMAIN_MAP.md)
- [Declarative workflow plan validation: stable IDs, domain references, dependency graphs, and deterministic recipes](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_WORKFLOW_DOMAIN_MAP.md#validated-workflow-plans)


- [Declarative CI builder and publication-authorization design](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_BUILDER_DESIGN.md) — recipe-style pipeline direction. The definition builder, bounded in-process executor, and fail-closed required-evidence evaluator now exist on `development`; canonical deterministic reporting, provider-host integration, and publication remain incomplete.
- [CI user workflows and fluent vocabulary](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CI_USER_WORKFLOWS.md) — user-centered workflows and the words the fluent API should express; proposed capabilities are clearly separated from implemented ones.

- [UML capability feasibility pilot](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML_CAPABILITY_PILOT.md)

- [Read-only CI observation UML and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunObservationUML.md)
- [RepositoryIdentity class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/RepositoryIdentity.md)
- [CiRunEvidenceVerifier class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/CiRunEvidenceVerifier.md)
- [Evidence reconciliation UML and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/CiRunEvidenceVerifierUML.md)
- [Artifact handoff contract types and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/ArtifactHandoffContractsUML.md)
- [Artifact handoff contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/ARTIFACT_HANDOFF_CONTRACT.md)
- [ArtifactManifest class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/ArtifactManifest.md)
- [ArtifactReadyNotice class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/ArtifactReadyNotice.md)
- [ArtifactReceiptAcknowledgement class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/ArtifactReceiptAcknowledgement.md)
- [Bounded artifact stream reader class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/ArtifactPayloadReader.md)
- [Durable receipt ledger class companion](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/src/TheSingularityWorkshop.CiAi/FileArtifactReceiptLedger.md)
- [Durable receipt ledger UML and use case](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/UML/FileArtifactReceiptLedgerUML.md)
- [CogA–CogE responsibilities and integration boundary](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/COGS_VS_CAPABILITIES.md)
- [CogA capability catalog](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogA)
- [CogB bounded request ledger](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogB)
- [CogC result correlation](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogC)
- [CogD default-deny policy](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogD)
- [CogE lifecycle projection](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogE)
- [Pinned cross-Cog vertical contract proof](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/integration/CogEcosystem.ContractTests/README.md)
- [Cross-Cog integration workflow](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/cog-ecosystem.yml)

## 05 — Automated serialization benchmark handoff

The `.github/workflows/serialization-benchmark-dispatch.yml` watcher polls the `FSM_Serialization/development` branch for a successful build of the exact current commit, then dispatches a benchmark notice. It does not publish a package.

- GitHub scheduled workflows have a five-minute minimum cadence; the workflow wakes every five minutes and gates actual polling with the repository Actions variable `SERIALIZATION_BENCHMARK_POLL_MINUTES`.
- Supported values are `5`, `10`, `15`, `20`, `30`, or `60`. If unset, the default is `15` minutes for current testing.
- Change it under **Settings → Secrets and variables → Actions → Variables**. The interval is configurable without editing workflow code.
- **Run workflow** manually bypasses the schedule gate and polls immediately.
- The repository secret `BENCHMARK_DISPATCH_TOKEN` must authorize repository dispatches to `TrentBest/FSM_Serialization_Benchmarks` (Contents: write).
- The source SHA is carried as a stable `noticeId`; the receiving workflow should deduplicate that ID because the sender's Actions cache marker is a best-effort duplicate guard, not a transactional exactly-once guarantee.

The polling cadence is an operational setting, not a benchmark definition or a release authorization.

## 06 — Release status

**Implementation readiness: 42%. Package preparation: 70%. Candidate: 0.1.0-alpha.1.**

Release evidence is candidate-specific: use the latest successful [CiAi Contract Verification workflow](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/ci.yml) for the exact source commit under review. The workflow builds, runs offline contract checks, packs both NuGet and symbol packages, validates package metadata and embedded README, and uploads the artifacts for inspection. A green workflow and inspected package are not publication approval; owner review and explicit publication approval remain required.

The artifact handoff has typed manifest, ready-notice, and receipt-acknowledgement contracts, bounded caller-supplied stream reading, SHA-256 verification, provider repository/run/source-revision receipt correlation, and a dependency-free file-backed receipt ledger that suppresses exact retries and rejects conflicting reuse of a deduplication key. The stream reader does not fetch the manifest URI or authenticate a provider, and the ledger does not authorize cleanup. Not yet implemented: HeadlessAi invocation from CiAi, ProtocolAi vocabulary integration, host permission policy, live cross-repository request/result round trip, provider-authenticated URI retrieval, durable request-level idempotency/history, and retention/reclamation.

See [release readiness](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/RELEASE_READINESS.md), the [inter-repository model](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/INTER_REPOSITORY_MODEL.md), the [capability repository architecture](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CAPABILITY_REPOSITORIES.md), the [pull-based artifact handoff contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/ARTIFACT_HANDOFF_CONTRACT.md), the [Cogs-versus-capabilities model](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/COGS_VS_CAPABILITIES.md), the [ecosystem CI integration roadmap](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/ECOSYSTEM_CI_INTEGRATION_ROADMAP.md), and the [repository-specific integration samples](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/Samples/README.md).
