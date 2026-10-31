using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Durable event that tells a host one specific Await dependency changed state.</summary>
public sealed record AwaitGateEvent
{
    public AwaitGateEvent(string eventId, AwaitRegistration registration, AwaitGateEventKind kind,
        AwaitGateEvaluation evaluation, DateTimeOffset occurredAt)
        : this(eventId, GetAwaitId(registration), GetConsumerRepository(registration), kind, evaluation, occurredAt)
    {
        if (!registration.RequiredGates.Any(gate =>
            string.Equals(gate.GateId, evaluation.GateId, StringComparison.Ordinal) &&
            gate.Version == evaluation.GateVersion &&
            string.Equals(gate.ContractDigest, evaluation.ContractDigest, StringComparison.Ordinal) &&
            string.Equals(gate.ProviderRepository, evaluation.ProviderRepository, StringComparison.Ordinal)))
            throw new ArgumentException("The evaluated gate is not an exact prerequisite of this Await.", nameof(evaluation));
    }

    internal AwaitGateEvent(string eventId, string awaitId, string consumerRepository, AwaitGateEventKind kind,
        AwaitGateEvaluation evaluation, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(awaitId);
        if (!RepositoryIdentity.IsValid(consumerRepository))
            throw new ArgumentException("Consumer repository must be canonical owner/repository.", nameof(consumerRepository));
        ArgumentNullException.ThrowIfNull(evaluation);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == AwaitGateEventKind.Satisfied && evaluation.State != AwaitGateState.Satisfied)
            throw new ArgumentException("A satisfied event requires a satisfied evaluation.", nameof(evaluation));
        if (kind == AwaitGateEventKind.Invalidated && evaluation.State is AwaitGateState.Satisfied or AwaitGateState.Pending)
            throw new ArgumentException("An invalidation event requires rejected, indeterminate, or revoked evidence.", nameof(evaluation));

