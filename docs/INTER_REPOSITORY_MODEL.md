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

Latest checked offline CI evidence for the contract slices:
- [CogA catalog grammar regression tests](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogA/actions/runs/40040606499) — passed on `f288fe951aed6de9e609c0042a6360bcd529c61d`.
- [CogB request grammar regression tests](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogB/actions/runs/40040614627) — passed on `762c5f1bec5d5137b9313d071550bb699d50196d`.
- [CogC result contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogC/actions/runs/40037652342) — passed.
- [CogD policy contract](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogD/actions/runs/40037965079) — passed after pinning to the ProtocolAi package version actually available on NuGet.
- [CogE topology projection](https://github.com/TrentBest/TheSingularityWorkshop.CiAi.CogE/actions/runs/40037901319) — passed.

CogA and CogB now share the same canonical numeric version grammar: exactly three ASCII-numeric components, no leading zeroes except the component `0`, major component greater than zero, and no artificial integer-width limit. These runs prove each side's offline regression checks pass; they do not prove runtime integration.

The parent integration remains the critical gap: reconcile CogA's catalog vocabulary with CogD/ProtocolAi; connect CogB request identity to CogC results; verify claims against the authoritative GitHub run API in CiAi; then feed only reconciled evidence into CogE. A URL-shaped claim or a successful transport receipt is never enough to establish target success. The GUI remains a separate workstream.

## Static integration audit — 2026-10-23

Source inspection confirms that the Cogs are individually testable contracts, not yet one enforced runtime contract. The parent integration must resolve these specific seams before describing the compatibility matrix as verified:

- **Version semantics differ by layer.** CogA and CogB now share canonical syntax validation for three-part numeric versions (ASCII digits, no leading zeroes except the component `0`, major greater than zero, and no fixed-width integer ceiling). CogA's catalog separately accepts a declaration when its contract version has the same major version as the capability definition, while CogC requires the result version to exactly equal the coordinator's expected version. Keep catalog compatibility (whether a consumer may accept a provider) separate from wire-contract identity (which must match exactly for one request/result exchange); do not use a major-version match to validate a response.
- **Operation vocabulary has two owners today.** CogD defines `observe.status`, `test.noop`, and `publish.package` through ProtocolAi. CogB accepts a caller-supplied operation allowlist. CiAi must derive or validate that allowlist from the trusted policy/catalog contract, rather than independently configuring two lists that can drift.
- **Evidence validation is not authoritative verification.** CogC checks claimed success fields and the shape of a GitHub run URL, but it does not fetch the run or prove that the run's repository, run ID, head SHA, and conclusion match the request. Only CiAi's provider client can establish those facts; a structurally valid callback remains an unverified claim until reconciled against the provider.
- **Policy is a decision, not an execution boundary by itself.** CogD's prototype denies `publish.package` even with its approval flag. Preserve that denial; the host must independently authorize any future mutating operation. Do not enable publishing as part of the alpha integration.
- **Repository identity rules should be centralized.** CogA and CogB currently contain similar local owner/repository parsers. Add shared contract tests (or one parent-owned canonical validator) so malformed identities and case handling cannot diverge between catalog, request, result, and GitHub observation.

### Required integration proof

The first vertical slice should remain read-only: take a catalog declaration, resolve a permitted `observe.status` request, fetch a real GitHub Actions run through the CiAi provider client, construct a result from the returned snapshot, and validate exact request/result identity. Include negative tests for wrong target repository, request/correlation/idempotency ID, contract version, run ID, SHA, evidence URL, unknown conclusion, and failed run. Do not call this a cross-repository dispatch proof; it is a read-only reconciliation proof. Live dispatch remains a separate, approval-gated phase.

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

## Dependency monitoring and maintenance-release lifecycle

Dependency edges can serve more than build-time compatibility. They can also describe how a consumer tracks an upstream provider and decides whether to adopt a new published artifact. Keep the following decisions separate:

1. **Observe:** discover a candidate version from an approved package feed or publisher source.
2. **Classify:** compare it with the consumer's declared version-selection and trust policy.
3. **Resolve:** identify the exact package ID, version, feed, and available provenance. Do not validate against a floating version that can change during the run.
4. **Validate:** restore the candidate explicitly and run the consumer's required build, tests, compatibility checks, and other declared evidence.
5. **Decide:** apply the configured policy: report only, require adoption approval, or permit adoption within bounded rules.
6. **Prepare:** create a reviewable dependency change and maintenance-release candidate. Preserve the prior version and candidate identity.
7. **Authorize and publish:** treat downstream publication as a distinct privileged action, bound to the exact source commit, package ID/version/digest, destination feed, and plan.
8. **Verify and report:** confirm the published artifact is actually available, then record the upstream trigger and evidence in the downstream release report.

### Dependency policy modes

- **Development source:** use an explicitly configured upstream repository branch/project during coordinated development. This is not evidence that a published package is ready.
- **Pinned release:** require a specific package version for a release candidate.
- **Monitor only:** report new versions without modifying the consumer.
- **Approval-gated adoption:** evaluate and validate a candidate, then pause for an owner decision.
- **Policy-bounded adoption:** permit updates only inside explicit constraints, such as patch-only candidates, and only when every required check passes. Publication still follows its own authorization policy.

A version number or semantic-version range is not proof of compatibility. API compatibility tooling can provide useful evidence but cannot prove all behavioral compatibility. Unknown, missing, stale, or contradictory evidence must pause or block adoption rather than being treated as success.

### Durable state and user-visible status

The coordinator should persist enough state to resume after a runner exits: consumer repository and source SHA; dependency relationship ID; package ID and feed; currently adopted and proposed versions; source/provenance evidence; policy revision; each validation result; approval decision and scope; downstream candidate identity; publication attempt and verified outcome; timestamps and correlation IDs.

Use explicit statuses such as `UpdateDetected`, `AwaitingEvaluation`, `Validating`, `ApprovalRequired`, `Blocked`, `ReadyToAdopt`, `ReleaseCandidateReady`, `Publishing`, `Published`, and `Indeterminate`. In particular, an interrupted publish whose remote outcome is unknown must be reconciled against the feed before retrying; it must not be blindly repeated.

Reports and dashboard projections should distinguish discovery from adoption and adoption from publication. A maintenance release should state that an upstream dependency change triggered it, identify old and new package versions, cite the downstream source commit, and summarize the actual validation and authorization evidence. Never infer these facts from an LLM-generated description or a commit message.

Monitoring adapters should be read-only by default. They can create a notification or candidate request, but must not directly rewrite dependency files or invoke publishers. The existing pipeline engine, policy evaluator, evidence model, and authorization boundary remain the single path through which consequential work is scheduled and verified.

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