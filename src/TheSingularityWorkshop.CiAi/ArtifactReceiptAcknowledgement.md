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
| AcknowledgedAt, LocalValidation                |
+------------------------------------------------+
| MakesPayloadEligibleForRetentionReview         |
+------------------------------------------------+
```

## Retention boundary

The eligibility property is only a signal for a separate retention-policy decision. It does not delete payloads. Durable artifacts, rejected artifacts, audit holds, other consumers, expiry rules, and provider storage confirmation must be handled by a later retention coordinator. The `VerifyAgainst` method returns stable mismatch codes for request ID, artifact ID/version, digest, and client identity, allowing the coordinator to reject a receipt for the wrong artifact. A stable deduplication key is provided as contract data; durable duplicate suppression is not implemented in this type.
