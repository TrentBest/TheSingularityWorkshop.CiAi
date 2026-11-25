# Workflow rerun request contract

**Contract:** `ciai.workflow-rerun-request.v1`  
**Status:** Implemented request and local append-only ledger slice; host dispatch integration remains separate.

## Purpose

A rerun request records one bounded intent to revalidate one exact candidate after one exact source workflow run attempt. It gives the host durable correlation and idempotency data without giving CiAi a second workflow executor.

The contract is deliberately not a dispatch command. Persisting a request does not authenticate the requester, approve a repository operation, grant GitHub Actions permissions, or cause a workflow to run.

## Request identity

Each request binds:

- A stable request ID, idempotency key, and host-owned task/correlation ID.
- Canonical owner/repository identity.
- Exact workflow YAML path under `.github/workflows/`.
- Isolated candidate ref under `refs/heads/feature/`.
- Full 40-character candidate commit SHA.
- Exact source workflow run ID and run attempt that prompted the rerun.
- Requested time, expiry, and a bounded maximum of three additional attempts.

The candidate branch name and candidate SHA are both retained. The host must independently verify that the branch resolves to the requested SHA immediately before dispatch; a ref string is not proof of its current target.

The request lifetime is limited to seven days. A request with an expired or malformed identity is rejected at construction time.

## Append-only ledger

`FileCiWorkflowRerunRequestLedger` stores immutable request snapshots:

- First use of an idempotency key appends one record.
- Repeating the exact same request returns `AlreadyExists`.
- Reusing an idempotency key with different content returns `Conflict`.
- Reusing a request ID with another idempotency key returns `Conflict`.
- Every request is retained; a later run attempt is a separate request and never overwrites earlier evidence.
- Each record is rehydrated through the validated contract constructor and its canonical request digest is recomputed. Tampered or unsupported records fail closed.
- Writes are serialized through a process-wide file lock and use a flushed temporary file followed by an atomic rename.

The local ledger is bounded to 100,000 records and 64 KiB per record. Its directory must be writable only by the trusted host identity and must be backed up/retained according to host policy.

## Host authorization and dispatch boundary

The host, not CiAi, must:

1. Authenticate the requesting agent and resolve the task/correlation record.
2. Confirm that the task is active, leased, and within its own retry, time, concurrency, cost, and changed-file budgets.
3. Independently verify the repository, registered workflow path, isolated feature ref, and exact candidate SHA.
4. Confirm the source run ID/attempt belongs to that exact candidate and workflow.
5. Apply host authorization before dispatch and supply only a narrowly scoped short-lived provider credential.
6. Dispatch the registered workflow using the exact candidate ref/SHA, then persist the resulting run ID and attempt in the host's lifecycle record.
7. Observe and validate the new run through CiAi's exact-source evidence path.

The ledger does not contain a boolean `authorized` field. Authorization is not inferred from a request's existence, its idempotency key, a green CI result, or model output. Human approval remains in the host adapter.

CiAi does not call GitHub's dispatch or rerun APIs, does not retry silently, and does not manage task lifecycle. A missing host adapter is an explicit integration blocker.

## Tests

The contract suite covers:

- Exact repository/workflow/ref/SHA binding.
- Rejection of canonical and non-feature refs, traversal paths, and malformed SHAs.
- Positive source run/attempt identity and bounded retry/lifetime values.
- Stable request digests and exact idempotency behavior.
- Request-ID conflicts, persistence/reload, and preservation of later attempts.
- Tamper detection when stored request content no longer matches its digest.

## Implementation

- `CiWorkflowRerunRequest`
- `CiWorkflowDispatchInputContract`
- `FileCiWorkflowRerunRequestLedger`
- `CiWorkflowRerunRequestLedgerTests`

This slice is not the live red → repair → green pipeline. That still requires an authenticated FSM_Foundry host adapter, a real provider dispatch/observer path, the HeadlessAi provider invocation, and a full exact-SHA integration proof.
