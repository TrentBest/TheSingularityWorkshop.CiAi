using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Immutable, exact-candidate inputs for a trusted workflow_dispatch invocation.
/// This is intent only: it does not authenticate a caller or authorize dispatch.
/// </summary>
public sealed record CiWorkflowDispatchInputContract
{
    private static readonly Regex BranchNamePattern = new(
        @"^refs/heads/[A-Za-z0-9][A-Za-z0-9._/-]{0,240}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CiWorkflowDispatchInputContract(
        string repository,
        string workflowPath,
        string workflowDefinitionRef,
        string workflowDefinitionSha,
        string candidateRef,
        string candidateSha)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        if (workflowPath.Length > 240 || workflowPath.Any(char.IsControl) ||
            workflowPath.Contains('\\') || workflowPath.StartsWith("/", StringComparison.Ordinal) ||
            workflowPath.Split('/').Any(segment => segment is "" or "." or "..") ||
            !workflowPath.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(workflowPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
              workflowPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Workflow path must identify a YAML workflow under .github/workflows/.", nameof(workflowPath));
        if (!string.Equals(workflowDefinitionRef, "refs/heads/development", StringComparison.Ordinal))
            throw new ArgumentException("Workflow definition must use the trusted development ref.", nameof(workflowDefinitionRef));
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionSha);
        if (workflowDefinitionSha.Length != 40 || !workflowDefinitionSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Workflow definition SHA must be a full 40-character hexadecimal commit identifier.", nameof(workflowDefinitionSha));
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateRef);
        if (!BranchNamePattern.IsMatch(candidateRef) ||
            !candidateRef.StartsWith("refs/heads/feature/", StringComparison.Ordinal) ||
            candidateRef.Contains("..", StringComparison.Ordinal) ||
            candidateRef.Contains("//", StringComparison.Ordinal) ||
            candidateRef.EndsWith(".", StringComparison.Ordinal) ||
            candidateRef.EndsWith("/", StringComparison.Ordinal) ||
            candidateRef.Contains("@{", StringComparison.Ordinal) ||
            candidateRef.Any(char.IsControl) ||
            candidateRef.Split('/').Any(segment => segment.StartsWith(".", StringComparison.Ordinal) ||
                segment.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Candidate ref must be a valid full refs/heads/... branch identity.", nameof(candidateRef));
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateSha);
        if (candidateSha.Length != 40 || !candidateSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Candidate SHA must be a full 40-character hexadecimal commit identifier.", nameof(candidateSha));

        Repository = repository;
        WorkflowPath = workflowPath;
        WorkflowDefinitionRef = workflowDefinitionRef;
        WorkflowDefinitionSha = workflowDefinitionSha.ToLowerInvariant();
        CandidateRef = candidateRef;
        CandidateSha = candidateSha.ToLowerInvariant();
    }

    public string Repository { get; }
    public string WorkflowPath { get; }
    public string WorkflowDefinitionRef { get; }
    public string WorkflowDefinitionSha { get; }
    public string CandidateRef { get; }
    public string CandidateSha { get; }
}

/// <summary>
/// A bounded request to revalidate one exact candidate after one exact source workflow attempt.
/// The request is durable intent, not host authorization and not a dispatch command.
/// </summary>
public sealed record CiWorkflowRerunRequest
{
    public const int CurrentContractVersion = 1;
    public const int MaximumAllowedAdditionalAttempts = 3;
    public static readonly TimeSpan MaximumRequestLifetime = TimeSpan.FromDays(7);

    public CiWorkflowRerunRequest(
        string requestId,
        string idempotencyKey,
        string correlationId,
        CiWorkflowDispatchInputContract dispatch,
        long sourceRunId,
        int sourceRunAttempt,
        int maximumAdditionalAttempts,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt)
    {
        RequireIdentifier(requestId, nameof(requestId), 128);
        RequireIdentifier(idempotencyKey, nameof(idempotencyKey), 128);
        RequireIdentifier(correlationId, nameof(correlationId), 128);
        ArgumentNullException.ThrowIfNull(dispatch);
        if (sourceRunId <= 0 || sourceRunAttempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRunId), "Source run and attempt must be positive.");
        if (maximumAdditionalAttempts is < 1 or > MaximumAllowedAdditionalAttempts)
            throw new ArgumentOutOfRangeException(nameof(maximumAdditionalAttempts),
                $"Additional attempts must be between 1 and {MaximumAllowedAdditionalAttempts}.");
        if (requestedAt == default || expiresAt <= requestedAt ||
            expiresAt - requestedAt > MaximumRequestLifetime)
            throw new ArgumentException("Request expiry must be after creation and no more than seven days later.");

        RequestId = requestId;
        IdempotencyKey = idempotencyKey;
        CorrelationId = correlationId;
        Dispatch = dispatch;
        SourceRunId = sourceRunId;
        SourceRunAttempt = sourceRunAttempt;
        MaximumAdditionalAttempts = maximumAdditionalAttempts;
        RequestedAt = requestedAt.ToUniversalTime();
        ExpiresAt = expiresAt.ToUniversalTime();
        RequestDigest = ComputeDigest();
    }

    public int ContractVersion => CurrentContractVersion;
    public string RequestId { get; }
    public string IdempotencyKey { get; }
    public string CorrelationId { get; }
    public CiWorkflowDispatchInputContract Dispatch { get; }
    public long SourceRunId { get; }
    public int SourceRunAttempt { get; }
    public int MaximumAdditionalAttempts { get; }
    public DateTimeOffset RequestedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>SHA-256 of the canonical immutable request fields; not an authorization token.</summary>
    public string RequestDigest { get; }

    private string ComputeDigest()
    {
        var canonical = string.Join("\n", new[]
        {
            "ciai.workflow-rerun-request.v1",
            RequestId,
            IdempotencyKey,
            CorrelationId,
            Dispatch.Repository,
            Dispatch.WorkflowPath,
            Dispatch.WorkflowDefinitionRef,
            Dispatch.WorkflowDefinitionSha,
            Dispatch.CandidateRef,
            Dispatch.CandidateSha,
            SourceRunId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SourceRunAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MaximumAdditionalAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RequestedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void RequireIdentifier(string value, string name, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximumLength ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':'))
            throw new ArgumentException($"Identifier must contain only ASCII letters, digits, '-', '_', '.', or ':', up to {maximumLength} characters.", name);
    }
}

/// <summary>Outcome from appending an immutable rerun request to the local ledger.</summary>
public enum CiWorkflowRerunRequestLedgerOutcome
{
    Saved = 0,
    AlreadyExists = 1,
    Conflict = 2
}
