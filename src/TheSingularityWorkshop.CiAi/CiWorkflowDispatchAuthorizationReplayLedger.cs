using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Host-owned durable replay ledger. Implementations must make TryConsume atomic across processes.
/// </summary>
public interface ICiWorkflowDispatchAuthorizationReplayLedger
{
    IReadOnlySet<string> ReadConsumedAuthorizationIds();

    /// <summary>
    /// Atomically records one authorization ID. Returns false if the ID was already consumed.
    /// The caller must invoke this immediately before a real dispatch, after re-evaluating authorization.
    /// </summary>
    bool TryConsume(string authorizationId, string requestDigest,
        DateTimeOffset expiresAtUtc, DateTimeOffset consumedAtUtc);
}

/// <summary>
/// Append-only file-backed replay ledger with an exclusive lock file and a SHA-256 hash chain.
/// Corrupt, truncated, reordered, or modified history fails closed.
/// </summary>
public sealed class FileCiWorkflowDispatchAuthorizationReplayLedger : ICiWorkflowDispatchAuthorizationReplayLedger
{
    public const string CurrentSchema = "ciai.workflow-dispatch-replay-ledger.v1";
    private const string InitialDigest = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _path;

    public FileCiWorkflowDispatchAuthorizationReplayLedger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public IReadOnlySet<string> ReadConsumedAuthorizationIds()
    {
        using var fileLock = AcquireLock();
        return new HashSet<string>(ReadAllCore().Select(record => record.AuthorizationId), StringComparer.Ordinal);
    }

