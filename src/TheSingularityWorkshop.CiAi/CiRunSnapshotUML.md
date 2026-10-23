# CiRunSnapshot — UML and Use Cases

## Class diagram

```mermaid
classDiagram
    class CiRunSnapshot {
        +string Repository
        +long RunId
        +string WorkflowName
        +string HeadSha
        +CiRunStatus Status
        +CiRunConclusion Conclusion
        +Uri EvidenceUrl
        +DateTimeOffset UpdatedAt
        +string Identity
        +bool IsSuccessful
    }
    class CiRunStatus {
        <<enumeration>>
        Unknown
        Queued
        InProgress
        Completed
    }
    class CiRunConclusion {
        <<enumeration>>
        None
        Success
        Failure
        Cancelled
        TimedOut
        ActionRequired
        Neutral
        Skipped
        Stale
        Unknown
    }
    class GitHubActionsRunClient
    GitHubActionsRunClient ..> CiRunSnapshot : constructs
    CiRunSnapshot --> CiRunStatus
    CiRunSnapshot --> CiRunConclusion
```

## Use cases

- Normalize a provider response into a typed, immutable observation.
- Reject mismatched repository/run evidence, malformed full commit SHA, and inconsistent status/conclusion combinations.
- Let callers ask whether a run succeeded without interpreting arbitrary provider strings.
- Preserve unknown future conclusions without accidentally treating them as success.

This diagram describes the current source contract. It does not imply dispatch, authorization, LLM invocation, or GUI integration.