        EventId = eventId;
        AwaitId = awaitId;
        ConsumerRepository = consumerRepository;
        Kind = kind;
        GateId = evaluation.GateId;
        GateVersion = evaluation.GateVersion;
        ContractDigest = evaluation.ContractDigest;
        ProviderRepository = evaluation.ProviderRepository;
        ProviderSourceSha = evaluation.ProviderSourceSha;
        ManifestDigest = evaluation.ManifestDigest;
        EvaluationPolicyVersion = evaluation.EvaluationPolicyVersion;
        EvaluationState = evaluation.State;
        RequirementResults = Array.AsReadOnly(evaluation.Requirements.ToArray());
        OccurredAt = occurredAt;
        PayloadDigest = ComputePayloadDigest();
    }

    public string EventId { get; }
    public string AwaitId { get; }
    public string ConsumerRepository { get; }
    public AwaitGateEventKind Kind { get; }
    public string GateId { get; }
    public int GateVersion { get; }
    public string ContractDigest { get; }
    public string ProviderRepository { get; }
    public string ProviderSourceSha { get; }
    public string ManifestDigest { get; }
    public string EvaluationPolicyVersion { get; }
    public AwaitGateState EvaluationState { get; }
    public IReadOnlyList<AwaitGateRequirementResult> RequirementResults { get; }
    public DateTimeOffset OccurredAt { get; }
    public string PayloadDigest { get; }

    private static string GetAwaitId(AwaitRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return registration.AwaitId;
    }

    private static string GetConsumerRepository(AwaitRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return registration.ConsumerRepository;
    }

    private string ComputePayloadDigest()
    {
        var canonical = string.Join("\n", new[]
        {
            "ciai.await-gate-event.v1", EventId, AwaitId, ConsumerRepository, Kind.ToString(), GateId,
            GateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ContractDigest, ProviderRepository, ProviderSourceSha, ManifestDigest,
            EvaluationPolicyVersion, EvaluationState.ToString(),
            OccurredAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            string.Join(";", RequirementResults.OrderBy(item => item.RequirementId, StringComparer.Ordinal)
                .Select(item => $"{item.RequirementId}:{item.State}:{item.ReasonCode}"))
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>Only state changes that can release or re-block dependent work are emitted.</summary>
public enum AwaitGateEventKind
{
    Satisfied = 0,
    Invalidated = 1
}

/// <summary>Result of attempting to persist an immutable event under its stable event ID.</summary>
public enum AwaitGateEventStoreOutcome
{
    Saved = 0,
    AlreadyExists = 1,
    Conflict = 2
}

/// <summary>Host delivery receipt outcome. Rejected receipts never suppress replay.</summary>
public enum AwaitGateReceiptOutcome
{
    Accepted = 0,
    Duplicate = 1,
    Rejected = 2
}

/// <summary>
/// Receipt supplied by a trusted host after it authenticates the delivery acknowledgement.
/// CiAi records correlation; it does not authenticate hosts or perform transport.
/// </summary>
public sealed record AwaitGateDeliveryReceipt
{
    public AwaitGateDeliveryReceipt(string receiptId, string eventId, string payloadDigest,
        string consumerRepository, string hostIdentity, AwaitGateReceiptOutcome outcome,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        AwaitGateContract.ValidateDigest(payloadDigest, nameof(payloadDigest));
        if (!RepositoryIdentity.IsValid(consumerRepository))
            throw new ArgumentException("Consumer repository must be canonical owner/repository.", nameof(consumerRepository));
        ArgumentException.ThrowIfNullOrWhiteSpace(hostIdentity);
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        ReceiptId = receiptId;
        EventId = eventId;
        PayloadDigest = payloadDigest.ToLowerInvariant();
        ConsumerRepository = consumerRepository;
        HostIdentity = hostIdentity;
        Outcome = outcome;
        ReceivedAt = receivedAt;
    }

    public string ReceiptId { get; }
    public string EventId { get; }
    public string PayloadDigest { get; }
    public string ConsumerRepository { get; }
    public string HostIdentity { get; }
    public AwaitGateReceiptOutcome Outcome { get; }
    public DateTimeOffset ReceivedAt { get; }
}

/// <summary>
/// File-backed immutable event outbox. Event IDs are idempotent; conflicting reuse is rejected.
/// The host owns retry scheduling and delivery.
/// </summary>
public sealed class FileAwaitGateEventOutbox
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public FileAwaitGateEventOutbox(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public AwaitGateEventStoreOutcome Enqueue(AwaitGateEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Directory.CreateDirectory(_directory);
        var path = GetPath(item.EventId);
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            var existing = Read(path);
            return string.Equals(existing.PayloadDigest, item.PayloadDigest, StringComparison.Ordinal)
                ? AwaitGateEventStoreOutcome.AlreadyExists
                : AwaitGateEventStoreOutcome.Conflict;
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, StoredEvent.From(item), JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path);
            return AwaitGateEventStoreOutcome.Saved;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public AwaitGateEvent? Load(string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        var path = GetPath(eventId);
        if (!File.Exists(path)) return null;
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        return Read(path);
    }

    /// <summary>Loads every durable event in stable event-ID order; delivery scheduling remains host-owned.</summary>
    public IReadOnlyList<AwaitGateEvent> ReadAll()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<AwaitGateEvent>();
        var events = new List<AwaitGateEvent>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(value => value, StringComparer.Ordinal))
        {
            using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            events.Add(Read(path));
        }
        return Array.AsReadOnly(events.OrderBy(item => item.EventId, StringComparer.Ordinal).ToArray());
    }

    private string GetPath(string eventId) =>
        Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventId))) + ".json");

    private static AwaitGateEvent Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stored = JsonSerializer.Deserialize<StoredEvent>(input)
            ?? throw new InvalidDataException("Await gate event snapshot is empty.");
        if (stored.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Await gate event schema version is unsupported.");
        var evaluation = new AwaitGateEvaluation(stored.GateId, stored.GateVersion, stored.ContractDigest,
            stored.ProviderRepository, stored.ProviderSourceSha, stored.ManifestDigest,
            stored.EvaluationPolicyVersion, stored.EvaluationState,
            Array.AsReadOnly(stored.RequirementResults));
        var restored = new AwaitGateEvent(stored.EventId, stored.AwaitId, stored.ConsumerRepository, stored.Kind, evaluation, stored.OccurredAt);
        if (!string.Equals(restored.PayloadDigest, stored.PayloadDigest, StringComparison.Ordinal))
            throw new InvalidDataException("Await gate event payload digest does not match its contents.");
        return restored;
    }

    private sealed record StoredEvent(int SchemaVersion, string EventId, string AwaitId, string ConsumerRepository,
        AwaitGateEventKind Kind, string GateId, int GateVersion, string ContractDigest,
        string ProviderRepository, string ProviderSourceSha, string ManifestDigest,
        string EvaluationPolicyVersion, AwaitGateState EvaluationState,
        AwaitGateRequirementResult[] RequirementResults, DateTimeOffset OccurredAt, string PayloadDigest)
    {
        public static StoredEvent From(AwaitGateEvent item) => new(1, item.EventId, item.AwaitId, item.ConsumerRepository,
            item.Kind, item.GateId, item.GateVersion, item.ContractDigest, item.ProviderRepository,
            item.ProviderSourceSha, item.ManifestDigest, item.EvaluationPolicyVersion,
            item.EvaluationState, item.RequirementResults.ToArray(), item.OccurredAt, item.PayloadDigest);
    }
}

