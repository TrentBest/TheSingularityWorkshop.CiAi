using System.Security.Cryptography;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Host-owned protected workflow policy. Configure this only from trusted control-plane state.</summary>
public sealed record CiWorkflowDefinitionTrustPolicy
{
    public CiWorkflowDefinitionTrustPolicy(
        string repository,
        string workflowPath,
        string approvedProtectedRef,
        string approvedProtectedRevisionSha)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedProtectedRef);
        ValidateSha(approvedProtectedRevisionSha, nameof(approvedProtectedRevisionSha));
        Repository = repository;
        WorkflowPath = workflowPath;
        ApprovedProtectedRef = approvedProtectedRef;
        ApprovedProtectedRevisionSha = approvedProtectedRevisionSha.ToLowerInvariant();
    }

    public string Repository { get; }
    public string WorkflowPath { get; }
    public string ApprovedProtectedRef { get; }
    public string ApprovedProtectedRevisionSha { get; }

    /// <summary>
    /// Compares workflow bytes fetched independently by the host at the run's recorded workflow SHA
    /// with bytes fetched at the approved protected revision. This method does not perform network
    /// access: the host must authenticate both provider fetches and bind them to these exact refs.
    /// </summary>
    public CiWorkflowDefinitionTrustDecision Evaluate(
        CiWorkflowProvenanceRecord provenance,
        ReadOnlyMemory<byte> workflowBytesAtRunRevision,
        ReadOnlyMemory<byte> workflowBytesAtApprovedRevision)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        var observedDigest = Digest(workflowBytesAtRunRevision);
        var approvedDigest = Digest(workflowBytesAtApprovedRevision);
        var workflowRefSuffix = "/" + WorkflowPath + "@";
        var correctWorkflowIdentity =
            string.Equals(provenance.Repository, Repository, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(provenance.WorkflowPath, WorkflowPath, StringComparison.Ordinal) &&
            provenance.WorkflowRef.StartsWith(Repository + workflowRefSuffix, StringComparison.Ordinal) &&
            provenance.WorkflowSha.Length == 40;

        var bytesMatch = workflowBytesAtRunRevision.Length > 0 &&
            workflowBytesAtApprovedRevision.Length > 0 &&
            CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(workflowBytesAtRunRevision.Span),
                SHA256.HashData(workflowBytesAtApprovedRevision.Span));

        var trusted = correctWorkflowIdentity && bytesMatch;
        var reason = trusted
            ? "workflow-definition.matches-approved-protected-content"
            : !correctWorkflowIdentity
                ? "workflow-definition.identity-mismatch"
                : workflowBytesAtRunRevision.IsEmpty || workflowBytesAtApprovedRevision.IsEmpty
                    ? "workflow-definition.content-unavailable"
                    : "workflow-definition.content-mismatch";

        return new CiWorkflowDefinitionTrustDecision(
            trusted,
            reason,
            Repository,
            WorkflowPath,
            provenance.WorkflowRef,
            provenance.WorkflowSha,
            ApprovedProtectedRef,
            ApprovedProtectedRevisionSha,
            observedDigest,
            approvedDigest);
    }

    private static string Digest(ReadOnlyMemory<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();

    private static void ValidateSha(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length != 40 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("Revision must be a full 40-character SHA.", parameter);
    }
}

/// <summary>
/// Immutable output of protected workflow comparison. Construction is restricted to the policy evaluator.
/// A host must still retain this decision and its source identities in its audit record.
/// </summary>
public sealed record CiWorkflowDefinitionTrustDecision
{
    internal CiWorkflowDefinitionTrustDecision(
        bool isTrusted,
        string reasonCode,
        string repository,
        string workflowPath,
        string observedWorkflowRef,
        string observedWorkflowSha,
        string approvedProtectedRef,
        string approvedProtectedRevisionSha,
        string observedContentSha256,
        string approvedContentSha256)
    {
        IsTrusted = isTrusted;
        ReasonCode = reasonCode;
        Repository = repository;
        WorkflowPath = workflowPath;
        ObservedWorkflowRef = observedWorkflowRef;
        ObservedWorkflowSha = observedWorkflowSha;
        ApprovedProtectedRef = approvedProtectedRef;
        ApprovedProtectedRevisionSha = approvedProtectedRevisionSha;
        ObservedContentSha256 = observedContentSha256;
        ApprovedContentSha256 = approvedContentSha256;
    }

    public bool IsTrusted { get; }
    public string ReasonCode { get; }
    public string Repository { get; }
    public string WorkflowPath { get; }
    public string ObservedWorkflowRef { get; }
    public string ObservedWorkflowSha { get; }
    public string ApprovedProtectedRef { get; }
    public string ApprovedProtectedRevisionSha { get; }
    public string ObservedContentSha256 { get; }
    public string ApprovedContentSha256 { get; }

    public bool IsFor(CiWorkflowProvenanceRecord provenance)
        => string.Equals(Repository, provenance.Repository, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(WorkflowPath, provenance.WorkflowPath, StringComparison.Ordinal) &&
           string.Equals(ObservedWorkflowRef, provenance.WorkflowRef, StringComparison.Ordinal) &&
           string.Equals(ObservedWorkflowSha, provenance.WorkflowSha, StringComparison.OrdinalIgnoreCase);
}
