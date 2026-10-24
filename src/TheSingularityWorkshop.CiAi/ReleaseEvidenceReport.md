# ReleaseEvidenceReport

**User moniker:** The Singularity Workshop

## Purpose

Represent release evidence tied to one exact repository and commit, and conservatively aggregate required categories without converting missing, skipped, stale, unknown, or unverified observations into a pass.

## UML class card

```text
+----------------------------------------------------+
| ReleaseEvidenceReport                              |
+----------------------------------------------------+
| + Repository : string                              |
| + HeadSha : string                                |
| + Categories : IReadOnlyDictionary<Category,State> |
| + Records : IReadOnlyList<Record>                  |
| + IsPassing : bool                                |
| + Aggregate(repository, sha, required, records)   |
+----------------------------------------------------+
| Exact repository/SHA boundary; missing requirements|
| stay missing; every required category must pass.   |
+----------------------------------------------------+
```

## Use cases

- Build a release-gate summary from multiple independently verified workflow observations.
- Preserve raw provider status/conclusion beside the normalized category state.
- Track workflow run/attempt, PR, job, diagnostic, retrieval timestamp, and artifact digest scope.
- Reject evidence collections that mix candidate repositories or commit SHAs.
- Keep publication skipped/missing distinct from publication success; the report is not authorization to publish.

## Boundaries

This model does not discover workflows, call GitHub, verify provider responses, prove artifact contents, or authorize release actions. The collector must validate the provider data before creating `ReleaseEvidenceRecord` instances. A SHA-256 digest must identify its scope (for example, downloaded `.nupkg` bytes versus a digest inside a package manifest). `IsPassing` means only that the supplied required categories are directly verified and passing; it is not a release authorization.
