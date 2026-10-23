# CiAi inter-repository model

**Status: experimental design contract; not a claim of completed dispatch or GUI behavior.**

A repository is a node in a typed, directed network. Each declares what it provides, consumes, requests, or receives notification about. GitHub remains authoritative for repository identity, commit SHA, workflow state, permissions, and run evidence. A relationship is directional; "connected" alone is insufficient.

## Capability roles (a repository may have several)

| Role | Responsibility |
|---|---|
| Provider | Publishes a versioned capability, artifact, package, manifest, or contract |
| Consumer | Declares a provider capability and accepted version/range |
| Build executor | Builds/tests/packs a specific repository and commit |
| Coordinator | Validates intent and selects a permitted operation |
| Request sender | Initiates permitted work in another repository |
| Result receiver | Accepts and correlates an outcome |
| Observer | Reads authoritative state and evidence without mutation |
| Policy authority | Defines allowed targets, operations, constraints, approvals |
| Presentation adapter | Projects the same typed model into text or GUI |

These are capabilities, not mutually exclusive classifications guessed from repository names.

## Relationship contract (conceptual fields, not a finalized wire schema)

- Stable relationship ID; canonical source and target repository identities.
- Type: provides, consumes, requests, notifies, or reports-to.
- Application-owned capability identity and contract version.
- Trigger, named authorization requirement (never a secret), correlation ID.
- Evidence: authoritative URL, repository, run ID, commit SHA, status/conclusion, timestamps.

Implement typed records after inspecting existing repository conventions. Use ProtocolAi for controlled vocabulary. Do not make LLM-generated strings executable or introduce a competing serialization framework without evidence.

## Round trip

1. Caller states intent and target.
2. Host resolves repository and operation, then validates arguments, contract version, permissions, and approval.
3. Sender dispatches through an explicitly configured GitHub mechanism. Same-repository GITHUB_TOKEN is not assumed to authorize cross-repository writes.
4. Target executes only its own permitted operation; request includes correlation and idempotency IDs.
5. Target reports a typed result, or CiAi observes the authoritative workflow result. A callback is a notification, not proof.
6. CiAi validates sender/target, correlation, run ID, commit SHA, conclusion, and evidence URL.
7. UI reports pending/succeeded/failed/rejected/stale/unknown with evidence. Missing, duplicate, mismatched, or stale responses never become success.

## Invariants

- Read-only and mutating capabilities are separate.
- Model output never grants permission or directly triggers publish/deploy.
- NuGet publication is opt-in: explicit release intent, passing checks, valid version/package, and owner-configured credentials.
- Never place secrets in manifests, prompts, artifacts, results, logs, or UI state.
- Use least-privilege GitHub App permissions or narrowly scoped tokens.
- Dispatch is not a distributed transaction: handle retries, duplicates, late results, cancellation races, and partial failure.
- Preserve evidence/history; never silently overwrite a prior outcome.
- Each repository owns its source and CI; coordination does not silently rewrite sibling repositories.
- Never claim exactly-once delivery, recovery, or a completed GUI until tests prove it.

## GUI projection

Show a directed graph. A node exposes declared capabilities, dependencies/consumers, permissions, latest verified build/release state, and evidence. An edge exposes direction, relationship type, capability, contract version, trigger, policy, and latest correlated request/result. Use accessible high-contrast text/icons; do not communicate state by color alone. "Ready for consumer" requires verified provider evidence and compatible declared versions, not an LLM guess.

## Proof sequence and completion criteria

1. Offline catalog of repositories and relationships.
2. Observe a real workflow run (existing CiAi capability).
3. Dispatch a harmless request between test repositories.
4. Return a correlated result and reject duplicate, stale, wrong-repo, and wrong-SHA responses.
5. Gate a bounded operation using deterministic vocabulary and host-side policy.
6. Render the same model in a living GUI.
7. Add package packing; only later add explicitly approved publishing.

Completion requires tests for successful round trip and rejection of unauthorized targets, unknown operations, duplicates, mismatched correlation, wrong repository/run/SHA, invalid evidence URLs, and failed conclusions. Until then, mark capabilities planned/experimental.
