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

Run offline contract checks and pack the library:

```bash
dotnet run --project tests/TheSingularityWorkshop.CiAi.ContractTests --configuration Release
dotnet pack src/TheSingularityWorkshop.CiAi/TheSingularityWorkshop.CiAi.csproj --configuration Release
```

## 02 — Package

- Package ID: `TheSingularityWorkshop.CiAi`
- Current candidate: `0.1.0-alpha.1`
- Target framework: `.NET 8`
- Dependencies: none in the core library
- License: MIT

The package candidate is configured with repository metadata, release notes, symbols, and this README embedded in the NuGet package. CI builds, runs offline checks, and packs the library. A passing pack job is not a NuGet publication.

## 03 — Architecture and trust boundary

- **CI provider:** source of truth for repository identity, workflow run state, commit SHA, conclusion, and run evidence.
- **CiAi:** validates and normalizes CI facts; the current GitHub Actions client is read-only.
- **HeadlessAi:** provider-neutral HTTP/LLM integration is a future CiAi integration, not yet called by this package.
- **ProtocolAi:** controlled vocabulary integration is not yet wired into CiAi.
- **FSM_API:** optional lifecycle coordination; not CI transport, persistence, or a distributed scheduler.
- **SimpleDemos:** contains a separate live provider-ingestion proof; it does not prove an end-to-end LLM-driven CiAi workflow.

The current client validates the returned repository and run ID, maps unknown provider conclusions to an explicit `Unknown` value, and never treats an unknown conclusion as success. It does not dispatch work, cancel runs, publish packages, or deploy applications.

## 04 — Source-derived contract documentation

- [Read-only CI observation UML and use case](docs/UML/CiRunObservationUML.md)
- [RepositoryIdentity class companion](src/TheSingularityWorkshop.CiAi/RepositoryIdentity.md)

## 05 — Release status

**Implementation readiness: 30%. Package preparation: 70%. Candidate: 0.1.0-alpha.1.**

The latest verified candidate commit, `f207fc028a79a19305c7296a9e930c06b64ad40d`, passed the [CiAi Contract Verification workflow](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/runs/40040017424), including build, offline contract checks, NuGet packing, and packed-artifact validation. Re-run CI on the exact final candidate SHA after any further changes. Owner review and explicit publication approval remain required.

Not yet implemented: HeadlessAi invocation from CiAi, ProtocolAi vocabulary integration, host permission policy, live cross-repository request/result round trip, and durable idempotency/history. Those are explicitly outside this read-only alpha package's claims.

See [release readiness](docs/RELEASE_READINESS.md), the [inter-repository model](docs/INTER_REPOSITORY_MODEL.md), the [capability repository architecture](docs/CAPABILITY_REPOSITORIES.md), the [pull-based artifact handoff contract](docs/ARTIFACT_HANDOFF_CONTRACT.md), the [Cogs-versus-capabilities model](docs/COGS_VS_CAPABILITIES.md), the [ecosystem CI integration roadmap](docs/ECOSYSTEM_CI_INTEGRATION_ROADMAP.md), and the [repository-specific integration samples](docs/Samples/README.md).
