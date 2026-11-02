<!-- The Singularity Workshop animated moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# AiCi repository proposal composer

**Status: implemented proposal-composition slice. Host-side validation, authorization, approval, and execution remain separate responsibilities.**

## 1. Theory: intent is not authority

An LLM should describe bounded repository-development intentions in a stable, inspectable format. It should not receive an unrestricted Git client, shell, or ambient repository credentials merely because it produced a syntactically valid answer.

The responsibility chain is:

1. **HeadlessAi** obtains a task and CI evidence, invokes a model, and returns a normalized proposal. Model output remains untrusted.
2. **CommandAi** represents generic atomic commands as a versioned, bounded proposal and provides the shared codec.
3. **AiCi (CiAi)** composes repository-development vocabulary using the actual CommandAi types. It does not define a competing generic command model.
4. **GrammarAi / protocol validation** may validate structure and interoperable envelopes. Structural validity is not authorization.
5. **Foundry / trusted host adapter** independently validates repository identity, task scope, registered capabilities, branch and base SHA, allowed paths, budgets, expiry, and authenticated human approval where required.
6. **GitHub** remains authoritative for refs, commits, pull requests, workflow runs, and CI evidence. CiAi evaluates evidence against the exact candidate SHA.

A proposal is a request for host consideration, not an executable script, capability grant, or approval receipt. Missing or ambiguous identity, precondition, policy, or authorization must cause host rejection.

## 2. Implementation: composing a proposal

The public entry point is `AiCi.ForRepository(repository, featureBranch, baseSha)`.

```csharp
var proposal = AiCi.ForRepository(
        "TrentBest/Minesweeper",
        "feature/repair-board",
        baseCommitSha)
    .CreateProject("Minesweeper")
    .AddScript("Game/MinesweeperGame.cs", source)
    .ModifyScript("Game/Board.cs", expectedFileSha256, patch)
    .AddDependency("FSM_API", "1.0.13")
    .RequestBuild()
    .RequestTests()
    .Build();
```

The result is an immutable **CommandAi/1** `CommandProposal`. The /1 contract has only ordered action identities and ordered typed arguments; it does not define command IDs, targets, scopes, idempotency keys, or outcome contracts. CiAi therefore carries the repository, isolated feature branch, and full base commit SHA as explicit typed arguments on every command. Operation-specific values follow those context arguments.

### Initial command vocabulary

| Action | Meaning | Additional values |
|---|---|---|
| `CreateProject` | Request creation of a project | Project name |
| `AddProjectFile` / `AddScript` | Request addition of a file | Repository-relative path and bounded content |
| `ModifyProjectFile` / `ModifyScript` | Request a guarded file modification | Path, expected current-file SHA-256, bounded patch |
| `AddDependency` | Declare dependency-addition intent | Package ID and version |
| `RequestBuild` | Request host build validation | None |
| `RequestTests` | Request host test validation | None |

Action and parameter IDs are stable numeric identities in `AiCiCommandCatalog`. They are application vocabulary, not CommandAi capabilities. Equivalent builder calls produce deterministic ordered proposal data and deterministic CommandAi text encoding. The builder does not interpret package versions, install dependencies, run tests, or mutate a repository.

### Composition constraints

- Repository identity must have `owner/repository` form.
- The target must be an isolated branch; canonical `master` and `development` targets are rejected.
- The base commit must be a full hexadecimal SHA-1 commit identity.
- Paths must be bounded, repository-relative, and free of roots, traversal segments, backslashes, colons, and control characters.
- A modification carries the expected full 64-character SHA-256 digest of the current file content as a typed argument. The host must independently verify this digest before applying the operation; the proposal is not proof that the check occurred.
- Literal content and command count are bounded by CommandAi's `CommandLimits`.
- Empty proposals are rejected.

### Source and clean CI

CiAi pins CommandAi as a Git submodule at an explicit commit and references that project directly. This keeps the shared source contract authoritative without relying on an unpublished or floating NuGet package. Both CiAi's contract workflow and the Cog ecosystem integration workflow initialize submodules recursively.

### Verification

The contract suite exercises the action vocabulary and ordering, repository/branch/base bindings, deterministic encoding and codec round-trip, invalid branch and identity rejection, path traversal rejection, modification preconditions, payload bounds, and proposal-only/no-side-effect behavior.

The exact PR head must pass both **CiAi Contract Verification** and **Cog Ecosystem Integration** before this slice is considered verified. A pass establishes the tested source SHA and workflow outcomes only; it does not authorize a host mutation or establish the complete CODE RED repair loop.

## 3. Explicit boundary and remaining work

This slice does **not** implement:

- GrammarAi schema registration for repository-specific precondition expressions or ProtocolAi envelope integration.
- Foundry's host-side capability registry, policy evaluation, authenticated approval enforcement, or repository mutation adapter.
- GitHub branch/file writes, workflow dispatch, build/test execution, or evidence callback delivery.
- A complete failed-CI → model repair → guarded mutation → exact-SHA green-CI demonstration.

Those responsibilities remain with their designated owners and existing CODE RED issues. The next acceptance is a host-authorized, isolated-feature-branch application of a bounded proposal, followed by CiAi evidence evaluation for that exact candidate SHA and a provenance-bound repair turn when validation fails.

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
