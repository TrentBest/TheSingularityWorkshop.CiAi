# CiAi inter-repository model

**Status: experimental design contract; dispatch and GUI are not yet implemented.**

A repository is a node in a typed, directed network. It declares what it provides, consumes, requests, or receives notification about. GitHub remains authoritative for repository identity, commit SHA, workflow state, permissions, and run evidence. "Connected" alone is not enough.


## Implementation checkpoint — 2026-10-23

The Cogs now have independent, offline-tested contract slices on their own `development` branches. They are **not yet wired together**, and none of these contract prototypes enables live cross-repository dispatch.

| Component | Current tested slice | Readiness |
|---|---|---:|
| [CogA catalog](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogA/tree/development) | Typed repository/capability catalog and offline structural validation | 40% |
| [CogB request](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogB/tree/development) | Bounded outbound request envelope, target/operation allowlists, transport interface | 20% |
| [CogC result](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogC/tree/development) | Correlation checks and structural validation of claimed run evidence | 20% |
| [CogD policy](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogD/tree/development) | ProtocolAi-backed operation vocabulary and deterministic prototype policy | 20% |
| [CogE projection](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogE/tree/development) | Presentation-neutral graph projection; no GUI or live CI reads | 20% |

Latest checked offline CI evidence for the new contract slices:
- [CogB request contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogB/actions/runs/40037366200) — passed.
- [CogC result contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogC/actions/runs/40037652342) — passed.
- [CogD policy contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogD/actions/runs/40037965079) — passed after pinning to the ProtocolAi package version actually available on NuGet.
- [CogE topology projection](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogE/actions/runs/40037901319) — passed.

The parent integration remains the critical gap: reconcile CogA's catalog vocabulary with CogD/ProtocolAi; connect CogB request identity to CogC results; verify claims against the authoritative GitHub run API in CiAi; then feed only reconciled evidence into CogE. A URL-shaped claim or a successful transport receipt is never enough to establish target success. The GUI remains a separate workstream.

## Repository capabilities
- **Provider:** publishes a versioned capability, artifact, package, manifest, or contract.
- **Consumer:** declares a capability and accepted version/range.
- **Build executor:** builds/tests/packs a specific repository and commit.
- **Coordinator:** validates intent and selects permitted operations.
- **Request sender / result receiver:** initiates work and correlates outcomes.
- **Observer:** reads authoritative state/evidence without mutation.
- **Policy authority:** defines allowed targets, operations, constraints, approvals.
- **Presentation adapter:** projects the same model into text or GUI.

These are composable capabilities, not mutually exclusive types guessed from repository names.

## Relationship model
Conceptual fields for the typed contract (not a finalized wire schema): stable relationship ID; canonical source/target repository; relationship type (provides, consumes, requests, notifies, reports-to); application-owned capability identity; contract version; trigger; named authorization requirement (never a secret); correlation ID; and evidence (authoritative URL, repository, run ID, commit SHA, status/conclusion, timestamps).

Inspect existing repository conventions before implementation. Use ProtocolAi for controlled vocabulary. Do not make LLM-generated strings executable or invent another serialization framework without evidence.

## Request/result round trip
1. Caller states intent and target.
2. Host resolves repository and operation, then validates arguments, contract version, permissions, and approval.
3. Sender dispatches using an explicitly configured GitHub mechanism. Do not assume GITHUB_TOKEN authorizes cross-repository writes.
4. Target executes only its own permitted operation; request includes correlation and idempotency IDs.
5. Target returns a typed result, or CiAi observes authoritative workflow evidence. A callback is notification, not proof.
6. CiAi validates sender/target, correlation, run ID, commit SHA, conclusion, and evidence URL.
7. UI reports pending/succeeded/failed/rejected/stale/unknown with evidence. Missing, duplicate, mismatched, or stale responses never become success.

## Invariants
- Read-only and mutating capabilities are separate.
- Model output never grants permission or directly triggers publish/deploy.
- NuGet publishing is opt-in: explicit release intent, passing checks, valid package/version, and owner-configured credentials.
- Never put secrets in manifests, prompts, artifacts, results, logs, or UI.
- Use least-privilege GitHub App permissions or narrowly scoped tokens.
- Dispatch is not a distributed transaction: handle retries, duplicates, late results, cancellation races, and partial failure.
- Preserve evidence; never silently overwrite outcomes.
- Each repository owns its source and CI; coordination does not silently rewrite sibling repos.
- Do not claim exactly-once delivery, recovery, or a completed GUI until tests prove it.

## GUI projection
Show a directed graph. Nodes expose declared capabilities, dependencies/consumers, permissions, latest verified build/release state, and evidence. Edges expose direction, relationship type, capability, contract version, trigger, policy, and latest correlated request/result. Use high-contrast text/icons; never communicate state by color alone. "Ready for consumer" requires verified provider evidence and compatible versions, not an LLM guess.

## Proof sequence
1. Offline repository catalog.
2. Observe a real workflow (existing CiAi capability).
3. Dispatch a harmless request between test repos.
4. Return correlated results and reject duplicate, stale, wrong-repo, wrong-SHA messages.
5. Gate a bounded operation with deterministic vocabulary and host policy.
6. Render the same model in a living GUI.
7. Add package packing; only later add explicitly approved publishing.

Completion requires tests for successful round trip and rejection of unauthorized targets, unknown operations, duplicates, mismatched correlation, wrong repo/run/SHA, invalid evidence URLs, and failed conclusions. Mark unimplemented capabilities planned/experimental.