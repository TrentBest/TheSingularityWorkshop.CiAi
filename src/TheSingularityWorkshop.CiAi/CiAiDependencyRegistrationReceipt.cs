using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A receiver-issued acknowledgement bound to one immutable source delivery and one recipient.
/// An accepted receipt may be created only after the receiver's durable ledger reports Applied or Duplicate.
/// </summary>
public sealed class CiAiDependencyRegistrationReceipt
{
    public const string CurrentSchema = "ciai.dependency-registration-receipt.v1";

    private CiAiDependencyRegistrationReceipt(string deliveryId, string targetRepository,
        string sourceRepository, long sourceRevision, string sourceCommitSha,
        string deliveryPayloadSha256, CiAiDependencyRegistrationReceiptStatus status,
        string outcomeCode)
    {
        DeliveryId = deliveryId;
        TargetRepository = targetRepository;
        SourceRepository = sourceRepository;
        SourceRevision = sourceRevision;
        SourceCommitSha = sourceCommitSha;
        DeliveryPayloadSha256 = deliveryPayloadSha256;
        Status = status;
        OutcomeCode = outcomeCode;
        Validate();
        Payload = Serialize();
        PayloadSha256 = Digest(Payload);
    }

    public string DeliveryId { get; }
    public string TargetRepository { get; }
    public string SourceRepository { get; }
    public long SourceRevision { get; }
    public string SourceCommitSha { get; }
    public string DeliveryPayloadSha256 { get; }
    public CiAiDependencyRegistrationReceiptStatus Status { get; }
    public string OutcomeCode { get; }
    public string Payload { get; }
    public string PayloadSha256 { get; }

