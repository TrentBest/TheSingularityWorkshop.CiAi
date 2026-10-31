# ArtifactManifest

**User moniker:** Immutable Capability Receipt

## Purpose

Describes one provider-owned, immutable artifact version for a specific request and client. The manifest carries bounded retrieval metadata, provider/capability identity, source revision, digest, size, expiry, retention class, and optional provider workflow run ID.

## UML class card

```text
+----------------------------------------------------+
| ArtifactManifest                                   |
+----------------------------------------------------+
| RequestId, OperationId, IdempotencyKey              |
| ProviderRepository, CapabilityId, CapabilityVersion|
| ImplementationRevision, ArtifactId, ArtifactVersion|
| SourceSha, DigestAlgorithm, Digest                  |
| MediaType, SizeBytes, RetrievalUri                  |
| CreatedAt, ExpiresAt, RetentionClass                |
| ClientIdentity, ProviderRunId                       |
+----------------------------------------------------+
| Constructor validates identity, digest, bounds,     |
| HTTPS retrieval URI, and expiry ordering.           |
+----------------------------------------------------+
```

## Trust boundary

The manifest validates shape, not provider authenticity or digest-to-payload correspondence. Retrieval URIs must use HTTPS/default port and contain no user-info, query, or fragment. The consumer must authenticate the provider, enforce an allowlisted retrieval destination, recompute the digest from retrieved bytes, verify provenance, and run local validation. This class performs no network access and grants no cleanup or acceptance authority.
