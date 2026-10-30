using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Stable, content-addressed delivery identity for one exact workflow run attempt.</summary>
public sealed record CiWorkflowEvidenceDelivery
{
    public CiWorkflowEvidenceDelivery(string deliveryId, CiWorkflowEvidencePacket packet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentNullException.ThrowIfNull(packet);
        if (deliveryId.Length > 200 || deliveryId.Any(char.IsControl))
            throw new ArgumentException("Delivery ID is invalid.", nameof(deliveryId));
        DeliveryId = deliveryId;
        Packet = packet;
        Payload = packet.ToJson(indented: false);
        PayloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload))).ToLowerInvariant();
        IdempotencyKey = $"{packet.Repository}:{packet.RunId}:{packet.RunAttempt}:{packet.HeadSha}";
    }

    public string DeliveryId { get; }
    public string IdempotencyKey { get; }
    public CiWorkflowEvidencePacket Packet { get; }
    public string Payload { get; }
    public string PayloadSha256 { get; }
}

/// <summary>Result of an idempotent outbox enqueue operation.</summary>
public enum CiWorkflowEvidenceEnqueueOutcome
{
    Enqueued,
    Duplicate,
    Conflict
}

/// <summary>
/// Durable append-only outbox for CI evidence packets. Pending entries are replayable after process
/// restart; enqueue retries are idempotent, and reusing a delivery ID for different evidence conflicts.
/// Acknowledgement is intentionally external: this store never assumes a remote consumer accepted data.
/// </summary>
public sealed class FileCiWorkflowEvidenceOutbox
{
    private readonly string _path;

    public FileCiWorkflowEvidenceOutbox(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>Persists a packet once; an identical retry is a duplicate and conflicting reuse is rejected.</summary>
    public CiWorkflowEvidenceEnqueueOutcome Enqueue(CiWorkflowEvidenceDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);

        var records = ReadRecords();
        var existingByDeliveryId = records.FirstOrDefault(record =>
            string.Equals(record.DeliveryId, delivery.DeliveryId, StringComparison.Ordinal));
        if (existingByDeliveryId is not null)
            return string.Equals(existingByDeliveryId.PayloadSha256, delivery.PayloadSha256, StringComparison.Ordinal)
                ? CiWorkflowEvidenceEnqueueOutcome.Duplicate
                : CiWorkflowEvidenceEnqueueOutcome.Conflict;

        // The run-attempt key is the real idempotency boundary. A caller cannot enqueue
        // the same attempt twice merely by inventing a second delivery ID.
        var existingByAttempt = records.FirstOrDefault(record =>
            string.Equals(record.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal));
        if (existingByAttempt is not null)
            return string.Equals(existingByAttempt.PayloadSha256, delivery.PayloadSha256, StringComparison.Ordinal)
                ? CiWorkflowEvidenceEnqueueOutcome.Duplicate
                : CiWorkflowEvidenceEnqueueOutcome.Conflict;

        var line = JsonSerializer.Serialize(new OutboxRecord(delivery.DeliveryId,
            delivery.IdempotencyKey, delivery.Payload, delivery.PayloadSha256));
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
        return CiWorkflowEvidenceEnqueueOutcome.Enqueued;
    }

    /// <summary>Returns every durable packet not yet acknowledged by the caller.</summary>
    public IReadOnlyList<CiWorkflowEvidenceDelivery> ReadPending(
        IReadOnlySet<string>? acknowledgedDeliveryIds = null)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var records = ReadRecords();
        var pending = records
            .Where(record => acknowledgedDeliveryIds is null ||
                !acknowledgedDeliveryIds.Contains(record.DeliveryId))
            .Select(record =>
            {
                var packet = CiWorkflowEvidencePacket.FromJson(record.Payload);
                var delivery = new CiWorkflowEvidenceDelivery(record.DeliveryId, packet);
                if (!string.Equals(delivery.PayloadSha256, record.PayloadSha256, StringComparison.Ordinal) ||
                    !string.Equals(delivery.IdempotencyKey, record.IdempotencyKey, StringComparison.Ordinal))
                    throw new InvalidDataException("Outbox record integrity or identity validation failed.");
                return delivery;
            })
            .ToArray();
        return Array.AsReadOnly(pending);
    }

    private OutboxRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<OutboxRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<OutboxRecord>();
        if (input.Length > int.MaxValue)
            throw new InvalidDataException("CI evidence outbox exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("CI evidence outbox ends with an incomplete record; refusing to discard evidence.");
        var records = new List<OutboxRecord>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var record = JsonSerializer.Deserialize<OutboxRecord>(line)
                ?? throw new InvalidDataException("CI evidence outbox contains an empty record.");
            if (string.IsNullOrWhiteSpace(record.DeliveryId) ||
                string.IsNullOrWhiteSpace(record.IdempotencyKey) ||
                string.IsNullOrWhiteSpace(record.Payload) ||
                string.IsNullOrWhiteSpace(record.PayloadSha256) ||
                record.PayloadSha256.Length != 64 ||
                !record.PayloadSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("CI evidence outbox contains an invalid record.");
            records.Add(record);
        }
        if (records.Select(record => record.DeliveryId).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("CI evidence outbox contains duplicate delivery identities.");
        return records.ToArray();
    }

    private sealed record OutboxRecord(
        string DeliveryId,
        string IdempotencyKey,
        string Payload,
        string PayloadSha256);
}
