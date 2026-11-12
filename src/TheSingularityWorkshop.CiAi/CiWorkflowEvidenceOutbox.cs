using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Stable delivery identity for one exact v1 workflow run attempt and checkout.</summary>
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
        var provenance = packet.Provenance;
        // Hash a structured identity rather than delimiter-joining untrusted refs.
        // This keeps the key bounded and prevents ambiguous tuples if a workflow path
        // contains punctuation that would otherwise collide with the separator.
        var identity = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = CiWorkflowEvidencePacket.CurrentSchema,
            repository = packet.ObservedRun.Repository,
            runId = packet.ObservedRun.RunId,
            runAttempt = packet.ObservedRun.RunAttempt,
            workflowRef = provenance.WorkflowRef,
            workflowSha = provenance.WorkflowSha,
            checkoutRef = provenance.CheckoutRef,
            checkoutSha = provenance.CheckoutSha
        });
        IdempotencyKey = "sha256:" +
            Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
    }

    public string DeliveryId { get; }
    public string IdempotencyKey { get; }
    public CiWorkflowEvidencePacket Packet { get; }
    public string Payload { get; }
    public string PayloadSha256 { get; }
}

public enum CiWorkflowEvidenceEnqueueOutcome
{
    Enqueued,
    Duplicate,
    Conflict
}

/// <summary>
/// The single append-only outbox for canonical v1 evidence packets. Reload verifies bytes and identity but never restores host trust.
/// Workflow-definition and tested-checkout identities both participate in the v1 idempotency boundary.
/// </summary>
public sealed class FileCiWorkflowEvidenceOutbox
{
    private readonly string _path;

    public FileCiWorkflowEvidenceOutbox(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiWorkflowEvidenceEnqueueOutcome Enqueue(CiWorkflowEvidenceDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var records = ReadRecords();

        var existing = records.FirstOrDefault(record =>
            string.Equals(record.DeliveryId, delivery.DeliveryId, StringComparison.Ordinal));
        if (existing is not null)
            return SamePayload(existing, delivery)
                ? CiWorkflowEvidenceEnqueueOutcome.Duplicate
                : CiWorkflowEvidenceEnqueueOutcome.Conflict;

        existing = records.FirstOrDefault(record =>
            string.Equals(record.IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
            return SamePayload(existing, delivery)
                ? CiWorkflowEvidenceEnqueueOutcome.Duplicate
                : CiWorkflowEvidenceEnqueueOutcome.Conflict;

        var record = new OutboxRecord(delivery.DeliveryId, delivery.IdempotencyKey,
            delivery.Packet.ObservedRun.Repository, delivery.Packet.ObservedRun.RunId,
            delivery.Packet.ObservedRun.RunAttempt, delivery.Payload, delivery.PayloadSha256);
        Append(JsonSerializer.Serialize(record));
        return CiWorkflowEvidenceEnqueueOutcome.Enqueued;
    }

    /// <summary>Reloads pending packets with host trust deliberately unset; callers must re-evaluate it.</summary>
    public IReadOnlyList<CiWorkflowEvidenceDelivery> ReadPending(
        IReadOnlySet<string>? acknowledgedDeliveryIds = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var pending = ReadRecords()
            .Where(record => acknowledgedDeliveryIds is null ||
                !acknowledgedDeliveryIds.Contains(record.DeliveryId))
            .Select(record =>
            {
                var packet = CiWorkflowEvidencePacket.FromJson(
                    record.Payload, record.Repository, record.RunId, record.RunAttempt,
                    record.PayloadSha256, workflowDefinitionTrusted: false);
                var delivery = new CiWorkflowEvidenceDelivery(record.DeliveryId, packet);
                if (!string.Equals(delivery.IdempotencyKey, record.IdempotencyKey, StringComparison.Ordinal) ||
                    !string.Equals(delivery.PayloadSha256, record.PayloadSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("outbox record integrity or identity validation failed.");
                return delivery;
            }).ToArray();
        return Array.AsReadOnly(pending);
    }

    private bool SamePayload(OutboxRecord record, CiWorkflowEvidenceDelivery delivery) =>
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
            throw new InvalidDataException("Evidence outbox exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Evidence outbox ends with an incomplete record; refusing to discard evidence.");

        var records = new List<OutboxRecord>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var record = JsonSerializer.Deserialize<OutboxRecord>(line)
                ?? throw new InvalidDataException("Evidence outbox contains an empty record.");
            if (string.IsNullOrWhiteSpace(record.DeliveryId) ||
                !IsValidIdempotencyKey(record.IdempotencyKey) ||
                !RepositoryIdentity.IsValid(record.Repository) ||
                record.RunId <= 0 || record.RunAttempt <= 0 ||
                string.IsNullOrWhiteSpace(record.Payload) ||
                string.IsNullOrWhiteSpace(record.PayloadSha256) ||
                record.PayloadSha256.Length != 64 || !record.PayloadSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Evidence outbox contains an invalid record.");
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Payload)));
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(record.PayloadSha256)))
                throw new InvalidDataException("Evidence outbox payload digest is invalid.");
            records.Add(record);
        }

        if (records.Select(record => record.DeliveryId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            records.Select(record => record.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Evidence outbox contains duplicate delivery or attempt identities.");
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
        string Payload,
        string PayloadSha256);
}
