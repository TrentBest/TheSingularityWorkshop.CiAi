<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

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

For `ciai.workflow-evidence.v2` payloads, `SourceSha` identifies the packet's `candidateSha` (the code tested), while `ProviderRunId` identifies the GitHub run whose `workflowRef`/`workflowSha` describe the trusted workflow execution. The packet retains both SHA roles; do not substitute the workflow-definition SHA for `SourceSha`.

The manifest validates shape, not provider authenticity or digest-to-payload correspondence. Retrieval URIs must use HTTPS/default port and contain no user-info, query, or fragment. The consumer must authenticate the provider, enforce an allowlisted retrieval destination, recompute the digest from retrieved bytes, verify provenance, and run local validation. This class performs no network access and grants no cleanup or acceptance authority.

---

<!-- THE SINGULARITY WORKSHOP FOOTER -->
<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="200" /></p>
