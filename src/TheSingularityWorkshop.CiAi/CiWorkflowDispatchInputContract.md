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

GitHub only accepts `workflow_dispatch` events when the workflow file exists on the repository's default branch ([GitHub documentation](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)). CiAi's default branch is `master`; this draft does not change it. Therefore this input contract is prepared and validated by push/PR CI, but remote dispatch is not available until the user merges the workflow registration to the default branch. No merge is performed by this work.

CiAi's `.github/workflows/ci.yml` treats the five `ciai_*` fields as an all-or-none opt-in group. The host must use `CiWorkflowDispatchInputContract.TrustedWorkflowRef` (`refs/heads/development`) as the GitHub dispatch API's `ref`; it must never dispatch the candidate feature branch as the workflow-definition ref. The workflow records the trusted workflow-definition ref/SHA separately from the candidate ref/SHA. It validates the `feature/*` target branch, resolves that branch again from the remote, requires the resolved head to equal the requested candidate SHA, and fails before build/test if the branch moved. Only after that check does it check out the immutable candidate SHA and include both workflow-definition and candidate provenance in the run summary.

This guards the branch-ref nature of GitHub workflow dispatch: the API dispatches a branch, not an arbitrary commit SHA. The run's actual repository, workflow path, run ID, attempt, and head SHA still require read-back and evidence reconciliation by the host. The workflow concurrency group includes the request ID when present, so distinct task requests on the same feature branch do not cancel one another; duplicate dispatches for the same request remain in one group.

## Security boundary

- The trusted host authenticates the rerun request and evaluates `CiWorkflowRerunRequestPolicy` before mapping it.
- The host obtains a repository-scoped GitHub credential from its secret/workload-identity provider; the token is not represented here.
- The host dispatches only an allowlisted workflow from `TrustedWorkflowRef`, passes the isolated feature branch as `target_branch`, and supplies the exact candidate SHA separately.
- A workflow run is not accepted as evidence merely because dispatch returned success; the host must reconcile exact run provenance.
- A network timeout after dispatch is ambiguous and must be reconciled before retry. This mapper does not implement dispatch scheduling or durable dispatch-attempt state.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
