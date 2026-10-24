# FileArtifactReceiptLedger

**User moniker:** Durable Receipt, No Deletion Authority

## Purpose

Persists receipt evidence in a local append-only ledger. Exact retries with the same deduplication key and receipt content return `Duplicate`; reusing that key for different content returns `Conflict`. A receipt that does not reconcile with its manifest returns `Rejected` and is not persisted.

## UML class card

```text
+-----------------------------------------------------+
| FileArtifactReceiptLedger                           |
+-----------------------------------------------------+
| Record(Receipt, Manifest) : RecordOutcome           |
| ReadCompleteLinesAndRepairTail()                    |
+-----------------------------------------------------+
| ArtifactReceiptRecordOutcome                        |
| Recorded | Duplicate | Conflict | Rejected          |
+-----------------------------------------------------+
```

## Durability and concurrency

- Appends one complete encoded receipt record and flushes the file to disk.
- Serializes writers across processes with an exclusive sidecar lock file.
- Ignores and truncates an incomplete trailing record after a crash before accepting another write.
- Rejects a conflicting duplicate instead of silently replacing earlier evidence.
- Limits in-memory recovery scans to 2 GiB; larger ledgers fail explicitly rather than allocating an unbounded buffer.
- A competing writer may receive `IOException`; retry policy belongs to the caller.

The ledger does not authenticate the provider, fetch artifact URLs, decide consumer acceptance, or authorize retention cleanup. The file and its directory must be protected by the host's filesystem permissions.
