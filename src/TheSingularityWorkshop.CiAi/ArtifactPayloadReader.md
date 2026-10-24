# ArtifactPayloadReader

**User moniker:** Bounded Stream, Verified Bytes

## Purpose

Reads from a caller-authenticated stream into bounded memory and verifies the completed payload against the immutable manifest's declared byte count and SHA-256 digest. The caller retains ownership of the stream.

## UML class card

```text
+--------------------------------------------------+
| ArtifactPayloadReader                            |
+--------------------------------------------------+
| ReadAndVerifyAsync(Stream, Manifest, Limit, CT)  |
+--------------------------------------------------+
| ArtifactPayloadReadResult                        |
| Payload? : ReadOnlyMemory<byte>                  |
| Verification : ArtifactPayloadVerificationResult |
+--------------------------------------------------+
```

## Use case

1. The caller authenticates and authorizes a retrieval source outside this class.
2. The caller supplies the readable stream and expected manifest.
3. The reader enforces the configured byte limit while reading, observes cancellation, and rejects short, oversized, or digest-mismatched content.
4. Only verified payload bytes are returned. Rejected results expose no payload.
5. The caller disposes its stream and decides how to stage or locally validate the verified bytes.

This class does **not** fetch `manifest.RetrievalUri`, authenticate a provider, persist bytes, parse payload content, or execute anything.
