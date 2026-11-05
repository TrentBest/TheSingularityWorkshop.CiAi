# Await and Gate Contracts

**Status:** Implemented contract and local persistence slice. Host authentication, cross-repository transport, retry scheduling, and LLM resumption remain host responsibilities.

## Purpose

An **Await** lets a consumer repository defer only the work that depends on an unfinished capability. The repository and its LLM can continue independent work while the declared workstream waits.

A **Gate** is a versioned acceptance contract for one provider capability. The provider's evidence is evaluated against the exact approved contract, provider repository, source revision, manifest digest, and evaluation-policy version.

The dock/ship example maps to these terms:

- Consumer: `TrentBest/The`
- Provider: the separately identified repository delivering the capability
- Gate: for example, `B`
- Requirement: validated ship weight and dimensions, including units
- Evidence: exact-source workflow or artifact evidence, or an authorized human adjudication
- Event: a durable notice that this particular Await's Gate B is satisfied or invalidated

## Implemented contracts

| Contract | Responsibility |
|---|---|
| `AwaitGateApproval` | Records the approval reference, authority policy, approver identity, approval kind, and timestamp for a binding contract |
| `AwaitGateContract` | Immutable gate identity, version, consumer/provider, exact provider SHA, manifest digest, evaluation-policy version, requirements, approval, and computed canonical contract digest |
| `AwaitGateEvidence` | Requirement result attributed to the exact gate version, contract digest, provider source, and manifest |
| `AwaitGateEvaluation` | Evaluator-issued immutable result with per-requirement reason codes |
| `AwaitRegistration` | Consumer workstream and the complete set of exact Gate references it depends on |
| `AwaitGateEvent` | Immutable satisfied/invalidated event correlated to one Await and one exact Gate evaluation |
| `AwaitGateDeliveryReceipt` | Host receipt correlated to the event ID, payload digest, and consumer repository |

The canonical contract digest is computed from the versioned gate identity, consumer/provider identities, provider source SHA, manifest digest, evaluation-policy version, approval metadata, and normalized requirement definitions. An Await reference includes that digest; a later contract cannot silently replace the version a workstream approved.

## Evaluation rules

- Missing evidence is **Pending**, never success.
- Duplicate evidence for one requirement is **Indeterminate**.
- Repository, source SHA, manifest digest, gate version, contract digest, or evidence-kind mismatches are **Indeterminate**.
- A value that does not satisfy the approved expected value is **Rejected**.
- Every requirement must have exactly one matching item of evidence for the gate to be **Satisfied**.
- Machine evidence must identify a workflow run and positive attempt.
- Human adjudication must identify an authority policy and adjudicator.
- There is no LLM evidence or approval kind in the contract vocabulary. LLMs may propose requirements, but cannot supply an accepted adjudication.
- An Await is ready only when every exact Gate reference is satisfied. Partial satisfaction leaves the dependent workstream blocked; unrelated work is unaffected.

The library validates the structure and correlation of evidence. It does **not** authenticate a workflow artifact, verify signatures, determine whether a human is authorized, or validate the approval reference against protected control-plane policy. A trusted host must perform those checks before constructing the corresponding evidence and approved contract objects.

## Local durable stores and event delivery

`FileAwaitGateContractStore` persists immutable provider/gate/version contracts. Reusing the same identity with a different contract digest returns a conflict.

`FileAwaitRegistrationStore` persists Await declarations. An Await ID cannot be silently retargeted to another consumer, workstream, or prerequisite set.

`FileAwaitGateEventOutbox` persists immutable events, supports stable enumeration and reload, and treats an identical event ID/payload as an idempotent retry. Reusing an event ID for a different payload is a conflict.

`FileAwaitGateEventReceiptLedger` suppresses replay only after an accepted or duplicate receipt matches the exact event ID, payload digest, and consumer repository. Rejected or mismatched receipts do not suppress replay.

