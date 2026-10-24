namespace TheSingularityWorkshop.CiAi;

/// <summary>Client receipt facts correlated to one exact immutable artifact version.</summary>
public sealed record ArtifactReceiptAcknowledgement
{
    public ArtifactReceiptAcknowledgement(
        int contractVersion,
        string acknowledgementId,
        string deduplicationKey,
        string requestId,
        string artifactId,
        string artifactVersion,
        string digest,
        string clientIdentity,
        bool retrieved,
        bool integrityVerified,
        DateTimeOffset acknowledgedAt,
        ArtifactLocalValidationOutcome localValidation = ArtifactLocalValidationOutcome.NotRun)
    {
        if (contractVersion != ArtifactReadyNotice.SupportedContractVersion)
            throw new ArgumentOutOfRangeException(nameof(contractVersion), "Unsupported handoff contract version.");
        RequireText(acknowledgementId, nameof(acknowledgementId));
        RequireText(deduplicationKey, nameof(deduplicationKey));
        RequireText(requestId, nameof(requestId));
        RequireText(artifactId, nameof(artifactId));
        RequireText(artifactVersion, nameof(artifactVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 digest must contain 64 hexadecimal characters.", nameof(digest));
        RequireText(clientIdentity, nameof(clientIdentity));
        if (integrityVerified && !retrieved)
            throw new ArgumentException("Integrity cannot be verified before retrieval.", nameof(integrityVerified));
        if (!Enum.IsDefined(localValidation))
            throw new ArgumentOutOfRangeException(nameof(localValidation));

        ContractVersion = contractVersion;
        AcknowledgementId = acknowledgementId;
        DeduplicationKey = deduplicationKey;
        RequestId = requestId;
        ArtifactId = artifactId;
        ArtifactVersion = artifactVersion;
        Digest = digest.ToLowerInvariant();
        ClientIdentity = clientIdentity;
        Retrieved = retrieved;
        IntegrityVerified = integrityVerified;
        AcknowledgedAt = acknowledgedAt;
        LocalValidation = localValidation;
    }

    public int ContractVersion { get; }
    public string AcknowledgementId { get; }
    public string DeduplicationKey { get; }
    public string RequestId { get; }
    public string ArtifactId { get; }
    public string ArtifactVersion { get; }
    public string Digest { get; }
    public string ClientIdentity { get; }
    public bool Retrieved { get; }
    public bool IntegrityVerified { get; }
    public DateTimeOffset AcknowledgedAt { get; }
    public ArtifactLocalValidationOutcome LocalValidation { get; }

    /// <summary>Reconciles this receipt with the exact provider-published manifest.</summary>
    public IReadOnlyList<string> VerifyAgainst(ArtifactManifest? manifest)
    {
        if (manifest is null)
            return new[] { "manifest.required" };

        var errors = new List<string>();
        if (!string.Equals(RequestId, manifest.RequestId, StringComparison.Ordinal))
            errors.Add("requestId.mismatch");
        if (!string.Equals(ArtifactId, manifest.ArtifactId, StringComparison.Ordinal))
            errors.Add("artifactId.mismatch");
        if (!string.Equals(ArtifactVersion, manifest.ArtifactVersion, StringComparison.Ordinal))
            errors.Add("artifactVersion.mismatch");
        if (!string.Equals(Digest, manifest.Digest, StringComparison.OrdinalIgnoreCase))
            errors.Add("digest.mismatch");
        if (!string.Equals(ClientIdentity, manifest.ClientIdentity, StringComparison.Ordinal))
            errors.Add("clientIdentity.mismatch");
        return errors;
    }

    /// <summary>Receipt alone never authorizes deletion; retention policy must independently approve cleanup.</summary>
    public bool MakesPayloadEligibleForRetentionReview =>
        Retrieved && IntegrityVerified && LocalValidation != ArtifactLocalValidationOutcome.Rejected;

    private static void RequireText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 200 || value.Any(char.IsControl))
            throw new ArgumentException("Value must be at most 200 characters and contain no control characters.", parameterName);
    }
}

/// <summary>Consumer-side validation is distinct from transport receipt and digest verification.</summary>
public enum ArtifactLocalValidationOutcome
{
    NotRun,
    Accepted,
    Rejected
}
