using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Stable delivery identity for one exact v3 workflow run attempt and checkout.</summary>
public sealed record CiWorkflowEvidenceDeliveryV3
{
    public CiWorkflowEvidenceDeliveryV3(string deliveryId, CiWorkflowEvidencePacketV3 packet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentNullException.ThrowIfNull(packet);
        if (deliveryId.Length > 200 || deliveryId.Any(char.IsControl))
            throw new ArgumentException("Delivery ID is invalid.", nameof(deliveryId));

        DeliveryId = deliveryId;
        Packet = packet;
        Payload = packet.ToJson(indented: false);
        PayloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload))).ToLowerInvariant();
        var provenance = packet.Provenance;
        IdempotencyKey = string.Join(":", packet.ObservedRun.Repository, packet.ObservedRun.RunId,
            packet.ObservedRun.RunAttempt, provenance.WorkflowRef, provenance.WorkflowSha,
            provenance.CheckoutRef, provenance.CheckoutSha);
    }

    public string DeliveryId { get; }
    public string IdempotencyKey { get; }
    public CiWorkflowEvidencePacketV3 Packet { get; }
    public string Payload { get; }
    public string PayloadSha256 { get; }
}

public enum CiWorkflowEvidenceEnqueueV3Outcome
{
    Enqueued,
    Duplicate,
    Conflict
}

/// <summary>
/// Separate append-only v3 outbox. V2 storage and its legacy SHA aliases are intentionally untouched.
/// Workflow-definition and tested-checkout identities both participate in the v3 idempotency boundary.
/// </summary>
public sealed class FileCiWorkflowEvidenceOutboxV3
{
    private readonly string _path;

    public FileCiWorkflowEvidenceOutboxV3(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiWorkflowEvidenceEnqueueV3Outcome Enqueue(CiWorkflowEvidenceDeliveryV3 delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var records = ReadRecords();

        var existing = records.FirstOrDefault(record =>
            string.Equals(record.DeliveryId, delivery.DeliveryId, StringComparison.Ordinal));
        if (existing is not null)
            return SamePayload(existing, delivery)
                ? CiWorkflowEvidenceEnqueueV3Outcome.Duplicate
                : CiWorkflowEvidenceEnqueueV3Outcome.Conflict;

        existing = records.FirstOrDefault(record =>
            string.Equals(record.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
            return SamePayload(existing, delivery)
                ? CiWorkflowEvidenceEnqueueV3Outcome.Duplicate
                : CiWorkflowEvidenceEnqueueV3Outcome.Conflict;

        var record = new OutboxRecord(delivery.DeliveryId, delivery.IdempotencyKey,
            delivery.Packet.ObservedRun.Repository, delivery.Packet.ObservedRun.RunId,
            delivery.Packet.ObservedRun.RunAttempt, delivery.Payload, delivery.PayloadSha256);
        Append(JsonSerializer.Serialize(record));
        return CiWorkflowEvidenceEnqueueV3Outcome.Enqueued;
    }

    /// <summary>Reloads pending packets with host trust deliberately unset; callers must re-evaluate it.</summary>
    public IReadOnlyList<CiWorkflowEvidenceDeliveryV3> ReadPending(
        IReadOnlySet<string>? acknowledgedDeliveryIds = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var pending = ReadRecords()
            .Where(record => acknowledgedDeliveryIds is null ||
                !acknowledgedDeliveryIds.Contains(record.DeliveryId))
            .Select(record =>
            {
                var packet = CiWorkflowEvidencePacketV3.FromJson(
                    record.Payload, record.Repository, record.RunId, record.RunAttempt,
                    record.PayloadSha256, workflowDefinitionTrusted: false);
                var delivery = new CiWorkflowEvidenceDeliveryV3(record.DeliveryId, packet);
                if (!string.Equals(delivery.IdempotencyKey, record.IdempotencyKey, StringComparison.Ordinal) ||
                    !string.Equals(delivery.PayloadSha256, record.PayloadSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("V3 outbox record integrity or identity validation failed.");
                return delivery;
            }).ToArray();
        return Array.AsReadOnly(pending);
    }

    private bool SamePayload(OutboxRecord record, CiWorkflowEvidenceDeliveryV3 delivery) =>
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
            throw new InvalidDataException("V3 evidence outbox exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("V3 evidence outbox ends with an incomplete record; refusing to discard evidence.");

        var records = new List<OutboxRecord>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var record = JsonSerializer.Deserialize<OutboxRecord>(line)
                ?? throw new InvalidDataException("V3 evidence outbox contains an empty record.");
            if (string.IsNullOrWhiteSpace(record.DeliveryId) ||
                string.IsNullOrWhiteSpace(record.IdempotencyKey) ||
                !RepositoryIdentity.IsValid(record.Repository) ||
                record.RunId <= 0 || record.RunAttempt <= 0 ||
                string.IsNullOrWhiteSpace(record.Payload) ||
                record.PayloadSha256.Length != 64 || !record.PayloadSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("V3 evidence outbox contains an invalid record.");
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Payload)));
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(record.PayloadSha256)))
                throw new InvalidDataException("V3 evidence outbox payload digest is invalid.");
            records.Add(record);
        }

        if (records.Select(record => record.DeliveryId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            records.Select(record => record.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("V3 evidence outbox contains duplicate delivery or attempt identities.");
        return records.ToArray();
    }

    private sealed record OutboxRecord(
        string DeliveryId,
        string IdempotencyKey,
        string Repository,
        long RunId,
        int RunAttempt,
        string Payload,
        string PayloadSha256);
}
