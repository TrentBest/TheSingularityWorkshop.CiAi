using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheSingularityWorkshop.CiAi;

public enum CiWorkflowEvidenceReceiptV3Outcome
{
    Accepted,
    Duplicate,
    Rejected
}

/// <summary>A remote acknowledgement bound to the exact serialized v3 delivery.</summary>
public sealed record CiWorkflowEvidenceRemoteReceiptV3
{
    public const int CurrentContractVersion = 1;

    public CiWorkflowEvidenceRemoteReceiptV3(
        int contractVersion,
        string remoteReceiptId,
        string deliveryId,
        string idempotencyKey,
        string payloadSha256,
        CiWorkflowEvidenceReceiptV3Outcome outcome,
        DateTimeOffset receivedAt,
        string? rejectionCode = null)
    {
        if (contractVersion != CurrentContractVersion)
            throw new ArgumentOutOfRangeException(nameof(contractVersion));
        RequireText(remoteReceiptId, nameof(remoteReceiptId), 200);
        RequireText(deliveryId, nameof(deliveryId), 200);
        RequireText(idempotencyKey, nameof(idempotencyKey), 71);
        if (idempotencyKey.Length != 71 ||
            !idempotencyKey.StartsWith("sha256:", StringComparison.Ordinal) ||
            !idempotencyKey.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new ArgumentException("Idempotency key must use the bounded sha256:<hex> v3 format.", nameof(idempotencyKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadSha256);
        if (payloadSha256.Length != 64 || !payloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Payload digest must be a SHA-256 hexadecimal value.", nameof(payloadSha256));
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (outcome == CiWorkflowEvidenceReceiptV3Outcome.Rejected)
            RequireText(rejectionCode ?? string.Empty, nameof(rejectionCode), 200);
        else if (rejectionCode is not null)
            throw new ArgumentException("Accepted and duplicate receipts cannot carry a rejection code.", nameof(rejectionCode));

        ContractVersion = contractVersion;
        RemoteReceiptId = remoteReceiptId;
        DeliveryId = deliveryId;
        IdempotencyKey = idempotencyKey;
        PayloadSha256 = payloadSha256.ToLowerInvariant();
        Outcome = outcome;
        ReceivedAt = receivedAt;
        RejectionCode = rejectionCode;
    }

    public int ContractVersion { get; }
    public string RemoteReceiptId { get; }
    public string DeliveryId { get; }
    public string IdempotencyKey { get; }
    public string PayloadSha256 { get; }
    public CiWorkflowEvidenceReceiptV3Outcome Outcome { get; }
    public DateTimeOffset ReceivedAt { get; }
    public string? RejectionCode { get; }

    public IReadOnlyList<string> VerifyAgainst(CiWorkflowEvidenceDeliveryV3? delivery)
    {
        if (delivery is null) return new[] { "delivery.required" };
        var errors = new List<string>();
        if (!string.Equals(DeliveryId, delivery.DeliveryId, StringComparison.Ordinal))
            errors.Add("deliveryId.mismatch");
        if (!string.Equals(IdempotencyKey, delivery.IdempotencyKey, StringComparison.Ordinal))
            errors.Add("idempotencyKey.mismatch");
        if (!string.Equals(PayloadSha256, delivery.PayloadSha256, StringComparison.Ordinal))
            errors.Add("payloadSha256.mismatch");
        return Array.AsReadOnly(errors.ToArray());
    }

    private static void RequireText(string value, string name, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maxLength} characters and contain no control characters.", name);
    }
}

public enum CiWorkflowEvidenceReceiptV3RecordOutcome
{
    Recorded,
    Duplicate,
    Conflict
}

/// <summary>
/// Separate durable v3 receipt ledger. Only exact Accepted or Duplicate receipts suppress replay;
/// Rejected receipts remain durable but do not acknowledge the delivery.
/// </summary>
public sealed class FileCiWorkflowEvidenceReceiptLedgerV3
{
    private readonly string _path;

    public FileCiWorkflowEvidenceReceiptLedgerV3(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiWorkflowEvidenceReceiptV3RecordOutcome Record(
        CiWorkflowEvidenceRemoteReceiptV3 receipt,
        CiWorkflowEvidenceDeliveryV3 delivery)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(delivery);
        var errors = receipt.VerifyAgainst(delivery);
        if (errors.Count != 0)
            throw new ArgumentException("Receipt does not match the exact v3 delivery: " +
                string.Join(", ", errors), nameof(receipt));

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = AcquireFileLock();
        var records = ReadRecords();
        var payload = JsonSerializer.Serialize(receipt, Options());
        var existing = records.FirstOrDefault(item =>
            string.Equals(item.RemoteReceiptId, receipt.RemoteReceiptId, StringComparison.Ordinal));
        if (existing is not null)
            return string.Equals(existing.ReceiptJson, payload, StringComparison.Ordinal)
                ? CiWorkflowEvidenceReceiptV3RecordOutcome.Duplicate
                : CiWorkflowEvidenceReceiptV3RecordOutcome.Conflict;

        // A delivery can have only one terminal acceptance identity. A rejection may be
        // followed by a later accepted receipt, but conflicting accepted receipts are rejected.
        var accepted = records.FirstOrDefault(item =>
            string.Equals(item.DeliveryId, receipt.DeliveryId, StringComparison.Ordinal) &&
            item.Outcome is CiWorkflowEvidenceReceiptV3Outcome.Accepted or CiWorkflowEvidenceReceiptV3Outcome.Duplicate);
        if (accepted is not null)
            return string.Equals(accepted.ReceiptJson, payload, StringComparison.Ordinal)
                ? CiWorkflowEvidenceReceiptV3RecordOutcome.Duplicate
                : CiWorkflowEvidenceReceiptV3RecordOutcome.Conflict;

        var record = new ReceiptRecord(receipt.RemoteReceiptId, receipt.DeliveryId,
            receipt.Outcome, payload);
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n"));
        output.Flush(flushToDisk: true);
        return CiWorkflowEvidenceReceiptV3RecordOutcome.Recorded;
    }

    public IReadOnlySet<string> ReadAcknowledgedDeliveryIds()
    {
        using var fileLock = AcquireFileLock();
        return new HashSet<string>(ReadRecords()
            .Where(item => item.Outcome is CiWorkflowEvidenceReceiptV3Outcome.Accepted or CiWorkflowEvidenceReceiptV3Outcome.Duplicate)
            .Select(item => item.DeliveryId), StringComparer.Ordinal);
    }

    public IReadOnlyList<CiWorkflowEvidenceRemoteReceiptV3> ReadReceipts()
    {
        using var fileLock = AcquireFileLock();
        return Array.AsReadOnly(ReadRecords().Select(item =>
            JsonSerializer.Deserialize<CiWorkflowEvidenceRemoteReceiptV3>(item.ReceiptJson, Options())
            ?? throw new InvalidDataException("V3 receipt ledger contains an empty receipt.")).ToArray());
    }

    private FileStream AcquireFileLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private ReceiptRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<ReceiptRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<ReceiptRecord>();
        if (input.Length > int.MaxValue)
            throw new InvalidDataException("V3 receipt ledger exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("V3 receipt ledger ends with an incomplete record; refusing to discard receipt history.");

        var records = new List<ReceiptRecord>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var item = JsonSerializer.Deserialize<ReceiptRecord>(line)
                ?? throw new InvalidDataException("V3 receipt ledger contains an empty record.");
            var receipt = JsonSerializer.Deserialize<CiWorkflowEvidenceRemoteReceiptV3>(item.ReceiptJson, Options())
                ?? throw new InvalidDataException("V3 receipt ledger contains an empty receipt.");
            if (!Enum.IsDefined(item.Outcome) ||
                !string.Equals(item.RemoteReceiptId, receipt.RemoteReceiptId, StringComparison.Ordinal) ||
                !string.Equals(item.DeliveryId, receipt.DeliveryId, StringComparison.Ordinal) ||
                item.Outcome != receipt.Outcome)
                throw new InvalidDataException("V3 receipt ledger identity does not match its receipt payload.");
            records.Add(item);
        }
        if (records.Select(item => item.RemoteReceiptId).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("V3 receipt ledger contains duplicate receipt identities.");
        return records.ToArray();
    }

    private static JsonSerializerOptions Options() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed record ReceiptRecord(
        string RemoteReceiptId,
        string DeliveryId,
        CiWorkflowEvidenceReceiptV3Outcome Outcome,
        string ReceiptJson);
}
