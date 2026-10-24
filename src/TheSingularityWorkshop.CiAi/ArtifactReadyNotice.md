# ArtifactReadyNotice

**User moniker:** Ready Is Not Received

## Purpose

Represents a metadata-only signal that a provider-published artifact is available for a named client CiAi. The notice binds a supported contract version and stable notice ID to the artifact manifest and a notification timestamp.

## UML class card

```text
+---------------------------------------------+
| ArtifactReadyNotice                         |
+---------------------------------------------+
| ContractVersion, NoticeId                   |
| RecipientIdentity, Manifest, NotifiedAt     |
+---------------------------------------------+
| Validates supported version, recipient      |
| binding, and artifact availability window.  |
+---------------------------------------------+
```

## Use case

1. Provider publishes an immutable artifact and manifest.
2. Provider creates a ready notice for the exact intended client.
3. Client validates notice version, provider/client identity, request correlation, expiry, and retrieval policy before pulling.
4. Client independently verifies retrieved bytes and provenance.

A notice does not prove delivery, retrieval, integrity, validation, or acceptance. This type does not send notifications or retrieve content.
