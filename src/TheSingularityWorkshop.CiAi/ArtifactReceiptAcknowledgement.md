# ArtifactReceiptAcknowledgement

**User moniker:** Courtesy Receipt, Not Delete

## Purpose

Captures client-reported retrieval and integrity outcomes for one exact artifact version. Local validation is a separate enum so a successful receipt can never be confused with acceptance.

## UML class card

```text
+------------------------------------------------+
| ArtifactReceiptAcknowledgement                |
+------------------------------------------------+
| AcknowledgementId, DeduplicationKey            |
| RequestId, ArtifactId, ArtifactVersion, Digest |
| ClientIdentity, Retrieved, IntegrityVerified   |
| ProviderRepository, ProviderRunId, SourceSha    |
| AcknowledgedAt, LocalValidation                |
+------------------------------------------------+
| MakesPayloadEligibleForRetentionReview         |
+------------------------------------------------+
```

## Retention boundary

The eligibility property is only a signal for a separate retention-policy decision. It does not delete payloads. Durable artifacts, rejected artifacts, audit holds, other consumers, expiry rules, and provider storage confirmation must be handled by a later retention coordinator. The `VerifyAgainst` method also requires and reconciles provider repository and source SHA, and reconciles provider run ID when the manifest identifies one. This prevents a receipt from being accepted for a different provider run or source revision. Durable duplicate suppression is handled separately by `FileArtifactReceiptLedger`; this contract type itself does not write storage or authorize deletion.
