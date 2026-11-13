# TheSingularityWorkshop.GrammarAi: CiAi Integration

## Intended role

GrammarAi is the reusable AI-facing knowledge-mapping layer for describing and validating typed CI workflow structures. It is not primarily a compiler, parser, lexer, or parser-edge-case test service.

CiAi should use GrammarAi to define its own versioned CI domain map: workflow plans, triggers, conditions/dependencies, typed steps/actions, inputs/outputs, declared capabilities, and expected evidence/outcome roles. GrammarAi supplies the structural model and bounded proposal validation; CiAi supplies the CI-specific vocabulary and meaning.

**Integration status:** implemented as the first structural vertical slice on CiAi `development`. CiAi pins GrammarAi source revision `2582362ad233faeffe82c2de28d464efffb0cda4` as `external/GrammarAi`, references its project directly, and exercises the public graph and proposal codec in contract tests. The slice models a versioned workflow node, repeated ordered step membership, and optional step dependencies. CiAi retains domain/action meaning in its existing catalog and semantic plan validator; this is not an execution or release authorization.

## Ownership boundary

- **ProtocolAi:** stable vocabulary identities. Global category ownership remains a separate cross-repository decision; do not invent category metadata in CiAi.
- **GrammarAi:** reusable production/structural grammar definitions, typed node and slot identities, schema versioning, graph validation, and compact integer-first proposal codec.
- **CiAi:** CI-specific action vocabulary and parameter identities, workflow-domain semantics, evidence/policy evaluation, and CI-provider adapters.
- **CommandAi:** generic atomic-command proposal representation and serialization.
- **Foundry/host:** live-state checks, runtime stepping, authorization, approval, dispatch, execution, retry/replay lifecycle.
- **CI provider:** authoritative workflow, run, job, step, and artifact state.

A valid GrammarAi structure is untrusted intent. Structural validity does not establish semantic correctness, successful CI evidence, authorization, or permission to execute, publish, or deploy.

## Implemented first integration slice

CiAi currently pins GrammarAi source revision `2582362ad233faeffe82c2de28d464efffb0cda4`. The implemented first slice is deliberately small:

- one workflow node and repeated ordered step nodes;
- optional ordered dependency relationships between steps;
- a populated integer-first proposal encoded and decoded by `GrammarStructureProposalCodec`;
- required workflow membership and optional dependency cardinality.

Typed input/output payloads, explicit triggers, capabilities, and outcome/evidence roles are not yet part of this first graph slice. CiAi continues to carry step domain/action meaning in its existing `CiWorkflowDomainCatalog` and validates dependency semantics with `CiWorkflowPlanValidator` before mapping. The graph codec is not a second semantic policy engine.

Use `GrammarStructureDefinition`, `GrammarNodeDefinition`, `GrammarSlotDefinition`, `GrammarStructureInstance`, and `GrammarStructureProposalCodec` where they fit. Evaluate the production-grammar API for any part that genuinely needs ordered symbol composition. Do not implement a second grammar or structural validator in CiAi, and do not attempt to model every CI workflow family in the first increment.

The active schema identity/version is `CiWorkflowGrammarMap.DefinitionId` / `SchemaVersion`; the GrammarAi proposal codec wire version remains a separate field. This first slice uses only GrammarAi node targets and therefore does not fabricate ProtocolAi category identities or bypass the host-owned external-reference resolver.

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
