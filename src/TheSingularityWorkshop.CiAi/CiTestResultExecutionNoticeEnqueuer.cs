using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Host-owned publisher for the exact canonical ciai.test-results.v1 UTF-8 bytes.
/// Implementations must persist these bytes unchanged and return the provider's immutable manifest.
/// </summary>
public interface ICiTestResultArtifactPublisher
{
    Task<ArtifactManifest> PublishAsync(
        CiTestResultReport report,
        CiAiArtifactDeliveryIdentity identity,
        ReadOnlyMemory<byte> canonicalReportBytes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes a canonical report, verifies the returned artifact manifest against that report,
/// and durably enqueues the metadata-only FSM_Execution notice before any network send.
/// </summary>
public sealed class CiTestResultExecutionNoticeEnqueuer
{
    public const string CapabilityId = "ciai.test-results";
    public const string CapabilityVersion = "1";
    public const string MediaType = "application/json";

    private readonly ICiTestResultArtifactPublisher _publisher;
    private readonly FileExecutionArtifactReadyNoticeOutbox _outbox;

    public CiTestResultExecutionNoticeEnqueuer(
        ICiTestResultArtifactPublisher publisher,
        FileExecutionArtifactReadyNoticeOutbox outbox)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(outbox);
        _publisher = publisher;
        _outbox = outbox;
    }

    public static string CreateStableNoticeId(CiAiArtifactDeliveryIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var capabilityKey = identity.DeriveIdempotencyKey(CapabilityId);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(capabilityKey));
        return "ciai-test-results-" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    public Task<ExecutionArtifactNoticeEnqueueOutcome> PublishAndEnqueueAsync(
        CiTestResultReport report,
        CiAiArtifactDeliveryIdentity identity,
        string recipientIdentity,
        DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default) =>
        PublishAndEnqueueAsync(report, identity, recipientIdentity,
            CreateStableNoticeId(identity), notifiedAt, cancellationToken);

    public async Task<ExecutionArtifactNoticeEnqueueOutcome> PublishAndEnqueueAsync(
        CiTestResultReport report,
        CiAiArtifactDeliveryIdentity identity,
        string recipientIdentity,
        string noticeId,
        DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeId);
        if (notifiedAt == default)
            throw new ArgumentException("Notice timestamp is required.", nameof(notifiedAt));
        if (!string.Equals(report.Schema, CiTestResultReport.CurrentSchema, StringComparison.Ordinal) ||
            report.ContractVersion != CiTestResultReport.CurrentContractVersion)
            throw new InvalidDataException("Only the canonical ciai.test-results.v1 report can be published to FSM_Execution.");

        var reportBytes = report.ToCanonicalUtf8Bytes();
        var reportDigest = SHA256.HashData(reportBytes);
        var existing = _outbox.ReadPending()
            .FirstOrDefault(item => string.Equals(item.NoticeId, noticeId, StringComparison.Ordinal));
        if (existing is not null)
        {
            try
            {
                ValidateManifest(report, identity, existing.Notice.Manifest, recipientIdentity,
                    reportBytes.Length, reportDigest, existing.Notice.NotifiedAt);
                return ExecutionArtifactNoticeEnqueueOutcome.Duplicate;
            }
            catch (Exception exception) when (exception is InvalidDataException or UnauthorizedAccessException)
            {
                return ExecutionArtifactNoticeEnqueueOutcome.Conflict;
            }
        }

        var manifest = await _publisher.PublishAsync(
            report, identity, new ReadOnlyMemory<byte>(reportBytes.ToArray()), cancellationToken).ConfigureAwait(false);
        ValidateManifest(report, identity, manifest, recipientIdentity, reportBytes.Length, reportDigest, notifiedAt);

        var notice = new ArtifactReadyNotice(
            ArtifactReadyNotice.SupportedContractVersion,
            noticeId,
            recipientIdentity,
            manifest,
            notifiedAt);
        return _outbox.Enqueue(new ExecutionArtifactReadyNoticeDelivery(notice));
    }

    private static void ValidateManifest(
        CiTestResultReport report,
        CiAiArtifactDeliveryIdentity identity,
        ArtifactManifest manifest,
        string recipientIdentity,
        int expectedSize,
        byte[] expectedDigest,
        DateTimeOffset notifiedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.RequestId, identity.RequestId, StringComparison.Ordinal) ||
            !string.Equals(manifest.OperationId, identity.OperationId, StringComparison.Ordinal) ||
            !string.Equals(manifest.IdempotencyKey, identity.DeriveIdempotencyKey(CapabilityId), StringComparison.Ordinal))
            throw new InvalidDataException("Published report manifest does not preserve the exact host dispatch identity.");
        if (!string.Equals(manifest.ProviderRepository, report.Repository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.SourceSha, report.CandidateSha, StringComparison.OrdinalIgnoreCase) ||
            manifest.ProviderRunId != report.RunId)
            throw new UnauthorizedAccessException("Published report manifest is not bound to the exact repository, candidate SHA, and workflow run.");
        if (!string.Equals(manifest.CapabilityId, CapabilityId, StringComparison.Ordinal) ||
            !string.Equals(manifest.CapabilityVersion, CapabilityVersion, StringComparison.Ordinal) ||
            !string.Equals(manifest.MediaType, MediaType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.ClientIdentity, recipientIdentity, StringComparison.Ordinal))
            throw new InvalidDataException("Published report manifest has an unexpected capability, media type, or recipient.");
        if (string.IsNullOrWhiteSpace(manifest.ArtifactId) ||
            !string.Equals(manifest.ArtifactVersion, "v1", StringComparison.Ordinal) ||
            !string.Equals(manifest.ImplementationRevision, report.WorkflowDefinitionSha, StringComparison.Ordinal))
            throw new InvalidDataException("Published report manifest artifact identity or implementation revision does not match the exact report.");
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
