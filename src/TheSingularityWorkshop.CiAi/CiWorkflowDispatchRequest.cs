using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A durable request for a CI workflow to validate one exact candidate commit.
/// This is intent only: it neither authenticates a caller nor dispatches a provider workflow.
/// </summary>
public sealed record CiWorkflowDispatchRequest
{
    public const string CurrentSchema = "ciai.workflow-dispatch-request.v1";

    public CiWorkflowDispatchRequest(
        string requestId,
        string correlationId,
        string repository,
        string workflowPath,
        string targetRef,
        string candidateSha,
        string baseSha,
        DateTimeOffset requestedAt,
        int maximumAttempts = 2,
        string? idempotencyKey = null)
    {
        ValidateIdentifier(requestId, nameof(requestId));
        ValidateIdentifier(correlationId, nameof(correlationId));
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must be canonical owner/repository.", nameof(repository));
        ValidateWorkflowPath(workflowPath);
        ValidateFeatureRef(targetRef);
        AwaitGateContract.ValidateSha(candidateSha, nameof(candidateSha));
        AwaitGateContract.ValidateSha(baseSha, nameof(baseSha));
        if (string.Equals(candidateSha, baseSha, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Candidate SHA must differ from its base SHA.", nameof(candidateSha));
        if (requestedAt == default)
            throw new ArgumentException("A request timestamp is required.", nameof(requestedAt));
        if (maximumAttempts is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts), "Dispatch attempts must be bounded from 1 through 5.");

        RequestId = requestId;
        CorrelationId = correlationId;
        Repository = repository;
        WorkflowPath = workflowPath;
        TargetRef = targetRef;
        CandidateSha = candidateSha.ToLowerInvariant();
        BaseSha = baseSha.ToLowerInvariant();
        RequestedAt = requestedAt.ToUniversalTime();
        MaximumAttempts = maximumAttempts;
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? ComputeIdempotencyKey(repository, workflowPath, targetRef, candidateSha, correlationId)
            : ValidateIdempotencyKey(idempotencyKey);
        PayloadDigest = ComputePayloadDigest();
    }

    public string Schema => CurrentSchema;
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string Repository { get; }
    public string WorkflowPath { get; }
    public string TargetRef { get; }
    public string CandidateSha { get; }
    public string BaseSha { get; }
    public DateTimeOffset RequestedAt { get; }
    public int MaximumAttempts { get; }
    public string IdempotencyKey { get; }
    public string PayloadDigest { get; }

    private string ComputePayloadDigest()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = Schema, RequestId, CorrelationId, Repository, WorkflowPath, TargetRef,
            CandidateSha, BaseSha, RequestedAt, MaximumAttempts, IdempotencyKey
        });
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static string ComputeIdempotencyKey(string repository, string workflowPath,
        string targetRef, string candidateSha, string correlationId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            repository, workflowPath, targetRef, candidateSha = candidateSha.ToLowerInvariant(), correlationId
        });
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string ValidateIdempotencyKey(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            !value.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new ArgumentException("Idempotency key must be sha256 followed by 64 hexadecimal characters.", nameof(value));
        return value.ToLowerInvariant();
    }

    private static void ValidateIdentifier(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 200 || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException("Identifier is invalid or exceeds 200 characters.", parameter);
    }

    private static void ValidateWorkflowPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 240 || value.StartsWith('/') || value.Contains('\\') ||
            value.Split('/').Any(part => part is "" or "." or "..") ||
            !value.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(value.EndsWith(".yml", StringComparison.Ordinal) || value.EndsWith(".yaml", StringComparison.Ordinal)))
            throw new ArgumentException("Workflow path must be a normalized YAML file under .github/workflows.", nameof(value));
    }

    private static void ValidateFeatureRef(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        const string prefix = "refs/heads/";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
            value.Length <= prefix.Length || value.Contains("..", StringComparison.Ordinal) ||
            value.EndsWith('/') || value.Contains("//", StringComparison.Ordinal) ||
            value.Any(char.IsControl) ||
            string.Equals(value, "refs/heads/development", StringComparison.Ordinal) ||
            string.Equals(value, "refs/heads/master", StringComparison.Ordinal))
            throw new ArgumentException("Target must be a non-canonical feature branch ref.", nameof(value));
    }
}

