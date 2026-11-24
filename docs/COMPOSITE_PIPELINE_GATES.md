# Composite Cross-Repository Pipeline Gates

## Purpose

A named development pipeline may require capabilities delivered by multiple repositories. A single prerequisite leaf must not release a hold when the declared capability is a complete multi-repository bundle.

CiAi represents this as a bounded Boolean expression over independently verified repository gate evaluations. The expression composes existing gate evidence; it does not replace the dependency graph, candidate store, evidence verifier, or host scheduler.

## Expression semantics

The current expression schema is version 1. Trees are immutable and bounded to depth 8 and 32 total nodes. AND/OR nodes require at least two children. Leaf gate IDs must be unique within an expression. Each definition also exposes a canonical SHA-256 digest over its schema version, pipeline identity, ordered tree structure, and exact leaf identities. Reordered children therefore produce a different definition digest; the authored expression is retained exactly rather than silently normalized.

Example: either repositories B and C must both pass, or D must pass as a complete alternative.

```csharp
var expression = CiAiPipelineGateExpression.ForPipeline(
    "PipelineA",
    new CiAiAnyOf(new CiAiGateExpression[]
    {
        new CiAiAllOf(new CiAiGateExpression[] { gateB, gateC }),
        gateD
    }));
```

Each leaf binds:
- stable gate ID;
- canonical owner/repository;
- candidate ID and full 40-character source commit SHA;
- evidence family (`PublishedPackage` or `DevelopmentCapability`);
- optional exact subject ID/version (package identity or capability identity);
- optional `RequiresProtectedPush` trust requirement for development-capability leaves.

The evaluator consumes normalized `CiAiRepositoryGateEvaluation` records keyed by gate ID. Published-package results are adapted from `DependencyReleaseGateEvaluation`; development-capability results can be created only from a fully verified `CiAiDevelopmentCapabilityEvidenceValidation` produced by the envelope parser plus the TRX/provenance verifier. The evaluator never trusts a caller-supplied boolean or an LLM assertion.

| State | Meaning |
|---|---|
| `Satisfied` | The expression has a complete verified alternative |
| `Waiting` | Required evidence is missing or still awaiting a terminal gate result |
| `Blocked` | A required leaf is rejected/mismatched, or every OR alternative is blocked |

For `AllOf(B,C)`, B alone is never sufficient. For `AnyOf(AllOf(B,C),D)`, D may independently satisfy the complete alternative. A stale candidate SHA, repository mismatch, candidate mismatch, or failed leaf cannot satisfy a gate.

Published-package and development-capability evidence are distinct leaf kinds. A development-capability leaf additionally binds the capability ID/version. A verified PR candidate may satisfy a development-work gate, but a leaf with `RequiresProtectedPush` remains `Waiting` until the exact capability evidence is observed on the protected `development` push; PR evidence cannot satisfy that stronger trust requirement. A development capability never implies NuGet publication.

## LLM allocation

`CiAiPipelineGateAdmissionPolicy` maps the composite result to a fail-closed admission decision:

- `Waiting` → `DormantWaiting`; no LLM allocation is permitted.
- `Blocked` → `DeniedBlocked`; no LLM allocation is permitted.
- `Satisfied` → `Eligible`; normal allocation may proceed.
- Unknown enum values → denied.

This is a policy decision, not a scheduler. The host/coordinator must persist dormancy and avoid prompt dispatch, retries, polling turns, and token allocation while held. The policy itself does not call an LLM.

## Event-driven reevaluation

`DependsOnGate(gateId)` identifies whether a gate event is relevant to this expression. The host can use it to reevaluate only pipelines whose expression contains the changed leaf. It must not poll blocked repositories with LLM turns to discover whether a gate changed.

The current slice provides the deterministic dependency predicate, evaluator, and admission decision. Durable gate-event outbox delivery, persisted dormant work state, and automatic wake-up remain coordinator/host integration work.

## Pipeline membership and cross-pipeline access

Every leaf repository must be registered and belong to the expression's named pipeline. An unregistered repository is rejected.

A repository belonging only to another pipeline is rejected unless an explicit `CiAiCrossPipelineGateGrant` matches all of:
- repository;
- candidate ID and exact source SHA;
- source pipeline and target pipeline;
- policy ID and version;
- approver identity and approval timestamp.

A grant for another SHA, candidate, or target pipeline cannot authorize the leaf. Grant metadata is not self-authenticating: the trusted host must authenticate the approver and validate the policy before constructing the grant. CiAi does not infer human approval from an LLM or repository-supplied text.

## Ownership and limits

- CiAi evaluates the typed expression against existing verified gate results.
- The trusted host authenticates cross-pipeline grants.
- The coordinator owns persistence, event delivery, dormant-state lifecycle, and wake-up.
- The existing LLM allocator owns budgets, fairness, provider limits, and actual dispatch.
- HeadlessAi receives bounded work only after the host has admitted it.
- No gate result authorizes repository writes, merge, NuGet publication, release, or deployment.

This implementation is the evaluator/policy foundation for issue #80. It intentionally does not add a second scheduler, evidence ledger, or authorization authority.
