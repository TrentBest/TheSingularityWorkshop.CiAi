namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A bounded request to revalidate one exact candidate commit. This is a contract,
/// not a credential and not a GitHub dispatch operation.
/// </summary>
public sealed record CiWorkflowRerunRequest
{
    public const int CurrentContractVersion = 1;
    public const int MaximumAllowedAttempts = 5;
    public static readonly TimeSpan MaximumRequestLifetime = TimeSpan.FromMinutes(30);

    public CiWorkflowRerunRequest(
        string requestId,
        string correlationId,
        string taskId,
        string repository,
        string headRef,
        string headSha,
        string workflowPath,
        string idempotencyKey,
        int attemptNumber,
        int maximumAttempts,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt)
    {
        RequireText(requestId, nameof(requestId), 200);
        RequireText(correlationId, nameof(correlationId), 200);
        RequireText(taskId, nameof(taskId), 200);
        RequireText(repository, nameof(repository), 200);
        RequireText(headRef, nameof(headRef), 300);
        RequireText(headSha, nameof(headSha), 40);
        RequireText(workflowPath, nameof(workflowPath), 300);
        RequireText(idempotencyKey, nameof(idempotencyKey), 200);

        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must be canonical owner/repository identity.", nameof(repository));
        if (headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Candidate SHA must be a full 40-character hexadecimal commit identifier.", nameof(headSha));
        if (!headRef.StartsWith("refs/heads/feature/", StringComparison.Ordinal) ||
            headRef.Any(char.IsControl) || headRef.Contains("..", StringComparison.Ordinal) ||
            headRef.EndsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Reruns are permitted only for an isolated feature branch ref.", nameof(headRef));
        if (!IsWorkflowPath(workflowPath))
            throw new ArgumentException("Workflow path must identify a .yml or .yaml file under .github/workflows.", nameof(workflowPath));
        if (attemptNumber < 1 || maximumAttempts < 1 || maximumAttempts > MaximumAllowedAttempts || attemptNumber > maximumAttempts)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt must fit within the configured maximum of five.");
        if (!string.Equals(idempotencyKey, $"{repository}:{taskId}:{attemptNumber}", StringComparison.Ordinal))
            throw new ArgumentException("Idempotency key must be derived from repository, task, and attempt identity.", nameof(idempotencyKey));
        if (expiresAt <= requestedAt || expiresAt - requestedAt > MaximumRequestLifetime)
            throw new ArgumentException("Request expiry must be after creation and no more than 30 minutes later.", nameof(expiresAt));

        ContractVersion = CurrentContractVersion;
        RequestId = requestId;
        CorrelationId = correlationId;
        TaskId = taskId;
        Repository = repository;
        HeadRef = headRef;
        HeadSha = headSha.ToLowerInvariant();
        WorkflowPath = workflowPath;
        IdempotencyKey = idempotencyKey;
        AttemptNumber = attemptNumber;
        MaximumAttempts = maximumAttempts;
        RequestedAt = requestedAt;
        ExpiresAt = expiresAt;
    }

    public int ContractVersion { get; }
    public string Schema => "ciai.workflow-rerun-request.v1";
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string TaskId { get; }
    public string Repository { get; }
    public string HeadRef { get; }
    public string HeadSha { get; }
    public string WorkflowPath { get; }
    public string IdempotencyKey { get; }
    public int AttemptNumber { get; }
    public int MaximumAttempts { get; }
    public DateTimeOffset RequestedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    private static bool IsWorkflowPath(string path) =>
        path.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
        (path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) &&
        !path.Contains("..", StringComparison.Ordinal) &&
        !path.Contains('\\') && !path.Any(char.IsControl);

    private static void RequireText(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maximumLength} characters and contain no control characters.", parameterName);
    }
}

/// <summary>
/// Identity and authorization facts asserted by the trusted host after it validates
/// the incoming credential's signature, issuer, audience, expiry, and revocation state.
/// Never populate this from unverified request headers or model output.
/// </summary>
public sealed record CiDispatchAuthorizationContext
{
    public const string RequiredScope = "ciai.workflow.dispatch";

