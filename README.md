# The Singularity Workshop CiAi

**CiAi exposes CI to AI. It is not an AI-powered CI runner.** The lowercase `i` is intentional: these are capabilities and facts for an LLM to use.

CiAi is the application-owned boundary between continuous-integration systems and an LLM. CI providers remain the authority for run state and evidence. CiAi converts provider data into bounded contracts, exposes only explicitly permitted operations, and returns evidence that can be inspected by people or models.

## Architecture

- **CI provider** — source of truth for repositories, workflow runs, commit SHAs, conclusions, and run URLs.
- **CiAi** — validates and normalizes CI facts, defines the supported intent/action boundary, and rejects unsupported or unauthorized requests.
- **HeadlessAi** — sends normalized input to a configured endpoint through an explicit adapter. It removes a hard dependency on one provider SDK; it does not remove the need for a configured endpoint.
- **ProtocolAi** — maps application-owned vocabulary to integer identities and validates protocol payloads. Unknown values can remain literals; they are never silently promoted into executable commands.
- **FSM_API** — optional lifecycle coordination where explicit state transitions help. It is not the CI transport, persistence layer, or distributed scheduler.
- **SimpleDemos** — a place for a small, inspectable end-to-end demonstration.

## First proof

The first milestone establishes a typed, validated CI-run contract and a deterministic smoke test without requiring API keys or making network calls. It intentionally does not claim that live GitHub ingestion or LLM interaction is already implemented.

Run:

```bash
dotnet run --project tests/TheSingularityWorkshop.CiAi.ContractTests
```

The smoke test checks valid snapshots, rejects invalid identity/evidence, and confirms unknown provider states remain explicitly unknown.

## Safety boundary

An LLM may interpret a request and explain evidence. It does not decide whether a run succeeded, which repository a request targets, or whether an operation is authorized. Every action must be validated by host code against trusted CI data and an explicit permission policy. The first milestone is read-only. No secrets, retries, cancellations, publishing, or deployments are enabled.

## Status

- **Implementation readiness: 10%** — repository purpose, boundaries, typed CI-run contract, and offline smoke-test foundation.
- **Not yet implemented:** live GitHub API ingestion, HeadlessAi invocation, ProtocolAi vocabulary integration, permissioned operations, and live end-to-end verification.
