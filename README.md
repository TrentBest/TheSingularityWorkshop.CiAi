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

## Observe a real GitHub Actions run

The read-only client fetches the requested run directly from GitHub and validates the returned repository and run ID before constructing a typed snapshot.

```bash
dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- TrentBest/TheSingularityWorkshop.CiAi 40027841199
```

Replace the example run ID with a real run ID from the target repository's Actions page. Public repositories can be queried without a token; set `CIAI_GITHUB_TOKEN` when the target requires authentication. The client only performs a GET and never retries, cancels, publishes, or deploys anything.

## Verify the contracts offline

```bash
dotnet run --project tests/TheSingularityWorkshop.CiAi.ContractTests
```

The tests use a fake HTTP endpoint to check snapshot validation, repository/run identity checks, and safe handling of an unknown future conclusion. They require no API key or network call. The cross-repository [SimpleDemos proof](https://github.com/TrentBest/SimpleDemos/actions/runs/40028749226) also fetched a real completed GitHub Actions run and printed its authoritative status, conclusion, commit, and evidence URL.

## Safety boundary

An LLM may interpret a request and explain evidence. It does not decide whether a run succeeded, which repository a request targets, or whether an operation is authorized. Every action must be validated by host code against trusted CI data and an explicit permission policy. The current client is read-only. No secrets, retries, cancellations, publishing, or deployments are enabled.

## Status

- **Implementation readiness: 25%** — validated typed run contract, read-only GitHub Actions REST client, offline contract tests, observe-run console sample, and a successful live provider-ingestion demonstration from SimpleDemos.
- **Not yet implemented:** HeadlessAi invocation from CiAi, ProtocolAi vocabulary integration, permissioned operations, and live end-to-end CI demonstration.
