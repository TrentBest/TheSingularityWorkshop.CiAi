<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiAi semantic stack: ProtocolAi and GrammarAi

## Purpose

CiAi owns the semantics of continuous integration. ProtocolAi supplies stable integer-backed identities for those semantics; GrammarAi supplies generic grammar and structure representations. CiAi composes the two into a CI-specific vocabulary and an initial workflow-recipe grammar.

The dependency direction is intentional:

- **ProtocolAi** defines vocabulary identities and immutable protocol definitions. It does not own CI policy, provider communication, command execution, or authorization.
- **GrammarAi** represents generic productions and protocol-qualified terminals. Its host-supplied resolver checks whether referenced protocol and symbol identities exist. It does not load ProtocolAi or interpret CI meaning.
- **CiAi** owns the CI vocabulary, grammar, workflow-domain catalog, plan validation, evidence semantics, and CI-specific constraints.
- **CommandAi** represents bounded command intent. A valid CI grammar or command proposal does not prove permission or execution.
- **Foundry/host** owns live-state validation, approval, authorization, replay/delivery, dispatch, and execution control.
- **Provider adapters/runners** remain authoritative for provider-side execution and observed run facts.

## Protocol vocabulary

`CiWorkflowProtocolCatalog` creates the CiAi-owned `ProtocolDefinition`. Stable symbol IDs identify workflow concepts and values including triggers, step conditions, actions, evidence outcomes, and the 22 workflow domains in `CiWorkflowDomainCatalog`.

Known vocabulary values encode as integer references. Unrecognized application data, such as a repository identity, remains a literal; it is not silently promoted into a new stable symbol. Symbol IDs are owned by CiAi and must not be reassigned to a different meaning.

The vocabulary is descriptive. The identity `publish` does not grant publication permission; `succeeded` does not establish that a provider run succeeded; and an `approval-requirement` identity is not an approval receipt.

## Workflow grammar

`CiWorkflowGrammarCatalog` builds a GrammarAi `GrammarDefinition` over protocol-qualified terminals. The grammar represents the initial recipe token structure: workflow identity and trigger, a sequence of steps, conditions, workflow domains, dependencies, evidence declarations, and action kinds.

GrammarAi does not parse arbitrary text. The grammar is a structural contract over known vocabulary terminals; dynamic values and CI-specific rules remain CiAi-owned. The host supplies the resolver that calls `CiWorkflowProtocolCatalog.Resolves` against the exact active protocol definition. A missing protocol or symbol identity is reported as unresolved; successful identity resolution does not prove semantic compatibility.

## Domain validation remains CiAi-owned

The existing `CiWorkflowPlanValidator` remains responsible for concrete plan values and graph semantics, including stable plan/step IDs, known workflow domains, valid conditions, dependency references, duplicate/self-dependencies, and cycles. It emits no executable order for an invalid plan.

This first semantic-stack slice does **not yet** translate a GrammarAi grammar instance into a `CiWorkflowPlanDraft`, validate a complete serialized CI recipe, construct CI-specific CommandAi proposals, or execute provider operations. Those are subsequent vertical-integration steps. Existing GitHub Actions workflows remain authoritative.

## Source revisions

CiAi pins the ProtocolAi and GrammarAi development source revisions as Git submodules so development and CI consume explicit commits rather than an unbounded floating dependency. The project and package-consumer smoke test reference both dependencies. CI packs the three source dependencies into an isolated local feed before restoring the external CiAi package consumer.

## Trust boundary

Protocol identity resolution, grammar construction, and plan validation are not authorization. A publish/deploy proposal remains untrusted intent. The host must revalidate the exact repository and source revision, apply owner policy, obtain any required candidate-bound approval, and record the actual provider outcome. Missing, unknown, skipped, cancelled, blocked, failed, and successful evidence remain distinct.

---

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
