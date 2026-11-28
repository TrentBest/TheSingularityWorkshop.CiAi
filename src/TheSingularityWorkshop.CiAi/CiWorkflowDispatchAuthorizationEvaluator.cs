using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// The immutable request facts to which one host authorization must be bound.
/// This adapter-neutral shape can be populated from CiWorkflowRerunRequest after that contract lands.
/// </summary>
public sealed record CiWorkflowDispatchAuthorizationSubject(
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
    int MaximumAdditionalAttempts,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset RequestExpiresAtUtc);

/// <summary>
/// Fresh live state obtained by a trusted host from GitHub immediately before dispatch.
/// The caller is responsible for authenticating the provider response and supplying the exact observed SHA.
/// </summary>
public sealed record CiWorkflowDispatchHostObservation(
    string Repository,
    string WorkflowPath,
    string WorkflowDefinitionRef,
    string WorkflowDefinitionSha,
    string CandidateRef,
    string CandidateSha,
    long SourceRunId,
    int SourceRunAttempt,
    bool WorkflowDispatchEnabled,
    DateTimeOffset ObservedAtUtc)
{
    public const string SchemaVersion = "ciai.workflow-dispatch-observation.v1";

    public string ComputeDigest()
    {
        var canonical = string.Join("\n",
            SchemaVersion,
            Repository,
            WorkflowPath,
            WorkflowDefinitionRef,
            (WorkflowDefinitionSha ?? string.Empty).ToLowerInvariant(),
            CandidateRef,
            (CandidateSha ?? string.Empty).ToLowerInvariant(),
            SourceRunId.ToString(CultureInfo.InvariantCulture),
            SourceRunAttempt.ToString(CultureInfo.InvariantCulture),
            WorkflowDispatchEnabled ? "true" : "false",
            ObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// Host-issued, short-lived authorization metadata. It deliberately contains no GitHub token,
/// App private key, PAT, or other credential. Authority authentication happens outside this record.
/// </summary>
public sealed record CiWorkflowDispatchAuthorization(
    string SchemaVersion,
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
    string HostObservationDigest);

/// <summary>
/// Structured result of the pure authorization check. An authorized decision is not a dispatch;
/// the trusted host must atomically reserve the authorization ID and then perform the permitted operation.
/// </summary>
public sealed record CiWorkflowDispatchAuthorizationDecision(
    bool IsAuthorized,
    string OutcomeCode,
    IReadOnlyList<string> DenialCodes,
    string? AuthorizationId,
    string? RequestDigest,
    string? HostObservationDigest);

/// <summary>
/// Fail-closed evaluator for host-authorized exact-SHA workflow dispatch. The host must pass the
/// authority identity only after authenticating the caller, and must atomically consume the authorization
/// ID in its durable replay ledger before dispatching.
/// </summary>
public static class CiWorkflowDispatchAuthorizationEvaluator
{
    public const string ExpectedAuthorizationSchema = "ciai.workflow-dispatch-authorization.v1";
    public static readonly TimeSpan DefaultMaximumObservationAge = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultMaximumAuthorizationLifetime = TimeSpan.FromMinutes(15);
    public const int MaximumAdditionalAttempts = 3;

    public static CiWorkflowDispatchAuthorizationDecision Evaluate(
        CiWorkflowDispatchAuthorizationSubject subject,
        CiWorkflowDispatchAuthorization authorization,
        CiWorkflowDispatchHostObservation observation,
        string authenticatedAuthorityIdentity,
        IReadOnlySet<string> trustedAuthorityIdentities,
        IReadOnlySet<string> consumedAuthorizationIds,
        DateTimeOffset nowUtc,
        TimeSpan? maximumObservationAge = null,
        TimeSpan? maximumAuthorizationLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(trustedAuthorityIdentities);
        ArgumentNullException.ThrowIfNull(consumedAuthorizationIds);

        var errors = new List<string>();
        var observationAgeLimit = maximumObservationAge ?? DefaultMaximumObservationAge;
        var authorizationLifetimeLimit = maximumAuthorizationLifetime ?? DefaultMaximumAuthorizationLifetime;
        if (observationAgeLimit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumObservationAge));
        if (authorizationLifetimeLimit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumAuthorizationLifetime));

        ValidateSubject(subject, errors);
        ValidateAuthorizationShape(authorization, errors);
        ValidateObservationShape(observation, errors);

        if (string.IsNullOrWhiteSpace(authenticatedAuthorityIdentity) ||
            !string.Equals(authenticatedAuthorityIdentity, authorization.AuthorityIdentity, StringComparison.Ordinal) ||
            !trustedAuthorityIdentities.Contains(authenticatedAuthorityIdentity))
            errors.Add("dispatch.authority-not-trusted");

        if (!string.IsNullOrWhiteSpace(authorization.AuthorizationId) && consumedAuthorizationIds.Contains(authorization.AuthorizationId))
            errors.Add("dispatch.authorization-replayed");

        if (!string.Equals(subject.RequestId, authorization.RequestId, StringComparison.Ordinal))
            errors.Add("dispatch.request-id-mismatch");
        if (!string.Equals(subject.RequestDigest, authorization.RequestDigest, StringComparison.OrdinalIgnoreCase))
            errors.Add("dispatch.request-digest-mismatch");

        CompareIdentity(subject.Repository, authorization.Repository, "dispatch.repository-mismatch", errors);
        CompareIdentity(subject.Repository, observation.Repository, "dispatch.observed-repository-mismatch", errors);
        CompareExact(subject.WorkflowPath, authorization.WorkflowPath, "dispatch.workflow-path-mismatch", errors);
        CompareExact(subject.WorkflowPath, observation.WorkflowPath, "dispatch.observed-workflow-path-mismatch", errors);
        CompareExact(subject.WorkflowDefinitionRef, authorization.WorkflowDefinitionRef, "dispatch.workflow-ref-mismatch", errors);
        CompareExact(subject.WorkflowDefinitionRef, observation.WorkflowDefinitionRef, "dispatch.observed-workflow-ref-mismatch", errors);
        CompareSha(subject.WorkflowDefinitionSha, authorization.WorkflowDefinitionSha, "dispatch.workflow-sha-mismatch", errors);
        CompareSha(subject.WorkflowDefinitionSha, observation.WorkflowDefinitionSha, "dispatch.observed-workflow-sha-mismatch", errors);
        CompareExact(subject.CandidateRef, authorization.CandidateRef, "dispatch.candidate-ref-mismatch", errors);
        CompareExact(subject.CandidateRef, observation.CandidateRef, "dispatch.observed-candidate-ref-mismatch", errors);
        CompareSha(subject.CandidateSha, authorization.CandidateSha, "dispatch.candidate-sha-mismatch", errors);
        CompareSha(subject.CandidateSha, observation.CandidateSha, "dispatch.observed-candidate-sha-mismatch", errors);

        if (subject.SourceRunId != authorization.SourceRunId || subject.SourceRunId != observation.SourceRunId)
            errors.Add("dispatch.source-run-mismatch");
        if (subject.SourceRunAttempt != authorization.SourceRunAttempt ||
            subject.SourceRunAttempt != observation.SourceRunAttempt)
            errors.Add("dispatch.source-run-attempt-mismatch");

        if (!observation.WorkflowDispatchEnabled)
            errors.Add("dispatch.workflow-dispatch-not-enabled");

        var now = nowUtc.ToUniversalTime();
        var observedAt = observation.ObservedAtUtc.ToUniversalTime();
        if (observedAt > now)
            errors.Add("dispatch.observation-from-future");
        else if (now - observedAt > observationAgeLimit)
            errors.Add("dispatch.observation-stale");

        var issuedAt = authorization.IssuedAtUtc.ToUniversalTime();
        var expiresAt = authorization.ExpiresAtUtc.ToUniversalTime();
        if (issuedAt > now)
            errors.Add("dispatch.authorization-from-future");
        if (observedAt > issuedAt)
            errors.Add("dispatch.observation-after-authorization-issued");
        if (expiresAt <= now)
            errors.Add("dispatch.authorization-expired");
        if (expiresAt <= issuedAt || expiresAt - issuedAt > authorizationLifetimeLimit)
            errors.Add("dispatch.authorization-lifetime-invalid");
        if (issuedAt < subject.RequestedAtUtc.ToUniversalTime())
            errors.Add("dispatch.authorization-predates-request");
        if (expiresAt > subject.RequestExpiresAtUtc.ToUniversalTime())
            errors.Add("dispatch.authorization-outlives-request");

        if (authorization.AdditionalAttemptNumber < 1 ||
            authorization.AdditionalAttemptNumber > subject.MaximumAdditionalAttempts ||
            authorization.AdditionalAttemptNumber > MaximumAdditionalAttempts)
            errors.Add("dispatch.additional-attempt-out-of-budget");

        var expectedObservationDigest = observation.ComputeDigest();
        if (!string.Equals(expectedObservationDigest, authorization.HostObservationDigest, StringComparison.OrdinalIgnoreCase))
            errors.Add("dispatch.host-observation-digest-mismatch");

        var distinctErrors = errors.Distinct(StringComparer.Ordinal).ToArray();
        return new CiWorkflowDispatchAuthorizationDecision(
            distinctErrors.Length == 0,
            distinctErrors.Length == 0 ? "authorized" : distinctErrors[0],
            Array.AsReadOnly(distinctErrors),
            authorization.AuthorizationId,
            authorization.RequestDigest,
            expectedObservationDigest);
    }

    private static void ValidateSubject(CiWorkflowDispatchAuthorizationSubject subject, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(subject.RequestId))
            errors.Add("dispatch.subject-request-id-missing");
        if (!IsDigest(subject.RequestDigest))
            errors.Add("dispatch.subject-request-digest-invalid");
        if (!RepositoryIdentity.IsValid(subject.Repository))
            errors.Add("dispatch.subject-repository-invalid");
        if (!IsSafeWorkflowPath(subject.WorkflowPath))
            errors.Add("dispatch.subject-workflow-path-invalid");
        if (!string.Equals(subject.WorkflowDefinitionRef, "refs/heads/development", StringComparison.Ordinal))
            errors.Add("dispatch.subject-workflow-ref-not-development");
        if (!IsSha(subject.WorkflowDefinitionSha))
            errors.Add("dispatch.subject-workflow-sha-invalid");
        if (!IsCandidateRef(subject.CandidateRef))
            errors.Add("dispatch.subject-candidate-ref-invalid");
        if (!IsSha(subject.CandidateSha))
            errors.Add("dispatch.subject-candidate-sha-invalid");
        if (subject.SourceRunId < 1 || subject.SourceRunAttempt < 1)
            errors.Add("dispatch.subject-source-run-invalid");
        if (subject.MaximumAdditionalAttempts is < 1 or > MaximumAdditionalAttempts)
            errors.Add("dispatch.subject-attempt-budget-invalid");
        if (subject.RequestExpiresAtUtc <= subject.RequestedAtUtc)
            errors.Add("dispatch.subject-request-window-invalid");
    }

    private static void ValidateAuthorizationShape(CiWorkflowDispatchAuthorization authorization, List<string> errors)
    {
        if (!string.Equals(authorization.SchemaVersion, ExpectedAuthorizationSchema, StringComparison.Ordinal))
            errors.Add("dispatch.authorization-schema-unsupported");
        if (string.IsNullOrWhiteSpace(authorization.AuthorizationId) || authorization.AuthorizationId.Length > 128)
            errors.Add("dispatch.authorization-id-invalid");
        if (string.IsNullOrWhiteSpace(authorization.AuthorityIdentity))
            errors.Add("dispatch.authority-identity-missing");
        if (!IsDigest(authorization.RequestDigest))
            errors.Add("dispatch.authorization-request-digest-invalid");
        if (!IsDigest(authorization.HostObservationDigest))
            errors.Add("dispatch.authorization-observation-digest-invalid");
    }

    private static void ValidateObservationShape(CiWorkflowDispatchHostObservation observation, List<string> errors)
    {
        if (!RepositoryIdentity.IsValid(observation.Repository))
            errors.Add("dispatch.observation-repository-invalid");
        if (!IsSafeWorkflowPath(observation.WorkflowPath))
            errors.Add("dispatch.observation-workflow-path-invalid");
        if (!IsSha(observation.WorkflowDefinitionSha) || !IsSha(observation.CandidateSha))
            errors.Add("dispatch.observation-sha-invalid");
        if (observation.SourceRunId < 1 || observation.SourceRunAttempt < 1)
            errors.Add("dispatch.observation-source-run-invalid");
    }

    private static void CompareIdentity(string expected, string actual, string code, List<string> errors)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            errors.Add(code);
    }

    private static void CompareExact(string expected, string actual, string code, List<string> errors)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            errors.Add(code);
    }

    private static void CompareSha(string expected, string actual, string code, List<string> errors)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            errors.Add(code);
    }

    private static bool IsSafeWorkflowPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
        (value.EndsWith(".yml", StringComparison.Ordinal) || value.EndsWith(".yaml", StringComparison.Ordinal)) &&
        !value.Contains("..", StringComparison.Ordinal) &&
        !value.Contains('\\') &&
        !value.Contains(' ');

    private static bool IsCandidateRef(string? value) =>
        value is { Length: > 11 } &&
        value.StartsWith("refs/heads/", StringComparison.Ordinal) &&
        !value.Contains('\\') &&
        !value.Contains(' ') &&
        !string.Equals(value, "refs/heads/development", StringComparison.Ordinal) &&
        !string.Equals(value, "refs/heads/master", StringComparison.Ordinal) &&
        !value.Contains("..", StringComparison.Ordinal);

    private static bool IsSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
