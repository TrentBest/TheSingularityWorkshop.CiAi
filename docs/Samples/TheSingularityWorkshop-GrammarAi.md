# TheSingularityWorkshop.GrammarAi: CiAi Integration

## Intended role

GrammarAi is the reusable AI-facing knowledge-mapping layer for describing and validating typed CI workflow structures. It is not primarily a compiler, parser, lexer, or parser-edge-case test service.

CiAi should use GrammarAi to define its own versioned CI domain map: workflow plans, triggers, conditions/dependencies, typed steps/actions, inputs/outputs, declared capabilities, and expected evidence/outcome roles. GrammarAi supplies the structural model and bounded proposal validation; CiAi supplies the CI-specific vocabulary and meaning.

**Integration status:** planned and tracked by [CiAi issue #59](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/59). The current CiAi development project does not yet reference GrammarAi. This page describes the intended consumer contract, not a completed integration.

## Ownership boundary

- **ProtocolAi:** stable vocabulary identities. Global category ownership remains a separate cross-repository decision; do not invent category metadata in CiAi.
- **GrammarAi:** reusable production/structural grammar definitions, typed node and slot identities, schema versioning, graph validation, and compact integer-first proposal codec.
- **CiAi:** CI-specific action vocabulary and parameter identities, workflow-domain semantics, evidence/policy evaluation, and CI-provider adapters.
- **CommandAi:** generic atomic-command proposal representation and serialization.
- **Foundry/host:** live-state checks, runtime stepping, authorization, approval, dispatch, execution, retry/replay lifecycle.
- **CI provider:** authoritative workflow, run, job, step, and artifact state.

A valid GrammarAi structure is untrusted intent. Structural validity does not establish semantic correctness, successful CI evidence, authorization, or permission to execute, publish, or deploy.

## First integration slice

Use GrammarAi's public API from an exact, immutable source revision. The initial CI map should remain deliberately small and cover a real workflow proposal, for example:

- one workflow/plan node;
- an ordered sequence of typed restore/build/test steps;
- explicit dependency/condition relationships;
- typed input/output or evidence roles;
- required and optional relationships where appropriate.

Use `GrammarStructureDefinition`, `GrammarNodeDefinition`, `GrammarSlotDefinition`, `GrammarStructureInstance`, and `GrammarStructureProposalCodec` where they fit. Evaluate the production-grammar API for any part that genuinely needs ordered symbol composition. Do not implement a second grammar or structural validator in CiAi, and do not attempt to model every CI workflow family in the first increment.

The host selects the active versioned definition and supplies any vocabulary/identifier resolution. Keep structure-definition identity/version separate from the codec wire version.

## Evidence contract

The CiAi integration should retain the existing provider evidence contract:
- canonical repository identity, full source commit SHA, workflow path, trigger/event, run ID and attempt;
- provider status/conclusion, timestamps, job/step outcomes, and authoritative evidence URLs;
- exact resolved source revision of each sibling dependency, including the GrammarAi pin;
- distinct missing, not-triggered, skipped, cancelled, unavailable, unknown, failed, and successful states.

GrammarAi validates the proposed structure. CiAi evaluates the meaning of the declared CI policy against provider evidence. Foundry/host owns execution authorization and lifecycle.

## Adoption and verification

1. Pin an immutable GrammarAi source commit in CiAi's source dependency model; do not build against a moving branch ref.
2. Build CiAi against that exact source revision without relying on a NuGet publication.
3. Exercise a deterministic versioned CI workflow definition and populated proposal in contract tests.
4. Cover valid and invalid relationships, ordering/cardinality, stale schema and unknown wire versions, malformed/bounded payloads, and deterministic round trips as applicable.
5. Verify clean-checkout builds, package creation, and package-consumer smoke tests include the dependency correctly.
6. Keep the integration read-only/advisory unless a separate explicit host authorization contract says otherwise.

No GrammarAi integration is complete merely because CiAi references the project; tests must prove that CiAi actually invokes the GrammarAi model and codec.

Related work:
- [CiAi issue #59 — Integrate GrammarAi as CiAi's typed CI workflow knowledge-mapping layer](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/59)
- [CiAi issue #52 — Migrate AiCi repository composer to enriched CommandAi/1 atomic-command fields](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/52)
- [GrammarAi knowledge-mapping model](https://github.com/TrentBest/TheSingularityWorkshop.GrammarAi/blob/development/docs/KNOWLEDGE_MAPPING_MODEL.md)
