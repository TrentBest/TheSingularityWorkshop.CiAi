namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Explicit, candidate-scoped authorization metadata for one repository to satisfy a gate in another pipeline.
/// The trusted host must authenticate the approver and policy before constructing this value.
/// </summary>
public sealed record CiAiCrossPipelineGateGrant
{
    public CiAiCrossPipelineGateGrant(
        string grantId,
        string repository,
        string candidateId,
        string sourceCommitSha,
        string sourcePipeline,
        string targetPipeline,
        string policyId,
        int policyVersion,
        string approvedBy,
        DateTimeOffset approvedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        if (sourceCommitSha is null || sourceCommitSha.Length != 40 || !sourceCommitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character source commit SHA is required.", nameof(sourceCommitSha));
        if (policyVersion < 1) throw new ArgumentOutOfRangeException(nameof(policyVersion));
        if (approvedAt == default) throw new ArgumentException("Approval timestamp is required.", nameof(approvedAt));

        GrantId = ValidateName(grantId, nameof(grantId), 128);
        Repository = repository;
        CandidateId = ValidateName(candidateId, nameof(candidateId), 256);
        SourceCommitSha = sourceCommitSha.ToLowerInvariant();
        SourcePipeline = ValidateName(sourcePipeline, nameof(sourcePipeline), 100);
        TargetPipeline = ValidateName(targetPipeline, nameof(targetPipeline), 100);
        PolicyId = ValidateName(policyId, nameof(policyId), 128);
        PolicyVersion = policyVersion;
        ApprovedBy = ValidateName(approvedBy, nameof(approvedBy), 256);
        ApprovedAt = approvedAt;
    }

    public string GrantId { get; }
    public string Repository { get; }
    public string CandidateId { get; }
    public string SourceCommitSha { get; }
    public string SourcePipeline { get; }
    public string TargetPipeline { get; }
    public string PolicyId { get; }
    public int PolicyVersion { get; }
    public string ApprovedBy { get; }
    public DateTimeOffset ApprovedAt { get; }

    internal static string ValidateName(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maximumLength} printable characters.", parameterName);
        return normalized;
    }
}

/// <summary>Result of checking every leaf repository against named pipeline membership and explicit grants.</summary>
public sealed record CiAiPipelineGateMembershipResult(
    bool IsAllowed,
    IReadOnlyList<string> Errors);

/// <summary>
/// Fail-closed membership policy for composite pipeline gates. Cross-pipeline use requires an exact,
/// candidate-scoped grant; the caller is responsible for authenticating the grant's approval authority.
/// </summary>
public sealed class CiAiPipelineGateMembershipPolicy
{
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _memberships;
    private readonly IReadOnlyList<CiAiCrossPipelineGateGrant> _grants;

    public CiAiPipelineGateMembershipPolicy(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> repositoryPipelines,
        IEnumerable<CiAiCrossPipelineGateGrant>? crossPipelineGrants = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryPipelines);
        var memberships = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var entry in repositoryPipelines)
        {
            if (!RepositoryIdentity.IsValid(entry.Key))
                throw new ArgumentException("Membership keys must use canonical owner/repository identity.", nameof(repositoryPipelines));
            ArgumentNullException.ThrowIfNull(entry.Value);
            var pipelines = entry.Value.Select(value =>
                CiAiCrossPipelineGateGrant.ValidateName(value, nameof(repositoryPipelines), 100))
                .ToHashSet(StringComparer.Ordinal);
            if (pipelines.Count == 0)
                throw new ArgumentException("Every registered repository must belong to at least one pipeline.", nameof(repositoryPipelines));
            memberships.Add(entry.Key, pipelines);
        }

        var grants = (crossPipelineGrants ?? Array.Empty<CiAiCrossPipelineGateGrant>()).ToArray();
        if (grants.Any(grant => grant is null))
            throw new ArgumentException("Cross-pipeline grants cannot contain null entries.", nameof(crossPipelineGrants));
        if (grants.Select(grant => grant.GrantId).Distinct(StringComparer.Ordinal).Count() != grants.Length)
            throw new ArgumentException("Cross-pipeline grant IDs must be unique.", nameof(crossPipelineGrants));

        _memberships = memberships;
        _grants = Array.AsReadOnly(grants);
    }

    public CiAiPipelineGateMembershipResult Evaluate(CiAiPipelineGateExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var errors = new List<string>();

        foreach (var leaf in expression.Leaves)
        {
            if (!_memberships.TryGetValue(leaf.Repository, out var pipelines))
            {
                errors.Add("pipeline.repository-not-registered:" + leaf.Repository);
                continue;
            }

            if (pipelines.Contains(expression.Pipeline))
                continue;

            var explicitlyGranted = pipelines.Any(sourcePipeline => _grants.Any(grant =>
                string.Equals(grant.Repository, leaf.Repository, StringComparison.Ordinal) &&
                string.Equals(grant.CandidateId, leaf.CandidateId, StringComparison.Ordinal) &&
                string.Equals(grant.SourceCommitSha, leaf.CommitSha, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(grant.SourcePipeline, sourcePipeline, StringComparison.Ordinal) &&
                string.Equals(grant.TargetPipeline, expression.Pipeline, StringComparison.Ordinal)));

            if (!explicitlyGranted)
                errors.Add("pipeline.cross-pipeline-grant-required:" + leaf.Repository + ":" + expression.Pipeline);
        }

        return new CiAiPipelineGateMembershipResult(errors.Count == 0, errors.AsReadOnly());
    }
}
