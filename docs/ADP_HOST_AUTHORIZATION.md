# Host-Authorized Exact-SHA Workflow Dispatch

## Purpose

A durable rerun request is an immutable statement of intent. It is **not** permission to dispatch a workflow. This boundary separates the request ledger, human/host authorization, live GitHub state, and the eventual dispatch operation.

The versioned contracts are `ciai.workflow-dispatch-authorization.v1` and `ciai.workflow-dispatch-observation.v1`. The contract in `CiWorkflowDispatchAuthorizationEvaluator.cs` is deliberately pure. It returns a structured allow/deny decision and performs no network calls or side effects.

## Security model

- **Request**: immutable request ID, canonical digest, repository, workflow path, trusted workflow-definition ref/SHA, candidate ref/SHA, source run ID/attempt, attempt budget, and expiry.
- **Authenticated authority**: identity established by the trusted host's authentication layer. A string inside an authorization payload is not authentication.
- **Authorization**: short-lived host-issued approval bound to the exact request digest and all dispatch-relevant fields.
- **Host observation**: fresh GitHub API evidence for the workflow definition, candidate ref head, source run identity, and `workflow_dispatch` capability.
- **Replay ledger**: durable host-owned store that atomically reserves/consumes each authorization ID before dispatch.
- **Dispatcher**: trusted host adapter that performs the one permitted GitHub API action after every check succeeds.

No PAT, GitHub App private key, installation token, or other credential belongs in a request, authorization, observation, evidence packet, or log.

## Exact-ref rule

The workflow definition is pinned to `refs/heads/development`. The candidate branch is a separate, non-protected ref and must resolve to the exact candidate SHA in the authorization.

The host must dispatch the **trusted workflow definition on development**, passing the candidate ref and SHA as explicit inputs. The trusted workflow then checks out the candidate SHA for inspection/repair. It must not execute an untrusted candidate branch's workflow definition merely because that branch is the target of the repair.

This separation is essential:

- `WorkflowDefinitionSha` identifies the workflow code that is authorized to run.
- `CandidateSha` identifies the repository content the workflow is allowed to inspect or modify.
- A moved branch head, changed workflow definition, changed request digest, or changed source run invalidates the authorization.

## Evaluation sequence

1. The host loads the immutable rerun request and verifies its canonical digest.
2. A human or explicitly trusted authority approves the exact request and additional-attempt number.
3. The host authenticates that authority independently of the authorization payload.
4. Immediately before dispatch, the host queries GitHub for the workflow path/ref/SHA, confirms `workflow_dispatch`, resolves the candidate ref to its current SHA, and records the source run ID/attempt.
5. The host builds a `CiWorkflowDispatchHostObservation` and computes its canonical SHA-256 digest.
6. The host creates a short-lived `CiWorkflowDispatchAuthorization` bound to the request ID/digest and observation digest.
7. `CiWorkflowDispatchAuthorizationEvaluator.Evaluate(...)` checks identity, exact refs/SHAs, request binding, authority allow-list, observation freshness, authorization lifetime, expiry, attempt budget, and replay state.
8. If allowed, the host atomically reserves the authorization ID in its durable replay ledger. A stateless evaluator cannot prevent two concurrent hosts from using the same authorization.
9. Only after successful atomic reservation does the host dispatch the trusted workflow on `development`, passing the exact candidate ref/SHA and source run identity as inputs.
10. The host records the dispatch response and resulting run ID as evidence. A failed build still does not authorize another repair attempt.

## Fail-closed limits

The evaluator denies when any required identity is absent or mismatched, when the observation is older than 60 seconds or future-dated, when authorization is expired or longer than 15 minutes, when it outlives the request, when the attempt exceeds the request budget or the global maximum of three, or when the authorization ID has already been consumed.

The caller must authenticate the GitHub API response and the authority identity before invoking the evaluator. The evaluator does not independently authenticate network responses, verify signatures, reserve replay IDs, or dispatch workflows.

## Current implementation boundary

The evaluator accepts an adapter-neutral `CiWorkflowDispatchAuthorizationSubject`. Once the durable request contract is integrated, the host adapter should map its immutable request fields into this subject without recalculating or weakening any binding.

The next implementation slice is the host-owned durable replay ledger and a dry-run dispatcher adapter. The first live pilot remains opt-in and tied to the exact-SHA raWWar failure fixture. No merge, package publish, release, deployment, or branch deletion is authorized by this contract.