/// <summary>
/// Host-issued authorization for one exact dispatch request. The host authenticates the authority
/// and confirms the live branch head before constructing this value. No credential is stored here.
/// </summary>
public sealed record CiWorkflowDispatchAuthorization
{
    public CiWorkflowDispatchAuthorization(string authorizationId, string authorityIdentity,
        string requestId, string repository, string targetRef, string candidateSha,
        int maximumAttempts, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must be canonical owner/repository.", nameof(repository));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRef);
        AwaitGateContract.ValidateSha(candidateSha, nameof(candidateSha));
        if (maximumAttempts is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (expiresAt == default)
            throw new ArgumentException("Authorization expiry is required.", nameof(expiresAt));

        AuthorizationId = authorizationId;
        AuthorityIdentity = authorityIdentity;
        RequestId = requestId;
        Repository = repository;
        TargetRef = targetRef;
        CandidateSha = candidateSha.ToLowerInvariant();
        MaximumAttempts = maximumAttempts;
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public string AuthorizationId { get; }
    public string AuthorityIdentity { get; }
    public string RequestId { get; }
    public string Repository { get; }
    public string TargetRef { get; }
    public string CandidateSha { get; }
    public int MaximumAttempts { get; }
    public DateTimeOffset ExpiresAt { get; }
}

/// <summary>Deterministic, fail-closed authorization reconciliation; it never performs dispatch.</summary>
public static class CiWorkflowDispatchAuthorizationEvaluator
{
    public static IReadOnlyList<string> Verify(CiWorkflowDispatchRequest? request,
        CiWorkflowDispatchAuthorization? authorization, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (request is null) errors.Add("dispatch.request-missing");
        if (authorization is null) errors.Add("dispatch.authorization-missing");
        if (request is null || authorization is null) return errors.AsReadOnly();

        if (!string.Equals(authorization.RequestId, request.RequestId, StringComparison.Ordinal))
            errors.Add("dispatch.request-id-mismatch");
        if (!string.Equals(authorization.Repository, request.Repository, StringComparison.Ordinal))
            errors.Add("dispatch.repository-mismatch");
        if (!string.Equals(authorization.TargetRef, request.TargetRef, StringComparison.Ordinal))
            errors.Add("dispatch.ref-mismatch");
        if (!string.Equals(authorization.CandidateSha, request.CandidateSha, StringComparison.OrdinalIgnoreCase))
            errors.Add("dispatch.sha-mismatch");
        if (authorization.MaximumAttempts < request.MaximumAttempts)
            errors.Add("dispatch.attempt-budget-exceeded");
        if (now == default || authorization.ExpiresAt <= now.ToUniversalTime())
            errors.Add("dispatch.authorization-expired");
        return errors.AsReadOnly();
    }
}

/// <summary>Outcome of appending one immutable dispatch request.</summary>
public enum CiWorkflowDispatchRequestStoreOutcome
{
    Enqueued = 0,
    Duplicate = 1,
    Conflict = 2
}

/// <summary>
/// Append-only request ledger. It preserves every distinct request, suppresses exact retries,
/// and rejects reuse of either request or idempotency identity with changed content.
/// Scheduling, authentication, live-ref checks, and provider dispatch remain host responsibilities.
/// </summary>
public sealed class FileCiWorkflowDispatchRequestLedger
{
    private readonly string _path;

    public FileCiWorkflowDispatchRequestLedger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public CiWorkflowDispatchRequestStoreOutcome Enqueue(CiWorkflowDispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var records = ReadRecords();
        var existing = records.FirstOrDefault(item =>
            string.Equals(item.RequestId, request.RequestId, StringComparison.Ordinal) ||
            string.Equals(item.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
            return string.Equals(existing.PayloadDigest, request.PayloadDigest, StringComparison.Ordinal)
                ? CiWorkflowDispatchRequestStoreOutcome.Duplicate
                : CiWorkflowDispatchRequestStoreOutcome.Conflict;

        var line = JsonSerializer.Serialize(request);
        using var output = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(Encoding.UTF8.GetBytes(line + "\n"));
        output.Flush(flushToDisk: true);
        return CiWorkflowDispatchRequestStoreOutcome.Enqueued;
    }

    public IReadOnlyList<CiWorkflowDispatchRequest> ReadAll()
    {
        if (!File.Exists(_path)) return Array.Empty<CiWorkflowDispatchRequest>();
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        return Array.AsReadOnly(ReadRecords()
            .OrderBy(item => item.RequestedAt)
            .ThenBy(item => item.RequestId, StringComparer.Ordinal).ToArray());
    }

    private CiWorkflowDispatchRequest[] ReadRecords()
    {
        if (!File.Exists(_path)) return Array.Empty<CiWorkflowDispatchRequest>();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length == 0) return Array.Empty<CiWorkflowDispatchRequest>();
        if (input.Length > int.MaxValue) throw new InvalidDataException("Dispatch ledger exceeds the supported 2 GiB scan limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Dispatch ledger ends with an incomplete record; refusing to discard request history.");

        var records = new List<CiWorkflowDispatchRequest>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var stored = JsonSerializer.Deserialize<StoredRequest>(line)
                ?? throw new InvalidDataException("Dispatch ledger contains an empty record.");
            if (stored.Schema != CiWorkflowDispatchRequest.CurrentSchema)
                throw new InvalidDataException("Dispatch ledger contains an unsupported request schema.");
            var restored = new CiWorkflowDispatchRequest(stored.RequestId, stored.CorrelationId,
                stored.Repository, stored.WorkflowPath, stored.TargetRef, stored.CandidateSha,
                stored.BaseSha, stored.RequestedAt, stored.MaximumAttempts, stored.IdempotencyKey);
            if (!string.Equals(restored.PayloadDigest, stored.PayloadDigest, StringComparison.Ordinal))
                throw new InvalidDataException("Dispatch ledger request digest does not match its contents.");
            records.Add(restored);
        }
        if (records.Select(item => item.RequestId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
            records.Select(item => item.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Dispatch ledger contains duplicate request identities.");
        return records.ToArray();
    }

    private sealed record StoredRequest(string Schema, string RequestId, string CorrelationId,
        string Repository, string WorkflowPath, string TargetRef, string CandidateSha, string BaseSha,
        DateTimeOffset RequestedAt, int MaximumAttempts, string IdempotencyKey, string PayloadDigest);
}
