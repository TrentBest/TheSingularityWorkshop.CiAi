using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Immutable, deterministic fan-out envelope for one verified source snapshot.
/// Recipients must come from a trusted repository inventory and include every repository
/// that may need to accept a declaration or a later withdrawal.
/// </summary>
public sealed class CiAiDependencyRegistrationDelivery
{
    public const string CurrentSchema = "ciai.dependency-registration-delivery.v2";

    private CiAiDependencyRegistrationDelivery(CiAiDependencyRegistrationSnapshot snapshot,
        long sourceRevision, int sourceRunAttempt, string evidencePayloadSha256,
        string workflowPath, string sourceRef, string? inventoryProviderIdentity,\n        long? inventoryRevision, string? inventoryEvidencePayloadSha256, IReadOnlyList<string> recipients)
    {
        Snapshot = snapshot;
        SourceRevision = sourceRevision;
        SourceRunAttempt = sourceRunAttempt;
        EvidencePayloadSha256 = evidencePayloadSha256;
        WorkflowPath = workflowPath;
        SourceRef = sourceRef;
        RecipientRepositories = Array.AsReadOnly(recipients.ToArray());
        Validate();
        Payload = Serialize();
        PayloadSha256 = Digest(Payload);
        DeliveryId = CreateDeliveryId(snapshot.SourceRepository, sourceRevision, snapshot.SourceCommitSha);
    }

    public CiAiDependencyRegistrationSnapshot Snapshot { get; }
    public string SourceRepository => Snapshot.SourceRepository;
    public string SourceCommitSha => Snapshot.SourceCommitSha;
    public long SourceRevision { get; }
    public int SourceRunAttempt { get; }
    public string EvidencePayloadSha256 { get; }
    public string WorkflowPath { get; }
    public string SourceRef { get; }
    public IReadOnlyList<string> RecipientRepositories { get; }
    public string DeliveryId { get; }
    public string Payload { get; }
    public string PayloadSha256 { get; }