    public CiDispatchAuthorizationContext(
        string issuer,
        string audience,
        string subject,
        string credentialId,
        IReadOnlyCollection<string> scopes,
        IReadOnlyCollection<string> authorizedRepositories,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        RequireText(issuer, nameof(issuer));
        RequireText(audience, nameof(audience));
        RequireText(subject, nameof(subject));
        RequireText(credentialId, nameof(credentialId));
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(authorizedRepositories);
        if (scopes.Any(string.IsNullOrWhiteSpace) || authorizedRepositories.Any(r => !RepositoryIdentity.IsValid(r)))
            throw new ArgumentException("Authorization claims contain invalid values.");
        if (expiresAt <= issuedAt)
            throw new ArgumentException("Authorization expiry must be later than issue time.");

        Issuer = issuer;
        Audience = audience;
        Subject = subject;
        CredentialId = credentialId;
        Scopes = Array.AsReadOnly(scopes.Distinct(StringComparer.Ordinal).ToArray());
        AuthorizedRepositories = Array.AsReadOnly(authorizedRepositories.Distinct(StringComparer.Ordinal).ToArray());
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public string Issuer { get; }
    public string Audience { get; }
    public string Subject { get; }
    public string CredentialId { get; }
    public IReadOnlyList<string> Scopes { get; }
    public IReadOnlyList<string> AuthorizedRepositories { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    private static void RequireText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 200 || value.Any(char.IsControl))
            throw new ArgumentException("Authorization claim is invalid.", parameterName);
    }
}

/// <summary>A fresh read-only GitHub snapshot used to reject stale or mismatched rerun targets.</summary>
public sealed record CiWorkflowRerunTarget
{
    public CiWorkflowRerunTarget(string repository, string headRef, string headSha, string workflowPath)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must be canonical owner/repository identity.", nameof(repository));
        ArgumentException.ThrowIfNullOrWhiteSpace(headRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        if (headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Target SHA must be a full commit identifier.", nameof(headSha));
        if (!headRef.StartsWith("refs/heads/feature/", StringComparison.Ordinal))
            throw new ArgumentException("Target must be an isolated feature branch.", nameof(headRef));
        if (!workflowPath.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(workflowPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
              workflowPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) ||
            workflowPath.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Target workflow path is invalid.", nameof(workflowPath));
        Repository = repository;
        HeadRef = headRef;
        HeadSha = headSha.ToLowerInvariant();
        WorkflowPath = workflowPath;
    }

    public string Repository { get; }
    public string HeadRef { get; }
    public string HeadSha { get; }
    public string WorkflowPath { get; }
}

/// <summary>Evaluates a rerun request against a fresh GitHub target and trusted host identity.</summary>
public sealed class CiWorkflowRerunRequestPolicy
{
    private readonly string _expectedIssuer;
    private readonly string _expectedAudience;
    private readonly IReadOnlySet<string> _allowedRepositories;
    private readonly IReadOnlySet<string> _allowedWorkflowPaths;

    public CiWorkflowRerunRequestPolicy(
        string expectedIssuer,
        string expectedAudience,
        IEnumerable<string> allowedRepositories,
        IEnumerable<string> allowedWorkflowPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedIssuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);
        ArgumentNullException.ThrowIfNull(allowedRepositories);
        ArgumentNullException.ThrowIfNull(allowedWorkflowPaths);
        var repositories = allowedRepositories.ToArray();
        var workflows = allowedWorkflowPaths.ToArray();
        if (repositories.Length == 0 || repositories.Any(r => !RepositoryIdentity.IsValid(r)))
            throw new ArgumentException("At least one canonical repository must be allowed.", nameof(allowedRepositories));
        if (workflows.Length == 0 || workflows.Any(w => !IsWorkflowPath(w)))
            throw new ArgumentException("At least one valid workflow path must be allowed.", nameof(allowedWorkflowPaths));

        _expectedIssuer = expectedIssuer;
        _expectedAudience = expectedAudience;
        _allowedRepositories = new HashSet<string>(repositories, StringComparer.Ordinal);
        _allowedWorkflowPaths = new HashSet<string>(workflows, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Evaluate(
        CiWorkflowRerunRequest? request,
        CiDispatchAuthorizationContext? authorization,
        CiWorkflowRerunTarget? currentTarget,
        DateTimeOffset now)
    {
        var reasons = new List<string>();
        if (request is null) return new[] { "rerun.request-required" };
        if (authorization is null) return new[] { "rerun.authorization-required" };
        if (currentTarget is null) return new[] { "rerun.current-target-required" };

        if (request.ContractVersion != CiWorkflowRerunRequest.CurrentContractVersion)
            reasons.Add("rerun.unsupported-contract");
        if (now < request.RequestedAt || now >= request.ExpiresAt)
            reasons.Add("rerun.request-expired-or-not-yet-valid");
        if (!string.Equals(request.Repository, currentTarget.Repository, StringComparison.Ordinal) ||
            !string.Equals(request.HeadRef, currentTarget.HeadRef, StringComparison.Ordinal) ||
            !string.Equals(request.HeadSha, currentTarget.HeadSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.WorkflowPath, currentTarget.WorkflowPath, StringComparison.Ordinal))
            reasons.Add("rerun.target-mismatch-or-stale");
        if (!_allowedRepositories.Contains(request.Repository))
            reasons.Add("rerun.repository-not-allowed");
        if (!_allowedWorkflowPaths.Contains(request.WorkflowPath))
            reasons.Add("rerun.workflow-not-allowed");
        if (!string.Equals(authorization.Issuer, _expectedIssuer, StringComparison.Ordinal))
            reasons.Add("rerun.authorization-issuer-mismatch");
        if (!string.Equals(authorization.Audience, _expectedAudience, StringComparison.Ordinal))
            reasons.Add("rerun.authorization-audience-mismatch");
        if (!authorization.Scopes.Contains(CiDispatchAuthorizationContext.RequiredScope, StringComparer.Ordinal))
            reasons.Add("rerun.authorization-scope-missing");
        if (!authorization.AuthorizedRepositories.Contains(request.Repository, StringComparer.Ordinal))
            reasons.Add("rerun.repository-not-authorized");
        if (now < authorization.IssuedAt || now >= authorization.ExpiresAt)
            reasons.Add("rerun.authorization-expired-or-not-yet-valid");
        if (authorization.ExpiresAt - authorization.IssuedAt > TimeSpan.FromHours(1))
            reasons.Add("rerun.authorization-lifetime-exceeds-limit");
        return reasons.AsReadOnly();
    }

    private static bool IsWorkflowPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
        (path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) &&
        !path.Contains("..", StringComparison.Ordinal) &&
        !path.Contains('\\') && !path.Any(char.IsControl);
}
