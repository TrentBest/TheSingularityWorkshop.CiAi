using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// The dispatch identity copied from the host-persisted request mapping. These values are
/// protocol identity, not locally regenerated packet hashes: Execution validates all three
/// against the exact mapping created before workflow_dispatch.
/// </summary>
public sealed record CiAiArtifactDeliveryIdentity(
    string RequestId,
    string OperationId,
    string IdempotencyKey)
{
    public CiAiArtifactDeliveryIdentity Validate()
    {
        static bool Clean(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
            !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));
        if (!Clean(RequestId) || !Clean(OperationId) || !Clean(IdempotencyKey))
            throw new ArgumentException("Dispatch request, operation, and idempotency identities must be bounded and non-empty.");
        return this;
    }
}

/// <summary>One exact v1 packet paired with the original host-approved dispatch identity.</summary>
public sealed record CiWorkflowEvidenceDelivery : ICiWorkflowEvidenceDelivery
{
    public CiWorkflowEvidenceDelivery(
        CiAiArtifactDeliveryIdentity identity,
        CiWorkflowEvidencePacket packet)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(packet);
        identity.Validate();
        DeliveryId = identity.RequestId;
        OperationId = identity.OperationId;
        IdempotencyKey = identity.IdempotencyKey;
        Packet = packet;
        Payload = packet.ToJson(indented: false);
        PayloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload))).ToLowerInvariant();
    }

    internal CiWorkflowEvidenceDelivery(
        string deliveryId,
        string operationId,
        string idempotencyKey,
        CiWorkflowEvidencePacket packet)
        : this(new CiAiArtifactDeliveryIdentity(deliveryId, operationId, idempotencyKey), packet)
    {
    }

    public string DeliveryId { get; }
    public string OperationId { get; }
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
/// The single append-only outbox for canonical v1 evidence packets. It preserves the exact
/// request, operation, and idempotency identities supplied by the host's persisted dispatch
/// mapping. Packet bytes have a separate SHA-256 digest; reload never restores host trust.
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

        var record = new OutboxRecord(delivery.DeliveryId, delivery.OperationId, delivery.IdempotencyKey,
            delivery.Packet.ObservedRun.Repository, delivery.Packet.ObservedRun.RunId,
            delivery.Packet.ObservedRun.RunAttempt,
            delivery.Packet.RequiredChecks.Select(check =>
                new CiWorkflowRequiredCheckBinding(check.Key, check.JobName)).ToArray(),
            delivery.Payload, delivery.PayloadSha256);
        Append(JsonSerializer.Serialize(record));
        return CiWorkflowEvidenceEnqueueOutcome.Enqueued;
    }

    /// <summary>Reloads pending packets with host trust deliberately unset; callers must re-evaluate it.</summary>
    public IReadOnlyList<CiWorkflowEvidenceDelivery> ReadPending(
        IEnumerable<CiWorkflowRequiredCheckBinding> expectedRequiredChecks,
        IReadOnlySet<string>? acknowledgedDeliveryIds = null)
    {
        ArgumentNullException.ThrowIfNull(expectedRequiredChecks);
        var trustedRequiredChecks = expectedRequiredChecks.ToArray();
        if (trustedRequiredChecks.Length == 0)
            throw new ArgumentException("The host must supply its registered required-check set when reloading evidence.", nameof(expectedRequiredChecks));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var pending = ReadRecords()
            .Where(record => acknowledgedDeliveryIds is null ||
                !acknowledgedDeliveryIds.Contains(record.DeliveryId))
            .Select(record =>
            {
                if (!record.RequiredChecks.OrderBy(check => check.Key, StringComparer.Ordinal)
                    .SequenceEqual(trustedRequiredChecks.OrderBy(check => check.Key, StringComparer.Ordinal)))
                    throw new InvalidDataException("Evidence outbox required-check set does not match current host registration.");
                var packet = CiWorkflowEvidencePacket.FromJson(
                    record.Payload, record.Repository, record.RunId, record.RunAttempt,
                    record.PayloadSha256, trustedRequiredChecks, workflowDefinitionTrusted: false);
                var delivery = new CiWorkflowEvidenceDelivery(
                    record.DeliveryId, record.OperationId, record.IdempotencyKey, packet);
                if (!string.Equals(delivery.IdempotencyKey, record.IdempotencyKey, StringComparison.Ordinal) ||
                    !string.Equals(delivery.PayloadSha256, record.PayloadSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("outbox record integrity or identity validation failed.");
                return delivery;
            }).ToArray();
        return Array.AsReadOnly(pending);
    }

    private bool SamePayload(OutboxRecord record, CiWorkflowEvidenceDelivery delivery) =>
        string.Equals(record.OperationId, delivery.OperationId, StringComparison.Ordinal) &&
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
            if (!IsValidDispatchIdentity(record.DeliveryId) ||
                !IsValidDispatchIdentity(record.OperationId) ||
                !IsValidIdempotencyKey(record.IdempotencyKey) ||
                !RepositoryIdentity.IsValid(record.Repository) ||
                record.RunId <= 0 || record.RunAttempt <= 0 ||
                record.RequiredChecks is null || record.RequiredChecks.Length == 0 ||
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

    private static bool IsValidDispatchIdentity(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));

    private static bool IsValidIdempotencyKey(string? value) => IsValidDispatchIdentity(value);

    private sealed record OutboxRecord(
        string DeliveryId,
        string OperationId,
        string IdempotencyKey,
        string Repository,
        long RunId,
        int RunAttempt,
        CiWorkflowRequiredCheckBinding[] RequiredChecks,
        string Payload,
        string PayloadSha256);
}