    public static CiAiDependencyRegistrationDelivery FromVerification(
        CiAiDependencyRegistrationSourceVerification verification,
        IEnumerable<string> recipientRepositories)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(recipientRepositories);
        var recipients = recipientRepositories.ToArray();
        return new CiAiDependencyRegistrationDelivery(verification.Snapshot,
            verification.SourceRevision, verification.SourceRunAttempt,
            verification.EvidencePayloadSha256, verification.WorkflowPath,
            verification.SourceRef, null, null, null, recipients);
    }

 
    /// <summary>
    /// Creates a delivery whose recipient set and inventory evidence are bound to the immutable envelope.
    /// Previous deliveries are unioned so former recipients still receive withdrawals.
    /// </summary>
    public static CiAiDependencyRegistrationDelivery FromTrustedInventory(
        CiAiDependencyRegistrationSourceVerification verification,
        CiAiRepositoryInventorySnapshot inventory,
        IEnumerable<CiAiDependencyRegistrationDelivery> previousDeliveries)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(previousDeliveries);
        var recipients = CiAiDependencyRegistrationFanOutPlanner.CreateRecipients(
            verification.Snapshot.SourceRepository, inventory, previousDeliveries);
        return new CiAiDependencyRegistrationDelivery(verification.Snapshot,
            verification.SourceRevision, verification.SourceRunAttempt,
            verification.EvidencePayloadSha256, verification.WorkflowPath,
            verification.SourceRef, inventory.ProviderIdentity, inventory.Revision,
            inventory.EvidencePayloadSha256, recipients);
    }

    public static CiAiDependencyRegistrationDelivery FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateProperties(root, "schema", "deliveryId", "sourceRepository", "sourceRevision",
            "sourceCommitSha", "sourceRunAttempt", "evidencePayloadSha256", "workflowPath",
            "sourceRef", "inventoryProviderIdentity", "inventoryRevision",\n            "inventoryEvidencePayloadSha256", "snapshotJson", "recipientRepositories");
        if (root.GetProperty("schema").GetString() != CurrentSchema)
            throw new JsonException("Dependency registration delivery schema is unsupported.");

        var snapshot = CiAiDependencyRegistrationSnapshot.FromJson(
            root.GetProperty("snapshotJson").GetString() ?? throw new JsonException("Snapshot JSON is required."));
        var recipientsElement = root.GetProperty("recipientRepositories");
        if (recipientsElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Delivery recipients must be an array.");
        var recipients = recipientsElement.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String ? item.GetString()! :
                throw new JsonException("Delivery recipient must be a string.")).ToArray();

        var delivery = new CiAiDependencyRegistrationDelivery(snapshot,
            root.GetProperty("sourceRevision").GetInt64(),
            root.GetProperty("sourceRunAttempt").GetInt32(),
            root.GetProperty("evidencePayloadSha256").GetString()!,
            root.GetProperty("workflowPath").GetString()!,
            root.GetProperty("sourceRef").GetString()!, recipients);
        if (!string.Equals(delivery.DeliveryId, root.GetProperty("deliveryId").GetString(), StringComparison.Ordinal))
            throw new JsonException("Delivery identity does not match its source revision.");
        if (!string.Equals(delivery.SourceRepository, root.GetProperty("sourceRepository").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(delivery.SourceCommitSha, root.GetProperty("sourceCommitSha").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new JsonException("Delivery envelope identity does not match its snapshot.");
        return delivery;
    }

    private string Serialize()
    {
        var envelope = new
        {
            schema = CurrentSchema,
            deliveryId = CreateDeliveryId(Snapshot.SourceRepository, SourceRevision, Snapshot.SourceCommitSha),
            sourceRepository = Snapshot.SourceRepository,
            sourceRevision = SourceRevision,
            sourceCommitSha = Snapshot.SourceCommitSha,
            sourceRunAttempt = SourceRunAttempt,
            evidencePayloadSha256 = EvidencePayloadSha256,
            workflowPath = WorkflowPath,
            sourceRef = SourceRef,
            snapshotJson = Snapshot.ToJson(),
            recipientRepositories = RecipientRepositories
        };
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private void Validate()
    {
        if (SourceRevision < 1) throw new ArgumentOutOfRangeException(nameof(SourceRevision));
        if (SourceRunAttempt < 1) throw new ArgumentOutOfRangeException(nameof(SourceRunAttempt));
        if (EvidencePayloadSha256 is not { Length: 64 } || !EvidencePayloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Evidence payload digest must be a full SHA-256 value.");
        if (string.IsNullOrWhiteSpace(WorkflowPath) ||
            !WorkflowPath.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(WorkflowPath.EndsWith(".yml", StringComparison.Ordinal) || WorkflowPath.EndsWith(".yaml", StringComparison.Ordinal)) ||
            WorkflowPath.Contains("..", StringComparison.Ordinal) || WorkflowPath.Contains('\\'))
            throw new ArgumentException("Workflow path must identify a safe workflow under .github/workflows.");
        if (string.IsNullOrWhiteSpace(SourceRef) || !SourceRef.StartsWith("refs/heads/", StringComparison.Ordinal))
            throw new ArgumentException("Source ref must be a full branch ref.");
        if (RecipientRepositories.Any(value => !RepositoryIdentity.IsValid(value) ||
            string.Equals(value, SourceRepository, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Delivery recipients must be canonical repositories other than the source.");
        if (RecipientRepositories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != RecipientRepositories.Count)
            throw new ArgumentException("Delivery recipients must be unique.");
        var inventoryFieldsPresent = InventoryProviderIdentity is not null &&
            InventoryRevision is not null && InventoryEvidencePayloadSha256 is not null;
        var inventoryFieldsAbsent = InventoryProviderIdentity is null &&
            InventoryRevision is null && InventoryEvidencePayloadSha256 is null;
        if (!inventoryFieldsPresent && !inventoryFieldsAbsent)
            throw new ArgumentException("Delivery inventory provenance must be either complete or absent.");
        if (inventoryFieldsPresent &&
            (string.IsNullOrWhiteSpace(InventoryProviderIdentity) || InventoryRevision < 1 ||
             InventoryEvidencePayloadSha256 is not { Length: 64 } ||
             !InventoryEvidencePayloadSha256.All(Uri.IsHexDigit)))
            throw new ArgumentException("Delivery inventory provenance is invalid.");
        var requiredTargets = Snapshot.Registrations.Select(item => item.TargetRepository)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (requiredTargets.Any(target => !RecipientRepositories.Contains(target, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Delivery recipients omit a declared dependency target.");
    }

    private static void ValidateProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Dependency registration delivery must be an object.");
        var set = expected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!set.Contains(property.Name) || !seen.Add(property.Name))
                throw new JsonException("Dependency registration delivery has an unknown or duplicate property.");
        }
        if (seen.Count != set.Count)
            throw new JsonException("Dependency registration delivery is missing a required property.");
    }

    internal static string CreateDeliveryId(string repository, long revision, string sha)
    {
        var material = string.Join("|", repository.ToUpperInvariant(),
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture), sha.ToLowerInvariant());
        return "depdelivery-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))
            .AsSpan(0, 16)).ToLowerInvariant();
    }

    private static string Digest(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
}

/// <summary>Append-only, restart-safe outbox for federated dependency registration deliveries.</summary>
public sealed class FileCiAiDependencyRegistrationOutbox : ICiAiDependencyRegistrationOutboxStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _path;

    public FileCiAiDependencyRegistrationOutbox(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiAiDependencyRegistrationOutboxEnqueueOutcome Enqueue(CiAiDependencyRegistrationDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        using var fileLock = AcquireLock();
        var records = ReadRecords();
        var existing = records.FirstOrDefault(item =>
            string.Equals(item.DeliveryId, delivery.DeliveryId, StringComparison.Ordinal));
        if (existing is not null)
            return string.Equals(existing.PayloadSha256, delivery.PayloadSha256, StringComparison.Ordinal)
                ? CiAiDependencyRegistrationOutboxEnqueueOutcome.Duplicate
                : CiAiDependencyRegistrationOutboxEnqueueOutcome.Conflict;

        Append(new OutboxRecord(delivery.DeliveryId, delivery.Payload, delivery.PayloadSha256));
        return CiAiDependencyRegistrationOutboxEnqueueOutcome.Enqueued;
    }

    public IReadOnlyList<CiAiDependencyRegistrationDelivery> ReadAll()
    {
        using var fileLock = AcquireLock();
        return Array.AsReadOnly(ReadRecords().Select(item => CiAiDependencyRegistrationDelivery.FromJson(item.Payload))
            .OrderBy(item => item.SourceRepository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceRevision)
            .ToArray());
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private OutboxRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<OutboxRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<OutboxRecord>();
        if (input.Length > int.MaxValue) throw new InvalidDataException("Dependency outbox exceeds the supported scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Dependency outbox ends with an incomplete record.");
        var lines = StrictUtf8.GetString(bytes).Split('\n');
        var records = new List<OutboxRecord>(lines.Length - 1);
        foreach (var line in lines[..^1])
        {
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Dependency outbox contains an empty record.");
            var record = JsonSerializer.Deserialize<OutboxRecord>(line)
                ?? throw new InvalidDataException("Dependency outbox record is empty.");
            var delivery = CiAiDependencyRegistrationDelivery.FromJson(record.Payload);
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Payload))).ToLowerInvariant();
            if (!string.Equals(record.DeliveryId, delivery.DeliveryId, StringComparison.Ordinal) ||
                !string.Equals(record.PayloadSha256, digest, StringComparison.Ordinal) ||
                !string.Equals(record.Payload, delivery.Payload, StringComparison.Ordinal))
                throw new InvalidDataException("Dependency outbox record identity or payload integrity check failed.");
            records.Add(record);
        }
        if (records.Select(item => item.DeliveryId).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Dependency outbox contains duplicate delivery identities.");
        return records.ToArray();
    }

    private void Append(OutboxRecord record)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private sealed record OutboxRecord(string DeliveryId, string Payload, string PayloadSha256);
}

public enum CiAiDependencyRegistrationOutboxEnqueueOutcome
{
    Enqueued,
    Duplicate,
    Conflict
}