These file-backed stores are local persistence implementations, not a distributed database or delivery service. Protect their directories against unauthorized modification. The host owns scheduling, authentication, network delivery, retry policy, and invocation/resumption of the consumer LLM. CiAi does not poll providers, sleep, execute repository work, or send notifications.

## Example

The following is a contract/evaluation outline; values such as the approval reference, artifact digest, run identity, and evidence digest must come from the trusted host's validated sources.

```csharp
var approval = new AwaitGateApproval(
    approvalReference: "approval-record-123",
    authorityPolicyId: "cross-repo-gate-approval-v1",
    approverIdentity: "authorized-owner",
    kind: AwaitGateApprovalKind.Human,
    approvedAt: DateTimeOffset.UtcNow);

var gate = new AwaitGateContract(
    gateId: "B",
    version: 1,
    consumerRepository: "TrentBest/The",
    providerRepository: "TrentBest/MyVR",
    providerSourceSha: providerCommitSha,
    manifestDigest: providerManifestSha256,
    evaluationPolicyVersion: "ship-dimensions-v1",
    approval: approval,
    requirements: new[]
    {
        new AwaitGateRequirement(
            "dimensions",
            "Weight and dimensions are present, use declared units, and pass validation",
            "valid",
            AwaitGateEvidenceKind.Machine)
    });

var gateStore = new FileAwaitGateContractStore("state/gates");
gateStore.Save(gate);

var registration = new AwaitRegistration(
    "the-dock-await-001",
    "TrentBest/The",
    "ship-integration",
    new[]
    {
        new AwaitGateReference(gate.GateId, gate.Version,
            gate.ContractDigest, gate.ProviderRepository)
    });

new FileAwaitRegistrationStore("state/awaits").Save(registration);

// The host validates artifact provenance and approval policy before creating evidence.
var evidence = new AwaitGateEvidence(
    "dimensions",
    gate.GateId,
    gate.Version,
    gate.ContractDigest,
    gate.ProviderRepository,
    gate.ProviderSourceSha,
    gate.ManifestDigest,
    artifactEvidenceSha256,
    AwaitGateEvidenceKind.Machine,
    "valid",
    "artifact:provider-run-123",
    workflowRunId: "123",
    workflowAttempt: 1);

var evaluation = AwaitGateEvaluator.Evaluate(gate, new[] { evidence });
if (evaluation.IsSatisfied)
{
    var notification = new AwaitGateEvent(
        "event-the-dock-await-001-gate-b-v1",
        registration,
        AwaitGateEventKind.Satisfied,
        evaluation,
        DateTimeOffset.UtcNow);

    new FileAwaitGateEventOutbox("state/await-events").Enqueue(notification);
}
```

The host reads durable events, delivers them at least once, and records an `AwaitGateDeliveryReceipt` only after authenticating the receiver's acknowledgement. The receipt ledger then allows the host to avoid repeating an already acknowledged logical delivery.

An invalidation follows the same pattern with `AwaitGateEventKind.Invalidated` and an evaluator result of Rejected, Indeterminate, or Revoked. Use `AwaitGateEvaluator.Revoke` after the host authorizes revocation. It does not erase the historical satisfied event; it adds a new event bound to the changed evaluation.

## Deliberate boundaries and remaining integration

This slice provides versioned contracts, deterministic fail-closed evaluation, immutable local stores, event correlation, and receipt persistence. It does not yet provide:

- A trusted host adapter that authenticates provider artifacts and human approval records
- Cross-repository registration/discovery or Foundry transport
- Host-owned retry scheduling, notification delivery, or consumer LLM invocation
- A complete multi-event transition coordinator that automatically derives satisfied/invalidated events from provider updates
- A real cross-repository consumer integration test

Do not claim the end-to-end Await workflow is live until authenticated delivery and a real consumer integration test pass. A satisfied Gate is evidence that a declared prerequisite is met; it does not authorize merge, release, deployment, or NuGet publication.

---

![The Singularity Workshop](https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png)
