using System.Security.Cryptography;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Host-owned publisher for the exact canonical ciai.workflow-evidence.v1 UTF-8 packet.
/// The publisher must persist immutable bytes and return their provider manifest.
/// </summary>
public interface ICiWorkflowEvidenceArtifactPublisher
{
    Task<ArtifactManifest> PublishAsync(
        CiWorkflowEvidenceDelivery delivery,
        ReadOnlyMemory<byte> canonicalPacketBytes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes a durable v1 evidence-outbox entry and enqueues a metadata-only FSM_Execution
/// ready notice. Workflow evidence and test-result diagnostics remain separate artifacts.
/// </summary>
public sealed class CiWorkflowEvidenceExecutionNoticeEnqueuer
{
    public const string CapabilityId = "ciai.workflow-evidence";
    public const string CapabilityVersion = "1";
    public const string MediaType = "application/json";

    private readonly ICiWorkflowEvidenceArtifactPublisher _publisher;
    private readonly FileExecutionArtifactReadyNoticeOutbox _noticeOutbox;

    public CiWorkflowEvidenceExecutionNoticeEnqueuer(
        ICiWorkflowEvidenceArtifactPublisher publisher,
        FileExecutionArtifactReadyNoticeOutbox noticeOutbox)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(noticeOutbox);
        _publisher = publisher;
        _noticeOutbox = noticeOutbox;
    }

    public async Task<ExecutionArtifactNoticeEnqueueOutcome> PublishAndEnqueueAsync(
        CiWorkflowEvidenceDelivery delivery,
        string recipientIdentity,
        string noticeId,
        DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeId);
        if (notifiedAt == default)
            throw new ArgumentException("Notice timestamp is required.", nameof(notifiedAt));

        var packet = delivery.Packet;
        if (!string.Equals(packet.Schema, CiWorkflowEvidencePacket.CurrentSchema, StringComparison.Ordinal) ||
            packet.ContractVersion != CiWorkflowEvidencePacket.CurrentContractVersion)
            throw new InvalidDataException("Only canonical ciai.workflow-evidence.v1 packets can be published.");

        var bytes = System.Text.Encoding.UTF8.GetBytes(delivery.Payload);
        var digest = SHA256.HashData(bytes);
        var manifest = await _publisher.PublishAsync(
            delivery, new ReadOnlyMemory<byte>(bytes.ToArray()), cancellationToken).ConfigureAwait(false);
        ValidateManifest(delivery, manifest, recipientIdentity, bytes.Length, digest, notifiedAt);

        var notice = new ArtifactReadyNotice(
            ArtifactReadyNotice.SupportedContractVersion,
            noticeId,
            recipientIdentity,
            manifest,
            notifiedAt);
        return _noticeOutbox.Enqueue(new ExecutionArtifactReadyNoticeDelivery(notice));
    }

    private static void ValidateManifest(
        CiWorkflowEvidenceDelivery delivery,
        ArtifactManifest manifest,
        string recipientIdentity,
        int expectedSize,
        byte[] expectedDigest,
        DateTimeOffset notifiedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var packet = delivery.Packet;
        var run = packet.ObservedRun;
        if (!string.Equals(manifest.RequestId, delivery.DeliveryId, StringComparison.Ordinal) ||
            !string.Equals(manifest.OperationId, $"workflow-evidence:{run.RunId}:{run.RunAttempt}", StringComparison.Ordinal) ||
            !string.Equals(manifest.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal))
            throw new InvalidDataException("Published evidence manifest does not preserve the exact delivery identity.");
        if (!string.Equals(manifest.ProviderRepository, run.Repository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.SourceSha, packet.CandidateSha, StringComparison.OrdinalIgnoreCase) ||
            manifest.ProviderRunId != run.RunId)
            throw new UnauthorizedAccessException("Published evidence manifest is not bound to the exact repository, candidate SHA, and workflow run.");
        if (!string.Equals(manifest.CapabilityId, CapabilityId, StringComparison.Ordinal) ||
            !string.Equals(manifest.CapabilityVersion, CapabilityVersion, StringComparison.Ordinal) ||
            !string.Equals(manifest.ImplementationRevision, packet.Provenance.WorkflowSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.MediaType, MediaType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.ClientIdentity, recipientIdentity, StringComparison.Ordinal))
            throw new InvalidDataException("Published evidence manifest has an unexpected capability, workflow revision, media type, or recipient.");
        if (!string.Equals(manifest.DigestAlgorithm, "SHA-256", StringComparison.Ordinal) ||
            manifest.SizeBytes != expectedSize ||
            manifest.Digest.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(expectedDigest, Convert.FromHexString(manifest.Digest)))
            throw new InvalidDataException("Published evidence manifest does not preserve the exact canonical packet bytes.");
        if (manifest.CreatedAt < run.ObservedAt || notifiedAt < manifest.CreatedAt || notifiedAt >= manifest.ExpiresAt)
            throw new InvalidDataException("Published evidence manifest or notice timestamp is outside the observed evidence validity window.");
    }
}
