using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Append-only file ledger for exact-source rerun intent. It never dispatches workflows,
/// authenticates callers, or decides whether a host is authorized to execute a request.
/// </summary>
public sealed class FileCiWorkflowRerunRequestLedger
{
    private const int CurrentStorageSchemaVersion = 1;
    private const int MaximumLedgerEntries = 100_000;
    private const int MaximumStoredRequestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public FileCiWorkflowRerunRequestLedger(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public CiWorkflowRerunRequestLedgerOutcome Enqueue(CiWorkflowRerunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(_directory);
        using var ledgerLock = AcquireLedgerLock();

        var existing = ReadAllCore();
        var sameKey = existing.SingleOrDefault(item =>
            string.Equals(item.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
        if (sameKey is not null)
            return string.Equals(sameKey.RequestDigest, request.RequestDigest, StringComparison.Ordinal)
                ? CiWorkflowRerunRequestLedgerOutcome.AlreadyExists
                : CiWorkflowRerunRequestLedgerOutcome.Conflict;

        if (existing.Any(item => string.Equals(item.RequestId, request.RequestId, StringComparison.Ordinal)))
            return CiWorkflowRerunRequestLedgerOutcome.Conflict;
        if (existing.Count >= MaximumLedgerEntries)
            throw new InvalidOperationException($"Rerun request ledger reached its {MaximumLedgerEntries}-entry safety bound.");

        var path = GetPath(request.IdempotencyKey);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, StoredRequest.From(request), JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path);
            return CiWorkflowRerunRequestLedgerOutcome.Saved;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public CiWorkflowRerunRequest? LoadByIdempotencyKey(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        Directory.CreateDirectory(_directory);
        using var ledgerLock = AcquireLedgerLock();
        return ReadAllCore().SingleOrDefault(item =>
            string.Equals(item.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    /// <summary>Returns every immutable request in deterministic creation/identity order.</summary>
    public IReadOnlyList<CiWorkflowRerunRequest> ReadAll()
    {
        if (!Directory.Exists(_directory))
            return Array.Empty<CiWorkflowRerunRequest>();
        using var ledgerLock = AcquireLedgerLock();
        return Array.AsReadOnly(ReadAllCore()
            .OrderBy(item => item.RequestedAt)
            .ThenBy(item => item.RequestId, StringComparer.Ordinal)
            .ToArray());
    }

    private FileStream AcquireLedgerLock() =>
        new(Path.Combine(_directory, "ledger.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);

    private List<CiWorkflowRerunRequest> ReadAllCore()
    {
        var paths = Directory.EnumerateFiles(_directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (paths.Length > MaximumLedgerEntries)
            throw new InvalidDataException($"Rerun request ledger exceeds its {MaximumLedgerEntries}-entry safety bound.");

        var requests = new List<CiWorkflowRerunRequest>(paths.Length);
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumStoredRequestBytes)
                throw new InvalidDataException("Stored rerun request is empty or exceeds the storage byte limit.");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stored = JsonSerializer.Deserialize<StoredRequest>(input)
                ?? throw new InvalidDataException("Stored rerun request is empty.");
            if (stored.StorageSchemaVersion != CurrentStorageSchemaVersion)
                throw new InvalidDataException("Stored rerun request schema version is unsupported.");

            var request = stored.ToRequest();
            if (!string.Equals(Path.GetFileName(path), GetFileName(request.IdempotencyKey), StringComparison.Ordinal))
                throw new InvalidDataException("Stored rerun request path does not match its idempotency identity.");
            if (!string.Equals(stored.RequestDigest, request.RequestDigest, StringComparison.Ordinal))
                throw new InvalidDataException("Stored rerun request digest does not match its immutable contents.");
            requests.Add(request);
        }

        if (requests.Select(item => item.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != requests.Count ||
            requests.Select(item => item.RequestId).Distinct(StringComparer.Ordinal).Count() != requests.Count)
            throw new InvalidDataException("Rerun request ledger contains duplicate request or idempotency identities.");
        return requests;
    }

    private string GetPath(string idempotencyKey) => Path.Combine(_directory, GetFileName(idempotencyKey));

    private static string GetFileName(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey))) + ".json";

    private sealed record StoredRequest(
        int StorageSchemaVersion,
        int ContractVersion,
        string RequestId,
        string IdempotencyKey,
        string CorrelationId,
        string Repository,
        string WorkflowPath,
        string WorkflowDefinitionRef,
        string WorkflowDefinitionSha,
        string CandidateRef,
        string CandidateSha,
        long SourceRunId,
        int SourceRunAttempt,
        int MaximumAdditionalAttempts,
        DateTimeOffset RequestedAt,
        DateTimeOffset ExpiresAt,
        string RequestDigest)
    {
        public static StoredRequest From(CiWorkflowRerunRequest request) => new(
            CurrentStorageSchemaVersion,
            request.ContractVersion,
            request.RequestId,
            request.IdempotencyKey,
            request.CorrelationId,
            request.Dispatch.Repository,
            request.Dispatch.WorkflowPath,
            request.Dispatch.WorkflowDefinitionRef,
            request.Dispatch.WorkflowDefinitionSha,
            request.Dispatch.CandidateRef,
            request.Dispatch.CandidateSha,
            request.SourceRunId,
            request.SourceRunAttempt,
            request.MaximumAdditionalAttempts,
            request.RequestedAt,
            request.ExpiresAt,
            request.RequestDigest);

        public CiWorkflowRerunRequest ToRequest()
        {
            if (ContractVersion != CiWorkflowRerunRequest.CurrentContractVersion)
                throw new InvalidDataException("Stored rerun request contract version is unsupported.");
            return new CiWorkflowRerunRequest(
                RequestId,
                IdempotencyKey,
                CorrelationId,
                new CiWorkflowDispatchInputContract(Repository, WorkflowPath, WorkflowDefinitionRef,
                    WorkflowDefinitionSha, CandidateRef, CandidateSha),
                SourceRunId,
                SourceRunAttempt,
                MaximumAdditionalAttempts,
                RequestedAt,
                ExpiresAt);
        }
    }
}
