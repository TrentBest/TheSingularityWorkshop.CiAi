# ArtifactPayloadVerifier

**User moniker:** Byte Truth Gate

## Purpose

Checks a payload already retrieved by the caller against the exact size and SHA-256 digest in an `ArtifactManifest`. The caller may supply a stricter maximum payload size; the default is 16 MiB.

## UML class card

```text
+----------------------------------------------------+
| ArtifactPayloadVerifier                            |
+----------------------------------------------------+
| +DefaultMaximumPayloadBytes: long                  |
+----------------------------------------------------+
| +Verify(manifest, payload, maximumPayloadBytes)     |
|   : ArtifactPayloadVerificationResult               |
+----------------------------------------------------+
| Bounds size before hashing; checks exact byte count |
| and SHA-256 using a fixed-time digest comparison.   |
+----------------------------------------------------+

+----------------------------------------------------+
| ArtifactPayloadVerificationResult                  |
+----------------------------------------------------+
| IsVerified, FailureCode, ActualSizeBytes, ActualDigest|
+----------------------------------------------------+
```

## Security boundary

This verifier performs no network access, persistence, decompression, parsing, provider authentication, or execution. It cannot prevent a caller from allocating an oversized buffer before calling it; the retrieval layer must enforce streaming limits while downloading. A matching digest proves byte correspondence to the manifest, not producer authenticity or content safety.