/// <summary>
/// Durable receipt ledger. Only accepted/duplicate receipts with exact event, digest, and consumer
/// correlation suppress replay; rejected or mismatched receipts remain retryable.
/// </summary>
public sealed class FileAwaitGateEventReceiptLedger
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public FileAwaitGateEventReceiptLedger(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public AwaitGateEventStoreOutcome Record(AwaitGateDeliveryReceipt receipt, AwaitGateEvent item)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(item);
        if (!string.Equals(receipt.EventId, item.EventId, StringComparison.Ordinal) ||
            !string.Equals(receipt.PayloadDigest, item.PayloadDigest, StringComparison.Ordinal) ||
            !string.Equals(receipt.ConsumerRepository, GetConsumerRepository(item), StringComparison.Ordinal))
            return AwaitGateEventStoreOutcome.Conflict;
        if (receipt.Outcome == AwaitGateReceiptOutcome.Rejected)
            return AwaitGateEventStoreOutcome.Conflict;

        Directory.CreateDirectory(_directory);
        var path = GetPath(receipt.EventId);
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            var existing = Read(path);
            return existing == receipt
                ? AwaitGateEventStoreOutcome.AlreadyExists
                : AwaitGateEventStoreOutcome.Conflict;
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, new StoredReceipt(CurrentSchemaVersion, receipt), JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path);
            return AwaitGateEventStoreOutcome.Saved;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public bool HasAcceptedReceipt(string eventId, string payloadDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        AwaitGateContract.ValidateDigest(payloadDigest, nameof(payloadDigest));
        var path = GetPath(eventId);
        if (!File.Exists(path)) return false;
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var receipt = Read(path);
        return string.Equals(receipt.PayloadDigest, payloadDigest, StringComparison.Ordinal) &&
            receipt.Outcome is AwaitGateReceiptOutcome.Accepted or AwaitGateReceiptOutcome.Duplicate;
    }

    private static string GetConsumerRepository(AwaitGateEvent item) =>
        // Event consumer identity is represented by its Await registration; the event itself is correlated
        // to the Await ID, and the host must validate this mapping before submitting a receipt.
        item.ConsumerRepository;

    private string GetPath(string eventId) =>
        Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventId))) + ".json");

    private static AwaitGateDeliveryReceipt Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stored = JsonSerializer.Deserialize<StoredReceipt>(input)
            ?? throw new InvalidDataException("Await gate receipt snapshot is empty.");
        if (stored.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Await gate receipt schema version is unsupported.");
        return stored.Receipt;
    }

    private sealed record StoredReceipt(int SchemaVersion, AwaitGateDeliveryReceipt Receipt);
}
