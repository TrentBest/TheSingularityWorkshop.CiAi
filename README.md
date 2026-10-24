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

## 05 — Release status

**Implementation readiness: 42%. Package preparation: 70%. Candidate: 0.1.0-alpha.1.**

Release evidence is candidate-specific: use the latest successful [CiAi Contract Verification workflow](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/actions/workflows/ci.yml) for the exact source commit under review. The workflow builds, runs offline contract checks, packs both NuGet and symbol packages, validates package metadata and embedded README, and uploads the artifacts for inspection. A green workflow and inspected package are not publication approval; owner review and explicit publication approval remain required.

The artifact handoff has typed manifest, ready-notice, and receipt-acknowledgement contracts, bounded caller-supplied stream reading, SHA-256 verification, provider repository/run/source-revision receipt correlation, and a dependency-free file-backed receipt ledger that suppresses exact retries and rejects conflicting reuse of a deduplication key. The stream reader does not fetch the manifest URI or authenticate a provider, and the ledger does not authorize cleanup. Not yet implemented: HeadlessAi invocation from CiAi, ProtocolAi vocabulary integration, host permission policy, live cross-repository request/result round trip, provider-authenticated URI retrieval, durable request-level idempotency/history, and retention/reclamation.

See [release readiness](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/RELEASE_READINESS.md), the [inter-repository model](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/INTER_REPOSITORY_MODEL.md), the [capability repository architecture](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/CAPABILITY_REPOSITORIES.md), the [pull-based artifact handoff contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/ARTIFACT_HANDOFF_CONTRACT.md), the [Cogs-versus-capabilities model](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/COGS_VS_CAPABILITIES.md), the [ecosystem CI integration roadmap](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/ECOSYSTEM_CI_INTEGRATION_ROADMAP.md), and the [repository-specific integration samples](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/blob/development/docs/Samples/README.md).
