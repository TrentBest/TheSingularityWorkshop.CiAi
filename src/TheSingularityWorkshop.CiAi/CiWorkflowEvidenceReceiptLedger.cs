using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Outcome returned by a remote evidence consumer after it durably processes a delivery.</summary>
public enum CiWorkflowEvidenceReceiptOutcome
{
    Accepted,
    Duplicate,
    Rejected
}

/// <summary>
/// A remote receipt bound to one exact evidence delivery. A receipt is transport evidence only;
/// it never grants repository, merge, release, or publication authority.
/// </summary>
public sealed record CiWorkflowEvidenceRemoteReceipt
{
    public const int CurrentContractVersion = 1;

    public CiWorkflowEvidenceRemoteReceipt(
        int contractVersion,
        string remoteReceiptId,
        string deliveryId,
        string idempotencyKey,
        string payloadSha256,
        CiWorkflowEvidenceReceiptOutcome outcome,
        DateTimeOffset receivedAt,
        string? rejectionCode = null)
    {
        if (contractVersion != CurrentContractVersion)
            throw new ArgumentOutOfRangeException(nameof(contractVersion), "Unsupported receipt contract version.");
        RequireText(remoteReceiptId, nameof(remoteReceiptId), 200);
        RequireText(deliveryId, nameof(deliveryId), 200);
        RequireText(idempotencyKey, nameof(idempotencyKey), 500);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadSha256);
        if (payloadSha256.Length != 64 || !payloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Payload digest must be a 64-character SHA-256 value.", nameof(payloadSha256));
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (outcome == CiWorkflowEvidenceReceiptOutcome.Rejected)
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
    public CiWorkflowEvidenceReceiptOutcome Outcome { get; }
    public DateTimeOffset ReceivedAt { get; }
    public string? RejectionCode { get; }

    /// <summary>Reconciles the remote receipt with the exact locally persisted delivery.</summary>
    public IReadOnlyList<string> VerifyAgainst(CiWorkflowEvidenceDelivery? delivery)
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

    private static void RequireText(string value, string parameterName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maxLength} characters and contain no control characters.", parameterName);
    }
}

/// <summary>
/// Durable append-only record of remote delivery receipts. Only an exact Accepted or Duplicate
/// receipt marks a delivery acknowledged; Rejected receipts remain visible and retryable.
/// </summary>
public sealed class FileCiWorkflowEvidenceReceiptLedger
{
    private readonly string _path;

    public FileCiWorkflowEvidenceReceiptLedger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>Persists a receipt after reconciling it against the exact pending delivery.</summary>
    public CiWorkflowEvidenceReceiptRecordOutcome Record(
        CiWorkflowEvidenceRemoteReceipt receipt,
        CiWorkflowEvidenceDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(delivery);
        var errors = receipt.VerifyAgainst(delivery);
        if (errors.Count != 0)
            throw new ArgumentException("Remote receipt does not match the exact evidence delivery: " +
                string.Join(", ", errors), nameof(receipt));

        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var records = ReadRecords();
        var existing = records.FirstOrDefault(item =>
            string.Equals(item.RemoteReceiptId, receipt.RemoteReceiptId, StringComparison.Ordinal));
        var payload = JsonSerializer.Serialize(receipt, JsonOptions());
        if (existing is not null)
            return string.Equals(existing.ReceiptJson, payload, StringComparison.Ordinal)
                ? CiWorkflowEvidenceReceiptRecordOutcome.Duplicate
                : CiWorkflowEvidenceReceiptRecordOutcome.Conflict;

        var line = JsonSerializer.Serialize(new ReceiptRecord(receipt.RemoteReceiptId,
            receipt.DeliveryId, receipt.Outcome, payload));
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(Encoding.UTF8.GetBytes(line + "\n"));
        output.Flush(flushToDisk: true);
        return CiWorkflowEvidenceReceiptRecordOutcome.Recorded;
    }

    /// <summary>Returns the exact delivery IDs with a durable accepted or duplicate receipt.</summary>
    public IReadOnlySet<string> ReadAcknowledgedDeliveryIds()
    {
        var records = ReadRecords();
        return new HashSet<string>(records
            .Where(item => item.Outcome is CiWorkflowEvidenceReceiptOutcome.Accepted or CiWorkflowEvidenceReceiptOutcome.Duplicate)
            .Select(item => item.DeliveryId), StringComparer.Ordinal);
    }

    /// <summary>Returns all durable receipts, including policy rejections.</summary>
    public IReadOnlyList<CiWorkflowEvidenceRemoteReceipt> ReadReceipts() =>
        Array.AsReadOnly(ReadRecords().Select(item =>
            JsonSerializer.Deserialize<CiWorkflowEvidenceRemoteReceipt>(item.ReceiptJson, JsonOptions())
            ?? throw new InvalidDataException("Receipt ledger contains an empty receipt.")).ToArray());

    private ReceiptRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<ReceiptRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<ReceiptRecord>();
        if (input.Length > int.MaxValue)
            throw new InvalidDataException("Receipt ledger exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Receipt ledger ends with an incomplete record; refusing to discard receipt history.");

        var records = new List<ReceiptRecord>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var item = JsonSerializer.Deserialize<ReceiptRecord>(line)
                ?? throw new InvalidDataException("Receipt ledger contains an empty record.");
            var receipt = JsonSerializer.Deserialize<CiWorkflowEvidenceRemoteReceipt>(item.ReceiptJson, JsonOptions())
                ?? throw new InvalidDataException("Receipt ledger contains an empty receipt.");
            if (!string.Equals(item.RemoteReceiptId, receipt.RemoteReceiptId, StringComparison.Ordinal) ||
                !string.Equals(item.DeliveryId, receipt.DeliveryId, StringComparison.Ordinal) ||
                item.Outcome != receipt.Outcome)
                throw new InvalidDataException("Receipt ledger identity does not match its receipt payload.");
            records.Add(item);
        }
        if (records.Select(item => item.RemoteReceiptId).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Receipt ledger contains duplicate remote receipt identities.");
        return records.ToArray();
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed record ReceiptRecord(
        string RemoteReceiptId,
        string DeliveryId,
        CiWorkflowEvidenceReceiptOutcome Outcome,
        string ReceiptJson);
}

public enum CiWorkflowEvidenceReceiptRecordOutcome
{
    Recorded,
    Duplicate,
    Conflict
}
