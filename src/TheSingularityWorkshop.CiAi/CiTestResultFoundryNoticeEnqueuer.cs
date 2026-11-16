using System.Security.Cryptography;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Host-owned publisher for the exact canonical ciai.test-results.v1 UTF-8 bytes.
/// Implementations must persist these bytes unchanged and return the provider's immutable manifest.
/// </summary>
public interface ICiTestResultArtifactPublisher
{
    Task<ArtifactManifest> PublishAsync(
        CiTestResultReport report,
        ReadOnlyMemory<byte> canonicalReportBytes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes a canonical report, verifies the returned artifact manifest against that report,
/// and durably enqueues the metadata-only Foundry notice before any network send.
/// </summary>
public sealed class CiTestResultFoundryNoticeEnqueuer
{
    public const string CapabilityId = "ciai.test-results";
    public const string CapabilityVersion = "1";
    public const string MediaType = "application/json";

    private readonly ICiTestResultArtifactPublisher _publisher;
    private readonly FileFoundryArtifactReadyNoticeOutbox _outbox;

    public CiTestResultFoundryNoticeEnqueuer(
        ICiTestResultArtifactPublisher publisher,
        FileFoundryArtifactReadyNoticeOutbox outbox)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(outbox);
        _publisher = publisher;
        _outbox = outbox;
    }

    public async Task<FoundryArtifactNoticeEnqueueOutcome> PublishAndEnqueueAsync(
        CiTestResultReport report,
        string recipientIdentity,
        string noticeId,
        DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeId);
        if (notifiedAt == default)
            throw new ArgumentException("Notice timestamp is required.", nameof(notifiedAt));
        if (!string.Equals(report.Schema, CiTestResultReport.CurrentSchema, StringComparison.Ordinal) ||
            report.ContractVersion != CiTestResultReport.CurrentContractVersion)
            throw new InvalidDataException("Only the canonical ciai.test-results.v1 report can be published to Foundry.");

        var reportBytes = report.ToCanonicalUtf8Bytes();
        var reportDigest = SHA256.HashData(reportBytes);
        var manifest = await _publisher.PublishAsync(
            report, new ReadOnlyMemory<byte>(reportBytes.ToArray()), cancellationToken).ConfigureAwait(false);
        ValidateManifest(report, manifest, recipientIdentity, reportBytes.Length, reportDigest, notifiedAt);

        var notice = new ArtifactReadyNotice(
            ArtifactReadyNotice.SupportedContractVersion,
            noticeId,
            recipientIdentity,
            manifest,
            notifiedAt);
        return _outbox.Enqueue(new FoundryArtifactReadyNoticeDelivery(notice));
    }

    private static void ValidateManifest(
        CiTestResultReport report,
        ArtifactManifest manifest,
        string recipientIdentity,
        int expectedSize,
        byte[] expectedDigest,
        DateTimeOffset notifiedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.ProviderRepository, report.Repository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.SourceSha, report.CandidateSha, StringComparison.OrdinalIgnoreCase) ||
            manifest.ProviderRunId != report.RunId)
            throw new UnauthorizedAccessException("Published report manifest is not bound to the exact repository, candidate SHA, and workflow run.");
        if (!string.Equals(manifest.CapabilityId, CapabilityId, StringComparison.Ordinal) ||
            !string.Equals(manifest.CapabilityVersion, CapabilityVersion, StringComparison.Ordinal) ||
            !string.Equals(manifest.MediaType, MediaType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.ClientIdentity, recipientIdentity, StringComparison.Ordinal))
            throw new InvalidDataException("Published report manifest has an unexpected capability, media type, or recipient.");
        if (!string.Equals(manifest.IdempotencyKey, report.ReportIdentity, StringComparison.Ordinal))
            throw new InvalidDataException("Published report manifest idempotency key does not match the canonical report identity.");
        if (!string.Equals(manifest.DigestAlgorithm, "SHA-256", StringComparison.Ordinal) ||
            manifest.SizeBytes != expectedSize ||
            manifest.Digest.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(
                expectedDigest, Convert.FromHexString(manifest.Digest)))
            throw new InvalidDataException("Published report manifest does not preserve the exact canonical report bytes.");
        if (manifest.CreatedAt < report.ObservedAt || notifiedAt < manifest.CreatedAt || notifiedAt >= manifest.ExpiresAt)
            throw new InvalidDataException("Published report manifest or notice timestamp is outside the report's validity window.");
    }
}