    /// <summary>
    /// Creates a receipt from the result returned by the durable receiver ledger.
    /// Transport acceptance or HTTP success is not a ledger outcome and cannot create an accepted receipt.
    /// </summary>
    public static CiAiDependencyRegistrationReceipt FromApplyResult(
        CiAiDependencyRegistrationDelivery delivery, string targetRepository,
        CiAiDependencyRegistrationApplyResult applyResult)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(applyResult);
        if (!delivery.RecipientRepositories.Contains(targetRepository, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Receipt target is not an explicit delivery recipient.", nameof(targetRepository));

        var (status, code) = applyResult.Status switch
        {
            CiAiDependencyRegistrationApplyStatus.Applied =>
                (CiAiDependencyRegistrationReceiptStatus.Accepted, "applied"),
            CiAiDependencyRegistrationApplyStatus.Duplicate =>
                (CiAiDependencyRegistrationReceiptStatus.Accepted, "duplicate"),
            CiAiDependencyRegistrationApplyStatus.Stale =>
                (CiAiDependencyRegistrationReceiptStatus.Rejected, "stale"),
            CiAiDependencyRegistrationApplyStatus.Conflict =>
                (CiAiDependencyRegistrationReceiptStatus.Rejected, "conflict"),
            CiAiDependencyRegistrationApplyStatus.SourceMismatch =>
                (CiAiDependencyRegistrationReceiptStatus.Rejected, "source-mismatch"),
            _ => throw new ArgumentOutOfRangeException(nameof(applyResult))
        };

        return new CiAiDependencyRegistrationReceipt(delivery.DeliveryId, targetRepository,
            delivery.SourceRepository, delivery.SourceRevision, delivery.SourceCommitSha,
            delivery.PayloadSha256, status, code);
    }

    public static CiAiDependencyRegistrationReceipt FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateProperties(root, "schema", "deliveryId", "targetRepository", "sourceRepository",
            "sourceRevision", "sourceCommitSha", "deliveryPayloadSha256", "status", "outcomeCode");
        if (root.GetProperty("schema").GetString() != CurrentSchema)
            throw new JsonException("Dependency registration receipt schema is unsupported.");
        if (!Enum.TryParse<CiAiDependencyRegistrationReceiptStatus>(
                root.GetProperty("status").GetString(), ignoreCase: false, out var status) ||
            !Enum.IsDefined(status))
            throw new JsonException("Dependency registration receipt status is invalid.");

        var receipt = new CiAiDependencyRegistrationReceipt(
            root.GetProperty("deliveryId").GetString()!,
            root.GetProperty("targetRepository").GetString()!,
            root.GetProperty("sourceRepository").GetString()!,
            root.GetProperty("sourceRevision").GetInt64(),
            root.GetProperty("sourceCommitSha").GetString()!,
            root.GetProperty("deliveryPayloadSha256").GetString()!,
            status, root.GetProperty("outcomeCode").GetString()!);
        return receipt;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeliveryId) || !DeliveryId.StartsWith("depdelivery-", StringComparison.Ordinal))
            throw new ArgumentException("Receipt delivery identity is invalid.");
        if (!RepositoryIdentity.IsValid(TargetRepository) || !RepositoryIdentity.IsValid(SourceRepository) ||
            string.Equals(TargetRepository, SourceRepository, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Receipt source and target repositories must be distinct canonical identities.");
        if (SourceRevision < 1) throw new ArgumentOutOfRangeException(nameof(SourceRevision));
        if (SourceCommitSha is not { Length: 40 } || !SourceCommitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Receipt source commit must be a full SHA.");
        if (DeliveryPayloadSha256 is not { Length: 64 } || !DeliveryPayloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Receipt delivery digest must be a full SHA-256 value.");
        var expected = Status switch
        {
            CiAiDependencyRegistrationReceiptStatus.Accepted when OutcomeCode is "applied" or "duplicate" => true,
            CiAiDependencyRegistrationReceiptStatus.Rejected when OutcomeCode is "stale" or "conflict" or "source-mismatch" => true,
            _ => false
        };
        if (!expected) throw new ArgumentException("Receipt status and outcome code are inconsistent.");
    }

    private string Serialize() => JsonSerializer.Serialize(new
    {
        schema = CurrentSchema,
        deliveryId = DeliveryId,
        targetRepository = TargetRepository,
        sourceRepository = SourceRepository,
        sourceRevision = SourceRevision,
        sourceCommitSha = SourceCommitSha,
        deliveryPayloadSha256 = DeliveryPayloadSha256,
        status = Status.ToString(),
        outcomeCode = OutcomeCode
    }, new JsonSerializerOptions { WriteIndented = true }) + "\n";

    private static void ValidateProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Dependency registration receipt must be an object.");
        var allowed = expected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                throw new JsonException("Dependency registration receipt has an unknown or duplicate property.");
        if (seen.Count != allowed.Count)
            throw new JsonException("Dependency registration receipt is missing a required property.");
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public enum CiAiDependencyRegistrationReceiptStatus
{
    Accepted,
    Rejected
}

public enum CiAiDependencyRegistrationReceiptStoreOutcome
{
    Recorded,
    Duplicate,
    Conflict
}

/// <summary>
/// Durable append-only receipt journal. It records receiver statements, not proof that a receipt's
/// author is authentic; callers must authenticate the receiving repository before trusting a receipt.
/// </summary>
public sealed class FileCiAiDependencyRegistrationReceiptStore
{
    private readonly string _path;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public FileCiAiDependencyRegistrationReceiptStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiAiDependencyRegistrationReceiptStoreOutcome Record(CiAiDependencyRegistrationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        using var fileLock = AcquireLock();
        var records = ReadAllCore();
        var existing = records.FirstOrDefault(item =>
            item.DeliveryId == receipt.DeliveryId &&
            string.Equals(item.TargetRepository, receipt.TargetRepository, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.PayloadSha256 == receipt.PayloadSha256
                ? CiAiDependencyRegistrationReceiptStoreOutcome.Duplicate
                : CiAiDependencyRegistrationReceiptStoreOutcome.Conflict;

        var record = JsonSerializer.Serialize(new ReceiptRecord(receipt.DeliveryId,
            receipt.TargetRepository, receipt.Payload, receipt.PayloadSha256)) + "\n";
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(Encoding.UTF8.GetBytes(record));
        output.Flush(flushToDisk: true);
        return CiAiDependencyRegistrationReceiptStoreOutcome.Recorded;
    }

    public IReadOnlyList<CiAiDependencyRegistrationReceipt> ReadAll()
    {
        using var fileLock = AcquireLock();
        return Array.AsReadOnly(ReadAllCore().OrderBy(item => item.DeliveryId, StringComparer.Ordinal)
            .ThenBy(item => item.TargetRepository, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private ReceiptRecord[] ReadAllCore()
    {
        if (!File.Exists(_path)) return Array.Empty<ReceiptRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<ReceiptRecord>();
        if (input.Length > int.MaxValue) throw new InvalidDataException("Receipt journal exceeds the supported scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n') throw new InvalidDataException("Receipt journal ends with an incomplete record.");
        var lines = StrictUtf8.GetString(bytes).Split('\n');
        var records = new List<ReceiptRecord>(lines.Length - 1);
        foreach (var line in lines[..^1])
        {
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Receipt journal contains an empty record.");
            var record = JsonSerializer.Deserialize<ReceiptRecord>(line)
                ?? throw new InvalidDataException("Receipt journal record is empty.");
            var receipt = CiAiDependencyRegistrationReceipt.FromJson(record.Payload);
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Payload))).ToLowerInvariant();
            if (receipt.DeliveryId != record.DeliveryId ||
                !string.Equals(receipt.TargetRepository, record.TargetRepository, StringComparison.OrdinalIgnoreCase) ||
                receipt.Payload != record.Payload || digest != record.PayloadSha256)
                throw new InvalidDataException("Receipt journal identity or payload integrity check failed.");
            records.Add(record);
        }
        if (records.Select(item => item.DeliveryId + "|" + item.TargetRepository.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Receipt journal contains duplicate delivery/target identities.");
        return records.ToArray();
    }

    private sealed record ReceiptRecord(string DeliveryId, string TargetRepository,
        string Payload, string PayloadSha256);
}