    public bool TryConsume(string authorizationId, string requestDigest,
        DateTimeOffset expiresAtUtc, DateTimeOffset consumedAtUtc)
    {
        ValidateInput(authorizationId, requestDigest, expiresAtUtc, consumedAtUtc);
        using var fileLock = AcquireLock();
        var records = ReadAllCore();
        if (records.Any(record => string.Equals(record.AuthorizationId, authorizationId, StringComparison.Ordinal)))
            return false;

        var previousDigest = records.Length == 0 ? InitialDigest : records[^1].RecordDigest;
        var entry = new LedgerRecord(
            CurrentSchema,
            authorizationId,
            requestDigest.ToLowerInvariant(),
            expiresAtUtc.ToUniversalTime(),
            consumedAtUtc.ToUniversalTime(),
            previousDigest,
            string.Empty);
        entry = entry with { RecordDigest = ComputeRecordDigest(entry) };

        var line = JsonSerializer.Serialize(entry) + "\n";
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(Encoding.UTF8.GetBytes(line));
        output.Flush(flushToDisk: true);
        return true;
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private LedgerRecord[] ReadAllCore()
    {
        if (!File.Exists(_path)) return Array.Empty<LedgerRecord>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<LedgerRecord>();
        if (input.Length > int.MaxValue)
            throw new InvalidDataException("Dispatch replay ledger exceeds the supported scan limit.");

        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Dispatch replay ledger ends with an incomplete record.");

        var lines = StrictUtf8.GetString(bytes).Split('\n');
        var records = new List<LedgerRecord>(lines.Length - 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var expectedPreviousDigest = InitialDigest;

        foreach (var line in lines[..^1])
        {
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException("Dispatch replay ledger contains an empty record.");

            var record = JsonSerializer.Deserialize<LedgerRecord>(line)
                ?? throw new InvalidDataException("Dispatch replay ledger record is empty.");

            ValidateRecord(record);
            if (!seen.Add(record.AuthorizationId))
                throw new InvalidDataException("Dispatch replay ledger contains a duplicate authorization ID.");
            if (!string.Equals(record.PreviousRecordDigest, expectedPreviousDigest, StringComparison.Ordinal))
                throw new InvalidDataException("Dispatch replay ledger hash chain is discontinuous.");

            var expectedRecordDigest = ComputeRecordDigest(record);
            if (!string.Equals(record.RecordDigest, expectedRecordDigest, StringComparison.Ordinal))
                throw new InvalidDataException("Dispatch replay ledger record integrity check failed.");

            expectedPreviousDigest = record.RecordDigest;
            records.Add(record);
        }

        return records.ToArray();
    }

    private static void ValidateInput(string authorizationId, string requestDigest,
        DateTimeOffset expiresAtUtc, DateTimeOffset consumedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationId);
        if (authorizationId.Length > 128)
            throw new ArgumentException("Authorization ID exceeds the supported length.", nameof(authorizationId));
        if (!IsDigest(requestDigest))
            throw new ArgumentException("Request digest must be a full SHA-256 value.", nameof(requestDigest));
        if (expiresAtUtc <= consumedAtUtc)
            throw new ArgumentException("An authorization must be unexpired when consumed.", nameof(expiresAtUtc));
    }

    private static void ValidateRecord(LedgerRecord record)
    {
        if (!string.Equals(record.SchemaVersion, CurrentSchema, StringComparison.Ordinal))
            throw new InvalidDataException("Dispatch replay ledger schema is unsupported.");
        if (string.IsNullOrWhiteSpace(record.AuthorizationId) || record.AuthorizationId.Length > 128)
            throw new InvalidDataException("Dispatch replay ledger authorization ID is invalid.");
        if (!IsDigest(record.RequestDigest) || !IsDigest(record.PreviousRecordDigest) || !IsDigest(record.RecordDigest))
            throw new InvalidDataException("Dispatch replay ledger digest is invalid.");
        if (record.ExpiresAtUtc <= record.ConsumedAtUtc)
            throw new InvalidDataException("Dispatch replay ledger contains an expired-at-consumption record.");
    }

    private static string ComputeRecordDigest(LedgerRecord record)
    {
        var canonical = string.Join("\n",
            record.SchemaVersion,
            record.AuthorizationId,
            record.RequestDigest.ToLowerInvariant(),
            record.ExpiresAtUtc.ToUniversalTime().ToString("O"),
            record.ConsumedAtUtc.ToUniversalTime().ToString("O"),
            record.PreviousRecordDigest.ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record LedgerRecord(
        string SchemaVersion,
        string AuthorizationId,
        string RequestDigest,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset ConsumedAtUtc,
        string PreviousRecordDigest,
        string RecordDigest);
}

/// <summary>
/// A dry-run projection of the exact workflow and candidate inputs. It performs no GitHub API call
/// and does not consume the authorization; the live host must re-evaluate and atomically consume it
/// immediately before dispatch.
/// </summary>
public sealed record CiWorkflowDispatchDryRunPlan(
    string Repository,
    string WorkflowPath,
    string WorkflowDispatchRef,
    string WorkflowDefinitionSha,
    string CandidateRef,
    string CandidateSha,
    long SourceRunId,
    int SourceRunAttempt,
    int AdditionalAttemptNumber,
    string AuthorizationId,
    string RequestDigest);

public sealed record CiWorkflowDispatchDryRunResult(
    bool IsReady,
    string OutcomeCode,
    IReadOnlyList<string> DenialCodes,
    CiWorkflowDispatchDryRunPlan? Plan,
    string? Summary);

public static class CiWorkflowDispatchDryRunPlanner
{
    public static CiWorkflowDispatchDryRunResult Plan(
        CiWorkflowDispatchAuthorizationSubject subject,
        CiWorkflowDispatchAuthorization authorization,
        CiWorkflowDispatchHostObservation observation,
        string authenticatedAuthorityIdentity,
        IReadOnlySet<string> trustedAuthorityIdentities,
        ICiWorkflowDispatchAuthorizationReplayLedger replayLedger,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(replayLedger);
        var consumed = replayLedger.ReadConsumedAuthorizationIds();
        var decision = CiWorkflowDispatchAuthorizationEvaluator.Evaluate(
            subject, authorization, observation, authenticatedAuthorityIdentity,
            trustedAuthorityIdentities, consumed, nowUtc);

        if (!decision.IsAuthorized)
            return new CiWorkflowDispatchDryRunResult(false, decision.OutcomeCode,
                decision.DenialCodes, null, "DENIED: no workflow dispatch was attempted.");

        var plan = new CiWorkflowDispatchDryRunPlan(
            subject.Repository,
            subject.WorkflowPath,
            subject.WorkflowDefinitionRef,
            subject.WorkflowDefinitionSha,
            subject.CandidateRef,
            subject.CandidateSha,
            subject.SourceRunId,
            subject.SourceRunAttempt,
            authorization.AdditionalAttemptNumber,
            authorization.AuthorizationId,
            authorization.RequestDigest);

        var summary = $"DRY RUN ONLY: would dispatch {plan.WorkflowPath} on {plan.WorkflowDispatchRef} " +
            $"using workflow definition SHA {plan.WorkflowDefinitionSha}, targeting {plan.CandidateRef} " +
            $"at exact candidate SHA {plan.CandidateSha}, from source run {plan.SourceRunId} " +
            $"attempt {plan.SourceRunAttempt}, additional attempt {plan.AdditionalAttemptNumber}. " +
            "No GitHub API call was made and the authorization ID was not consumed.";

        return new CiWorkflowDispatchDryRunResult(true, "dry-run-ready",
            Array.Empty<string>(), plan, summary);
    }
}
