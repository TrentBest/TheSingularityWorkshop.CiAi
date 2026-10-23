# CiRunSnapshot

**User moniker:** CiAi

**Purpose:** immutable, provider-neutral facts about one CI workflow run. It validates repository identity, run ID, full commit SHA, exact GitHub Actions evidence URL, status, and conclusion consistency. `IsSuccessful` is true only for a completed run with a `Success` conclusion.

**Safety boundary:** a snapshot is validated structure built from provider data; it does not authorize actions or establish that an untrusted callback is authentic.

## Class card

```text
CiRunSnapshot
+ Repository : string
+ RunId : long
+ WorkflowName : string
+ HeadSha : string
+ Status : CiRunStatus
+ Conclusion : CiRunConclusion
+ EvidenceUrl : Uri
+ UpdatedAt : DateTimeOffset
+ Identity : string
+ IsSuccessful : bool
```

See [CiRunSnapshotUML.md](CiRunSnapshotUML.md) for the focused class diagram and use cases.
