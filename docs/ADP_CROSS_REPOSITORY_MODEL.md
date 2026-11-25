# Autonomous Development Pipeline: cross-repository graph

**Status:** First typed relationship and causal-event contracts implemented on `development`. Durable graph enrollment, event transport, execution adapters, and end-to-end feedback proof remain in progress.

## ADP is a development system, not a build chain

ADP means **Autonomous Development Pipeline**. It coordinates the development of a system whose parts may create, consume, validate, and deliver artifacts across repository boundaries. A NuGet dependency graph is only one possible input to that system; it is not the ADP graph.

A repository may occupy multiple roles at once. raWWar is the concrete acceptance case:

- raWWar produces proposed roleplaying content.
- SURPS admits, canonicalizes, versions, and owns the semantics of canonical roleplaying content.
- raWWar consumes the accepted SURPS content.
- raWWar owns and validates its Experience, including the exact content and Workshop capabilities used to construct it.

These roles must be represented by separate, directed, versioned relationships. Do not collapse them into one undirected dependency or infer content authority from build order.

## The three required edges

| Relationship | Direction | Authority | Consequence |
|---|---|---|---|
| Content contribution | raWWar → SURPS | SURPS | Admit, reject, or request revision of a proposed contribution |
| Canonical content consumption | SURPS → raWWar | SURPS for canonical content | Revalidate raWWar's content compatibility and affected Experience |
| Experience validation | SURPS → raWWar | raWWar for its Experience | Build and validate an Experience candidate against exact SURPS content |

The Experience validation edge is not a claim that SURPS owns the Experience. Its source identifies the exact upstream content revision; raWWar remains the Experience authority and delivery owner.

## Feedback without an infinite loop

A legitimate feedback cycle is not a cyclic execution dependency. It is a sequence of immutable, causally correlated events:

1. A raWWar source revision emits a content-contribution notice.
2. SURPS validates the proposal against its own schema, ontology, provenance, and admission policy.
3. If admitted, SURPS emits a canonical-content revision.
4. That exact SURPS revision triggers raWWar compatibility and Experience validation.
5. If raWWar work produces another proposed content contribution, it is a new source revision and a new event—not a replay of the previous event.

The same delivery retried must not create duplicate work. The current `CiRepositoryChangeNotice.IdempotencyKey` is derived from the relationship identity/version, directed endpoints, exact source commit, artifact identity, and artifact SHA-256. The transport event ID is deliberately excluded so a retry with a new envelope ID still deduplicates. A changed commit or changed artifact digest creates new work.

A `CausationId` can connect downstream events to the originating event for traceability. It is not itself a deduplication key and must not be used to suppress a genuinely changed artifact. Hosts should also coalesce queued work by relationship and exact artifact identity, cancel/supersede obsolete candidate work only under explicit policy, and retain every terminal outcome.

## Exact identity and evidence

Every hand-off must preserve:

- relationship ID and contract version;
- source and target repository identities;
- authority repository for the artifact's meaning/admission decision;
- full source commit SHA;
- artifact identity and SHA-256 of the artifact bytes;
- causal correlation ID when the work is downstream of another event;
- exact workflow run and attempt, validation policy version, and resulting evidence when validation executes.

A branch name is a mutable locator, not provenance. A package version alone is not enough to identify content. An Experience candidate must be traceable to the exact canonical content revision and the exact dependency/artifact closure used to assemble it.

## Ownership and authority

- SURPS owns canonical roleplaying ontology, schemas, identifiers, admission, and canonical content versions.
- raWWar owns its Experience, its world and game semantics, Experience composition, and its acceptance criteria.
- CiAi owns ADP orchestration contracts, relationship graph validation, task grouping, causal event identity, dependency scheduling, CI evidence correlation, policy evaluation, and bounded LLM work packets.
- FSM_Execution remains the durable task execution mechanism; ADP must not invent a second scheduler.
- The trusted host/FSM_Foundry independently authenticates and authorizes each proposed repository operation within its exact task scope. Human approval remains at that host boundary.
- GitHub Actions and the owning repository's CI remain authoritative for build/test execution and provider evidence.

An LLM may propose content or code and explain a failure. It cannot declare content canonical, authenticate CI evidence, authorize a write, merge, publish, release, or deploy.

## Initial implementation boundary

CiAi now has typed declarations for directed repository relationships and immutable change notices. Contract tests cover the raWWar → SURPS contribution edge, the SURPS → raWWar canonical-consumption edge, raWWar Experience ownership, exact commit/artifact provenance, and retry deduplication versus changed-input revalidation.

This is intentionally a contract foundation, not a claim that the entire ADP is running. The next slices are:

1. Validate and enroll a versioned relationship graph as part of the existing CiAi repository composition/manifest—not a parallel manifest system.
2. Persist change notices and delivery receipts using the existing outbox/receipt patterns.
3. Map relationship events into bounded ADP work groups and existing Await/Gate declarations.
4. Connect those work groups to FSM_Execution and the host authorization boundary.
5. Prove the complete raWWar ↔ SURPS ↔ raWWar loop using exact source revisions, artifact digests, CI evidence, duplicate delivery, changed content, and failed admission cases.

No package publication, release, deployment, or merge authority is created by these contracts.
