<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiWorkflowDispatchInputContract

**User moniker:** Exact Candidate, Correlated Run

## Purpose

`CiWorkflowDispatchInputContract` maps one validated `CiWorkflowRerunRequest` to the optional `workflow_dispatch` inputs accepted by CiAi's contract workflow. It creates an immutable input dictionary; it does not authenticate a caller, resolve GitHub state, hold credentials, or send a dispatch request.

## Input mapping

| Workflow input | Source |
|---|---|
| `target_branch` | `HeadRef` without the `refs/heads/` prefix |
| `ciai_request_id` | `RequestId` |
| `ciai_correlation_id` | `CorrelationId` |
| `ciai_task_id` | `TaskId` |
| `ciai_candidate_sha` | normalized full `HeadSha` |
| `ciai_attempt_number` | invariant-culture decimal `AttemptNumber` |

All six values are emitted together. The request, correlation, and task IDs must also match the workflow's bounded ASCII identity grammar; the mapper rejects values that the shell-side guard would reject instead of producing a dispatch that can only fail after it starts. The result is read-only so a caller cannot silently change the validated mapping after creation. Credentials, scopes, tokens, and authorization claims are never workflow inputs.

## Workflow-side exact-SHA guard

CiAi's `.github/workflows/ci.yml` treats the five `ciai_*` fields as an all-or-none opt-in group. It requires a valid `feature/*` branch and requires the dispatch event's ref and SHA to match the request. It resolves the branch again from the remote and fails before build/test if the branch has moved; it then checks out the resolved immutable SHA and includes request/correlation/task/attempt provenance in the run summary.

This guards the branch-ref nature of GitHub workflow dispatch: the API dispatches a branch, not an arbitrary commit SHA. The run's actual repository, workflow path, run ID, attempt, and head SHA still require read-back and evidence reconciliation by the host.

## Security boundary

- The trusted host authenticates the rerun request and evaluates `CiWorkflowRerunRequestPolicy` before mapping it.
- The host obtains a repository-scoped GitHub credential from its secret/workload-identity provider; the token is not represented here.
- The host dispatches only an allowlisted workflow and isolated feature ref.
- A workflow run is not accepted as evidence merely because dispatch returned success; the host must reconcile exact run provenance.
- A network timeout after dispatch is ambiguous and must be reconciled before retry. This mapper does not implement dispatch scheduling or durable dispatch-attempt state.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
