using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Stable durable delivery identity for one exact v2 workflow run attempt.</summary>
public sealed record CiWorkflowEvidenceV2Delivery : ICiWorkflowEvidenceDelivery
{
    public CiWorkflowEvidenceV2Delivery(string deliveryId, CiWorkflowEvidencePacketV2 packet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentNullException.ThrowIfNull(packet);
        if (deliveryId.Length > 200 || deliveryId.Any(char.IsControl))
            throw new ArgumentException("Delivery ID is invalid.", nameof(deliveryId));

        DeliveryId = deliveryId;
        Packet = packet;
        Payload = packet.ToJson(indented: false);
        PayloadSha256 = packet.PayloadSha256;
        var identity = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = CiWorkflowEvidencePacketV2.CurrentSchema,
            repository = packet.Repository,
            runId = packet.RunId,
            runAttempt = packet.RunAttempt,
            workflowPath = packet.WorkflowPath,
            workflowRef = packet.WorkflowEvidence.Provenance.WorkflowRef,
            workflowSha = packet.WorkflowEvidence.Provenance.WorkflowSha,
            candidateRef = packet.CandidateRef,
            candidateSha = packet.CandidateSha
        });
        IdempotencyKey = "sha256:" +
            Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
        if (!string.Equals(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload))),
            PayloadSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v2 delivery payload bytes do not match the packet digest.");
    }

    public string DeliveryId { get; }
    public string IdempotencyKey { get; }
    public CiWorkflowEvidencePacketV2 Packet { get; }
    public string Payload { get; }
    public string PayloadSha256 { get; }
}

public enum CiWorkflowEvidenceV2EnqueueOutcome
{
    Enqueued,
    Duplicate,
    Conflict
}

/// <summary>
/// Append-only durable outbox for exact v2 evidence bytes. Reload validates the outer digest,
/// nested v1 evidence, required-check bindings, and idempotency identity without restoring host trust.
/// </summary>
public sealed class FileCiWorkflowEvidenceV2Outbox
{
    private readonly string _path;

    public FileCiWorkflowEvidenceV2Outbox(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiWorkflowEvidenceV2EnqueueOutcome Enqueue(CiWorkflowEvidenceV2Delivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var records = ReadRecords();

        var existing = records.FirstOrDefault(record =>
            string.Equals(record.DeliveryId, delivery.DeliveryId, StringComparison.Ordinal));
        if (existing is not null)
            return SamePayload(existing, delivery)
                ? CiWorkflowEvidenceV2EnqueueOutcome.Duplicate
                : CiWorkflowEvidenceV2EnqueueOutcome.Conflict;

        existing = records.FirstOrDefault(record =>
            string.Equals(record.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
            return SamePayload(existing, delivery)
                ? CiWorkflowEvidenceV2EnqueueOutcome.Duplicate
                : CiWorkflowEvidenceV2EnqueueOutcome.Conflict;

        var record = new OutboxRecord(delivery.DeliveryId, delivery.IdempotencyKey,
            delivery.Packet.Repository, delivery.Packet.RunId, delivery.Packet.RunAttempt,
            delivery.Packet.RequiredChecks.Select(check => new CiWorkflowRequiredCheckBinding(check.Key, check.JobName)).ToArray(),
            delivery.Payload, delivery.PayloadSha256);
        Append(JsonSerializer.Serialize(record));
        return CiWorkflowEvidenceV2EnqueueOutcome.Enqueued;
    }

    public IReadOnlyList<CiWorkflowEvidenceV2Delivery> ReadPending(
        IReadOnlySet<string>? acknowledgedDeliveryIds = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var pending = ReadRecords()
            .Where(record => acknowledgedDeliveryIds is null ||
                !acknowledgedDeliveryIds.Contains(record.DeliveryId))
            .Select(record =>
            {
                var packet = CiWorkflowEvidencePacketV2.FromJson(
                    record.Payload, record.Repository, record.RunId, record.RunAttempt,
                    record.PayloadSha256, record.RequiredChecks, workflowDefinitionTrusted: false);
                var delivery = new CiWorkflowEvidenceV2Delivery(record.DeliveryId, packet);
                if (!string.Equals(delivery.IdempotencyKey, record.IdempotencyKey, StringComparison.Ordinal) ||
                    !string.Equals(delivery.PayloadSha256, record.PayloadSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("v2 outbox record integrity or identity validation failed.");
                return delivery;
            }).ToArray();
        return Array.AsReadOnly(pending);
    }

    private static bool SamePayload(OutboxRecord record, CiWorkflowEvidenceV2Delivery delivery) =>
        string.Equals(record.PayloadSha256, delivery.PayloadSha256, StringComparison.Ordinal) &&
        string.Equals(record.Payload, delivery.Payload, StringComparison.Ordinal) &&
        string.Equals(record.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal);

    private FileStream AcquireFileLock() => new(_path + ".lock", FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);

    private void Append(string line)
    {
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(Encoding.UTF8.GetBytes(line + "\n"));
        output.Flush(flushToDisk: true);
    }

    private OutboxRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<OutboxRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<OutboxRecord>();
        if (input.Length > int.MaxValue)
            throw new InvalidDataException("v2 evidence outbox exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("v2 evidence outbox ends with an incomplete record; refusing to discard evidence.");

        var records = new List<OutboxRecord>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var record = JsonSerializer.Deserialize<OutboxRecord>(line)
                ?? throw new InvalidDataException("v2 evidence outbox contains an empty record.");
            if (string.IsNullOrWhiteSpace(record.DeliveryId) ||
                !IsValidIdempotencyKey(record.IdempotencyKey) ||
                !RepositoryIdentity.IsValid(record.Repository) ||
                record.RunId <= 0 || record.RunAttempt <= 0 ||
                record.RequiredChecks is null || record.RequiredChecks.Length == 0 ||
                string.IsNullOrWhiteSpace(record.Payload) ||
                record.PayloadSha256 is not { Length: 64 } ||
                !record.PayloadSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("v2 evidence outbox contains an invalid record.");
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(record.Payload));
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(record.PayloadSha256)))
                throw new InvalidDataException("v2 evidence outbox payload digest is invalid.");
            records.Add(record);
        }

        if (records.Select(record => record.DeliveryId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            records.Select(record => record.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("v2 evidence outbox contains duplicate delivery or attempt identities.");
        return records.ToArray();
    }

    private static bool IsValidIdempotencyKey(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).ToString().All(IsLowerHex);

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private sealed record OutboxRecord(
        string DeliveryId,
        string IdempotencyKey,
        string Repository,
        long RunId,
        int RunAttempt,
        CiWorkflowRequiredCheckBinding[] RequiredChecks,
        string Payload,
        string PayloadSha256);
}
