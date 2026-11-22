# CiAi repository composition, badges, and observer profiles

**Status:** Initial composable API and deterministic README badge-stamping implementation. Host enrollment, remote workflow-definition retrieval, and cross-repository orchestration remain separate integration work.

## Repository composition

CiAi configuration is declarative. It describes repository capabilities; it does not execute model-authored code, grant host permissions, publish packages, merge branches, or deploy applications.

\`\`\`csharp
var configuration = CiAi
    .WithBadge(Badges.SithPolicy())
    .WithDependency(CiAi.Resolve("TrentBest/TheSingularityWorkshop.FSM_API", "runtime"))
    .ExtendWith(Gauntlet.Registrar())
    .Build("TrentBest/TheSingularityWorkshop.CiAi");
\`\`\`

The builder is immutable: every fluent call returns a new builder. Duplicate badge IDs, dependency declarations, and extension IDs are rejected. \`Build\` binds the declarations to one canonical \`owner/repository\` identity.

- \`WithBadge\` enables a badge definition.
- \`CiAi.Resolve\` creates a typed repository/capability reference; it does not fetch or trust a repository.
- \`WithDependency\` records the dependency edge. It does not authorize package adoption or release.
- \`ExtendWith\` registers a versioned extension identifier. An extension is declarative data, not executable code emitted by an LLM.

The built-in \`Badges.SithPolicy()\` uses GitHub's live workflow badge SVG for \`.github/workflows/sith-policy.yml\`. The SVG reflects the workflow status; the README does not hard-code a green/pass image.

## README stamping

Run the repository tool from the repository root:

```bash
dotnet run --project tools/CiAi.Repository -- --stamp-readmes
dotnet run --project tools/CiAi.Repository -- --check-readmes
```

The checked-in `.ciai/repository.json` is the declarative source of enabled badges, dependencies, and extensions. The CLI validates the manifest repository against `GITHUB_REPOSITORY` in CI. It stamps all repository-owned `README.md` files recursively (case-insensitive), excluding Git metadata, generated directories, and nested Git submodules. The reusable workflow below runs the read-only check in CI; it never commits to a canonical branch.

A consumer opts in with a small caller workflow:

```yaml
jobs:
  ciai-repository:
    uses: TrentBest/TheSingularityWorkshop.CiAi/.github/workflows/ciai-repository.yml@development
    with:
      ciai-ref: development
```

\`CiAiReadmeBadgeStamper.Stamp\` owns only the region between:

\`\`\`markdown
<!-- CIAI:BADGES:START -->
<!-- CIAI:BADGES:END -->
\`\`\`

Text outside that managed region is preserved. When no region exists, the block is inserted immediately after the first level-one heading. An incomplete or misordered marker pair is rejected rather than guessed.

\`StampRepositoryReadmes\` visits case-insensitive files named \`README.md\`, recursively, while excluding common generated directories and reparse-point files. Repeated stamping is deterministic and makes no change after the first successful stamp. A host workflow or maintenance command can call it; the library does not silently mutate the working tree.

A repository must explicitly opt in by checking in a CiAi configuration and invoking the stamper from its trusted workflow. No repository is enrolled merely because it is visible to the GitHub connection.

## External workflow observer profiles

\`CiWorkflowObserverProfile\` describes workflow-specific names and identities instead of baking the CiAi workflow names into every consumer:

| Profile | Workflow | Provenance artifact | Test artifact | Required job |
|---|---|---|---|---|
| \`CiAiDefault\` | \`.github/workflows/ci.yml\` | \`ciai-workflow-provenance-{attempt}\` | \`ciai-test-results-{attempt}\` | \`contract\` |
| \`RaWWar\` | \`.github/workflows/build.yml\` | \`rawwar-workflow-provenance-{attempt}\` | \`rawwar-contract-results-{runId}-{attempt}\` | \`build-and-contracts\` |

Supported artifact placeholders are \`{runId}\` and \`{attempt}\`. Profile values are configuration only; provider metadata must independently bind the source repository, run, attempt, artifact ID/name/digest/size, and exact required job.

## Workflow-definition trust

A successful candidate-controlled workflow is not automatically trusted. \`CiWorkflowDefinitionTrustPolicy\` compares exact workflow-file bytes retrieved by the host at the run's recorded workflow revision with exact bytes retrieved at an approved protected revision. The decision records both revisions, refs, content digests, and a reason code.

The policy helper does not download either file. A trusted host must retrieve both versions from GitHub using authenticated, exact refs, verify the returned repository/path/ref identities, and supply those bytes. Missing or mismatched content is untrusted. Provenance JSON and candidate-controlled booleans are not trust authorities.

The current raWWar PR #10 fixture remains structurally observable but non-actionable until the protected workflow-definition comparison succeeds. The first actionable pilot may be restricted to protected \`push\` runs on \`development\`; PR source head, PR base, merge/event SHA, and checkout SHA remain distinct identities and must not be collapsed.

## Enrollment strategy

CiAi enrollment will be driven by an explicit repository registry with an owner-approved scope and per-repository profile. Start with active Workshop source repositories and proven pilots; then expand to dependency-free repositories; finally add dependency-heavy repositories after dependency graph and release-gate evidence is reliable. Legacy bots, historical snapshots, benchmarks, private research repositories, and unrelated repositories must be classified before opt-in.

No package publication, merge, release, deployment, canonical-branch mutation, or branch deletion follows from enrollment or a passing badge.


The current implementation branch is undergoing exact-head contract verification; this document remains a proposed opt-in contract until its PR is reviewed and accepted.


## Ecosystem enrollment

See [the repository enrollment matrix](REPOSITORY_ENROLLMENT_MATRIX.md) for the active workstream inventory, adoption state, and rollout order. Repository-contract enrollment, trusted CI observation, and autonomous repair are tracked as separate maturity levels.
