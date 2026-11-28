using System.Globalization;
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
    /// Atomically records the complete authorization. Returns false if its ID was already consumed.
    /// The caller must invoke this immediately before a real dispatch, after re-evaluating authorization.
    /// </summary>
    bool TryConsume(CiWorkflowDispatchAuthorization authorization, DateTimeOffset consumedAtUtc);
}

/// <summary>
/// Append-only file-backed replay ledger with an exclusive lock file and a SHA-256 hash chain.
/// It retains the authorization facts needed to audit exactly what was consumed. Corrupt, truncated,
/// reordered, or modified history fails closed.
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

    public bool TryConsume(CiWorkflowDispatchAuthorization authorization, DateTimeOffset consumedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ValidateAuthorization(authorization, consumedAtUtc);

        using var fileLock = AcquireLock();
        var records = ReadAllCore();
        if (records.Any(record => string.Equals(record.AuthorizationId, authorization.AuthorizationId, StringComparison.Ordinal)))
            return false;

        var previousDigest = records.Length == 0 ? InitialDigest : records[^1].RecordDigest;
        var entry = new LedgerRecord(
            CurrentSchema,
            authorization.SchemaVersion,
            authorization.AuthorizationId,
            authorization.AuthorityIdentity,
            authorization.RequestId,
            authorization.RequestDigest.ToLowerInvariant(),
            authorization.Repository,
            authorization.WorkflowPath,
            authorization.WorkflowDefinitionRef,
            authorization.WorkflowDefinitionSha.ToLowerInvariant(),
            authorization.CandidateRef,
            authorization.CandidateSha.ToLowerInvariant(),
            authorization.SourceRunId,
            authorization.SourceRunAttempt,
            authorization.AdditionalAttemptNumber,
            authorization.IssuedAtUtc.ToUniversalTime(),
            authorization.ExpiresAtUtc.ToUniversalTime(),
            authorization.HostObservationDigest.ToLowerInvariant(),
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

    private static void ValidateAuthorization(CiWorkflowDispatchAuthorization authorization, DateTimeOffset consumedAtUtc)
    {
        if (!string.Equals(authorization.SchemaVersion,
                CiWorkflowDispatchAuthorizationEvaluator.ExpectedAuthorizationSchema, StringComparison.Ordinal))
            throw new ArgumentException("Authorization schema is unsupported.", nameof(authorization));
        if (string.IsNullOrWhiteSpace(authorization.AuthorizationId) || authorization.AuthorizationId.Length > 128)
            throw new ArgumentException("Authorization ID is invalid.", nameof(authorization));
        if (string.IsNullOrWhiteSpace(authorization.AuthorityIdentity) || string.IsNullOrWhiteSpace(authorization.RequestId))
            throw new ArgumentException("Authorization authority and request identities are required.", nameof(authorization));
        if (!IsDigest(authorization.RequestDigest) || !IsDigest(authorization.HostObservationDigest))
            throw new ArgumentException("Authorization digests must be full SHA-256 values.", nameof(authorization));
        if (!RepositoryIdentity.IsValid(authorization.Repository))
            throw new ArgumentException("Authorization repository identity is invalid.", nameof(authorization));
        if (string.IsNullOrWhiteSpace(authorization.WorkflowPath) ||
            !authorization.WorkflowPath.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(authorization.WorkflowPath.EndsWith(".yml", StringComparison.Ordinal) ||
              authorization.WorkflowPath.EndsWith(".yaml", StringComparison.Ordinal)) ||
            authorization.WorkflowPath.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Authorization workflow path is invalid.", nameof(authorization));
        if (!string.Equals(authorization.WorkflowDefinitionRef, "refs/heads/development", StringComparison.Ordinal))
            throw new ArgumentException("Authorization workflow definition must use development.", nameof(authorization));
        if (!IsSha(authorization.WorkflowDefinitionSha) || !IsSha(authorization.CandidateSha))
            throw new ArgumentException("Authorization workflow and candidate SHAs must be full commit identities.", nameof(authorization));
        if (string.IsNullOrWhiteSpace(authorization.CandidateRef) ||
            !authorization.CandidateRef.StartsWith("refs/heads/", StringComparison.Ordinal) ||
            string.Equals(authorization.CandidateRef, "refs/heads/development", StringComparison.Ordinal) ||
            string.Equals(authorization.CandidateRef, "refs/heads/master", StringComparison.Ordinal) ||
            authorization.CandidateRef.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Authorization candidate ref must be a non-protected branch.", nameof(authorization));
        if (authorization.SourceRunId < 1 || authorization.SourceRunAttempt < 1 || authorization.AdditionalAttemptNumber < 1)
            throw new ArgumentException("Authorization source run and attempt identities must be positive.", nameof(authorization));
        if (authorization.IssuedAtUtc > consumedAtUtc ||
            authorization.ExpiresAtUtc <= consumedAtUtc ||
            authorization.ExpiresAtUtc <= authorization.IssuedAtUtc)
            throw new ArgumentException("Authorization must be valid and unexpired when consumed.", nameof(authorization));
    }

    private static void ValidateRecord(LedgerRecord record)
    {
        if (!string.Equals(record.LedgerSchemaVersion, CurrentSchema, StringComparison.Ordinal) ||
            !string.Equals(record.AuthorizationSchemaVersion,
                CiWorkflowDispatchAuthorizationEvaluator.ExpectedAuthorizationSchema, StringComparison.Ordinal))
            throw new InvalidDataException("Dispatch replay ledger schema is unsupported.");
        if (string.IsNullOrWhiteSpace(record.AuthorizationId) || record.AuthorizationId.Length > 128 ||
            string.IsNullOrWhiteSpace(record.AuthorityIdentity) || string.IsNullOrWhiteSpace(record.RequestId))
            throw new InvalidDataException("Dispatch replay ledger authorization identity is invalid.");
        if (!IsDigest(record.RequestDigest) || !IsDigest(record.HostObservationDigest) ||
            !IsDigest(record.PreviousRecordDigest) || !IsDigest(record.RecordDigest))
            throw new InvalidDataException("Dispatch replay ledger digest is invalid.");
        if (!RepositoryIdentity.IsValid(record.Repository) ||
            !IsSha(record.WorkflowDefinitionSha) || !IsSha(record.CandidateSha))
            throw new InvalidDataException("Dispatch replay ledger repository or SHA is invalid.");
        if (record.ExpiresAtUtc <= record.ConsumedAtUtc || record.IssuedAtUtc > record.ConsumedAtUtc ||
            record.ExpiresAtUtc <= record.IssuedAtUtc)
            throw new InvalidDataException("Dispatch replay ledger contains an invalid authorization time window.");
        if (record.SourceRunId < 1 || record.SourceRunAttempt < 1 || record.AdditionalAttemptNumber < 1)
            throw new InvalidDataException("Dispatch replay ledger run or attempt identity is invalid.");
    }

    private static string ComputeRecordDigest(LedgerRecord record)
    {
        var canonical = string.Join("\n",
            record.LedgerSchemaVersion,
            record.AuthorizationSchemaVersion,
            record.AuthorizationId,
            record.AuthorityIdentity,
            record.RequestId,
            record.RequestDigest.ToLowerInvariant(),
            record.Repository,
            record.WorkflowPath,
            record.WorkflowDefinitionRef,
            record.WorkflowDefinitionSha.ToLowerInvariant(),
            record.CandidateRef,
            record.CandidateSha.ToLowerInvariant(),
            record.SourceRunId.ToString(CultureInfo.InvariantCulture),
            record.SourceRunAttempt.ToString(CultureInfo.InvariantCulture),
            record.AdditionalAttemptNumber.ToString(CultureInfo.InvariantCulture),
            record.IssuedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            record.ExpiresAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            record.HostObservationDigest.ToLowerInvariant(),
            record.ConsumedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            record.PreviousRecordDigest.ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private sealed record LedgerRecord(
        string LedgerSchemaVersion,
        string AuthorizationSchemaVersion,
        string AuthorizationId,
        string AuthorityIdentity,
        string RequestId,
        string RequestDigest,
        string Repository,
        string WorkflowPath,
        string WorkflowDefinitionRef,
        string WorkflowDefinitionSha,
        string CandidateRef,
        string CandidateSha,
        long SourceRunId,
        int SourceRunAttempt,
        int AdditionalAttemptNumber,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        string HostObservationDigest,
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
