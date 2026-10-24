namespace TheSingularityWorkshop.CiAi;

/// <summary>A metadata-only notice that a published artifact is available for client-side pull.</summary>
public sealed record ArtifactReadyNotice
{
    public const int SupportedContractVersion = 1;

    public ArtifactReadyNotice(
        int contractVersion,
        string noticeId,
        string recipientIdentity,
        ArtifactManifest manifest,
        DateTimeOffset notifiedAt)
    {
        if (contractVersion != SupportedContractVersion)
            throw new ArgumentOutOfRangeException(nameof(contractVersion), "Unsupported handoff contract version.");
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientIdentity);
        ArgumentNullException.ThrowIfNull(manifest);
        if (noticeId.Length > 200 || noticeId.Any(char.IsControl))
            throw new ArgumentException("Notice ID is invalid.", nameof(noticeId));
        if (recipientIdentity.Length > 200 || recipientIdentity.Any(char.IsControl))
            throw new ArgumentException("Recipient identity is invalid.", nameof(recipientIdentity));
        if (!string.Equals(recipientIdentity, manifest.ClientIdentity, StringComparison.Ordinal))
            throw new ArgumentException("Notice recipient does not match the manifest's intended client.", nameof(recipientIdentity));
        if (notifiedAt < manifest.CreatedAt || notifiedAt >= manifest.ExpiresAt)
            throw new ArgumentOutOfRangeException(nameof(notifiedAt), "Notice time must fall within the artifact's availability window.");

        ContractVersion = contractVersion;
        NoticeId = noticeId;
        RecipientIdentity = recipientIdentity;
        Manifest = manifest;
        NotifiedAt = notifiedAt;
    }

    public int ContractVersion { get; }
    public string NoticeId { get; }
    public string RecipientIdentity { get; }
    public ArtifactManifest Manifest { get; }
    public DateTimeOffset NotifiedAt { get; }
}
