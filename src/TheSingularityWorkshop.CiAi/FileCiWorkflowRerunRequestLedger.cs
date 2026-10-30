using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

public enum CiWorkflowRerunAcceptanceOutcome { Accepted, Duplicate, Conflict, Rejected }

/// <summary>Durable append-only acceptance ledger for validated rerun requests.</summary>
/// <remarks>This records acceptance only. It does not dispatch workflows or persist credentials.</remarks>
public sealed class FileCiWorkflowRerunRequestLedger
{
    private readonly string _path;

    public FileCiWorkflowRerunRequestLedger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiWorkflowRerunAcceptanceResult Accept(
        CiWorkflowRerunRequest request,
        CiDispatchAuthorizationContext authorization,
        CiWorkflowRerunTarget currentTarget,
        CiWorkflowRerunRequestPolicy policy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(currentTarget);
        ArgumentNullException.ThrowIfNull(policy);
        var blockingReasons = policy.Evaluate(request, authorization, currentTarget, now);
        if (blockingReasons.Count != 0)
            return new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Rejected, blockingReasons);

        var requestJson = JsonSerializer.Serialize(request);
        var requestDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson))).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var records = ReadRecords();

        var byRequestId = records.FirstOrDefault(record => string.Equals(record.RequestId, request.RequestId, StringComparison.Ordinal));
        if (byRequestId is not null)
            return string.Equals(byRequestId.RequestDigest, requestDigest, StringComparison.Ordinal)
                ? new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Duplicate, Array.Empty<string>())
                : new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Conflict, new[] { "rerun.request-id-conflict" });

        var byIdempotency = records.FirstOrDefault(record => string.Equals(record.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
        if (byIdempotency is not null)
            return string.Equals(byIdempotency.RequestDigest, requestDigest, StringComparison.Ordinal)
                ? new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Duplicate, Array.Empty<string>())
                : new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Conflict, new[] { "rerun.task-attempt-conflict" });

        var taskRecords = records.Where(record =>
            string.Equals(record.Repository, request.Repository, StringComparison.Ordinal) &&
            string.Equals(record.TaskId, request.TaskId, StringComparison.Ordinal)).ToArray();
        var expectedAttempt = taskRecords.Length + 1;
        if (request.AttemptNumber != expectedAttempt)
            return new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Conflict,
                new[] { request.AttemptNumber < expectedAttempt ? "rerun.attempt-already-recorded" : "rerun.attempt-sequence-gap" });
        if (taskRecords.Length > 0 && taskRecords.Any(record => record.MaximumAttempts != request.MaximumAttempts))
            return new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Conflict, new[] { "rerun.task-budget-changed" });
        if (taskRecords.Length >= request.MaximumAttempts)
            return new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Rejected, new[] { "rerun.task-attempt-budget-exhausted" });

        var record = new LedgerRecord(request.RequestId, request.IdempotencyKey, request.Repository,
            request.TaskId, request.AttemptNumber, request.MaximumAttempts, requestJson, requestDigest);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
        return new CiWorkflowRerunAcceptanceResult(CiWorkflowRerunAcceptanceOutcome.Accepted, Array.Empty<string>());
    }

    public IReadOnlyList<CiWorkflowRerunRequest> ReadAccepted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        return Array.AsReadOnly(ReadRecords().Select(record =>
            JsonSerializer.Deserialize<CiWorkflowRerunRequest>(record.RequestJson) ??
            throw new InvalidDataException("Rerun ledger contains an empty request payload.")).ToArray());
    }

    private LedgerRecord[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<LedgerRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<LedgerRecord>();
        if (input.Length > int.MaxValue) throw new InvalidDataException("Rerun ledger exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n') throw new InvalidDataException("Rerun ledger has an incomplete final record; refusing to discard it.");
        var records = new List<LedgerRecord>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var record = JsonSerializer.Deserialize<LedgerRecord>(line) ??
                throw new InvalidDataException("Rerun ledger contains an empty record.");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.RequestJson))).ToLowerInvariant();
            if (!string.Equals(digest, record.RequestDigest, StringComparison.Ordinal) ||
                !string.Equals(record.IdempotencyKey, $"{record.Repository}:{record.TaskId}:{record.AttemptNumber}", StringComparison.Ordinal))
                throw new InvalidDataException("Rerun ledger integrity or identity validation failed.");
            var request = JsonSerializer.Deserialize<CiWorkflowRerunRequest>(record.RequestJson) ??
                throw new InvalidDataException("Rerun ledger request could not be rehydrated.");
            if (request.RequestId != record.RequestId || request.Repository != record.Repository ||
                request.TaskId != record.TaskId || request.AttemptNumber != record.AttemptNumber ||
                request.MaximumAttempts != record.MaximumAttempts)
                throw new InvalidDataException("Rerun ledger record does not match its request payload.");
            records.Add(record);
        }
        if (records.Select(record => record.RequestId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            records.Select(record => record.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Rerun ledger contains duplicate request or attempt identities.");
        return records.ToArray();
    }

    private sealed record LedgerRecord(string RequestId, string IdempotencyKey, string Repository,
        string TaskId, int AttemptNumber, int MaximumAttempts, string RequestJson, string RequestDigest);
}

public sealed record CiWorkflowRerunAcceptanceResult(
    CiWorkflowRerunAcceptanceOutcome Outcome, IReadOnlyList<string> BlockingReasons);
