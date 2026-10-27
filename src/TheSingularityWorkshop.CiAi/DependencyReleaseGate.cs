namespace TheSingularityWorkshop.CiAi;

/// <summary>Stable identity for a consumer release candidate while dependency gates are pending.</summary>
/// <remarks>
/// This is a provider-neutral contract. It does not persist state itself, query NuGet, edit projects,
/// run builds, or publish packages. A host must persist the candidate identity and supply attributable
/// observations whenever it resumes evaluation.
/// </remarks>
public sealed record DependencyReleaseCandidate
{
    public DependencyReleaseCandidate(
        string repository,
        string commitSha,
        string packageId,
        string packageVersion,
        string candidateId,
        IReadOnlyList<RequiredPublishedDependency> dependencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must be canonical owner/repository.", nameof(repository));
        if (commitSha is null || commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character commit SHA is required.", nameof(commitSha));
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!string.Equals(packageId, packageId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Package ID cannot have surrounding whitespace.", nameof(packageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        if (!string.Equals(packageVersion, packageVersion.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Package version cannot have surrounding whitespace.", nameof(packageVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(dependencies);

        var copy = dependencies.ToArray();
        if (copy.Any(d => d is null))
            throw new ArgumentException("Dependencies cannot contain null entries.", nameof(dependencies));
        if (copy.Select(d => d.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
            throw new ArgumentException("Each package ID may appear only once in a candidate.", nameof(dependencies));

        Repository = repository;
        CommitSha = commitSha.ToLowerInvariant();
        PackageId = packageId;
        PackageVersion = packageVersion;
        CandidateId = candidateId;
        Dependencies = Array.AsReadOnly(copy);
    }

    public string Repository { get; }
    public string CommitSha { get; }
    public string PackageId { get; }
    public string PackageVersion { get; }
    public string CandidateId { get; }
    public IReadOnlyList<RequiredPublishedDependency> Dependencies { get; }
}

/// <summary>One exact upstream package requirement for a consumer candidate.</summary>
public sealed record RequiredPublishedDependency
{
    public RequiredPublishedDependency(string upstreamRepository, string upstreamCommitSha, string packageId, string packageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamRepository);
        if (!RepositoryIdentity.IsValid(upstreamRepository))
            throw new ArgumentException("Upstream repository must be canonical owner/repository.", nameof(upstreamRepository));
        if (upstreamCommitSha is null || upstreamCommitSha.Length != 40 || !upstreamCommitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character upstream commit SHA is required.", nameof(upstreamCommitSha));
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        if (!string.Equals(packageId, packageId.Trim(), StringComparison.Ordinal) ||
            !string.Equals(packageVersion, packageVersion.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Package identity cannot have surrounding whitespace.");

        UpstreamRepository = upstreamRepository;
        UpstreamCommitSha = upstreamCommitSha.ToLowerInvariant();
        PackageId = packageId;
        PackageVersion = packageVersion;
    }

    public string UpstreamRepository { get; }
    public string UpstreamCommitSha { get; }
    public string PackageId { get; }
    public string PackageVersion { get; }
}

/// <summary>Evidence that an exact upstream package version was actually published and verified on a feed.</summary>
public sealed record PublishedDependencyEvidence(
    string UpstreamRepository,
    string UpstreamCommitSha,
    string PackageId,
    string PackageVersion,
    string WorkflowRunId,
    int WorkflowAttempt,
    string FeedUri,
    bool PackageFound,
    bool UpstreamValidationPassed);

public enum DependencyReleaseGateState
{
    WaitingForPublishedDependencies = 0,
    ReadyForConsumerVerification = 1,
    Blocked = 2
}

/// <summary>Evaluation of a preserved candidate. Readiness is not publish authorization.</summary>
public sealed record DependencyReleaseGateEvaluation(
    string CandidateId,
    string Repository,
    string CommitSha,
    string PackageId,
    string PackageVersion,
    DependencyReleaseGateState State,
    IReadOnlyList<string> MissingDependencyEvidence,
    IReadOnlyList<string> Errors)
{
    public bool CanVerifyConsumer => State == DependencyReleaseGateState.ReadyForConsumerVerification && Errors.Count == 0;
}

/// <summary>
/// Fail-closed dependency gate. Re-evaluation always uses the candidate's original commit and exact package versions.
/// A passing upstream build alone, an unverified feed observation, or evidence for another candidate cannot open the gate.
/// </summary>
public static class DependencyReleaseGateEvaluator
{
    public static DependencyReleaseGateEvaluation Evaluate(
        DependencyReleaseCandidate candidate,
        IEnumerable<PublishedDependencyEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(evidence);
        var supplied = evidence.ToArray();
        var errors = new List<string>();
        var missing = new List<string>();

        foreach (var dependency in candidate.Dependencies)
        {
            var matches = supplied.Where(e =>
                string.Equals(e.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.PackageVersion, dependency.PackageVersion, StringComparison.Ordinal)).ToArray();

            var label = dependency.PackageId + "@" + dependency.PackageVersion;
            if (matches.Length == 0)
            {
                missing.Add(label);
                continue;
            }
            if (matches.Length != 1)
            {
                errors.Add("dependency.evidence-ambiguous:" + label);
                continue;
            }

            var item = matches[0];
            if (!string.Equals(item.UpstreamRepository, dependency.UpstreamRepository, StringComparison.Ordinal) ||
                !string.Equals(item.UpstreamCommitSha, dependency.UpstreamCommitSha, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("dependency.upstream-identity-mismatch:" + label);
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.WorkflowRunId) || item.WorkflowAttempt < 1)
                errors.Add("dependency.workflow-identity-missing:" + label);
            if (!Uri.TryCreate(item.FeedUri, UriKind.Absolute, out var feedUri) ||
                feedUri.Scheme != Uri.UriSchemeHttps)
                errors.Add("dependency.feed-identity-invalid:" + label);
            if (!item.UpstreamValidationPassed)
                errors.Add("dependency.upstream-validation-failed:" + label);
            if (!item.PackageFound)
                errors.Add("dependency.package-not-published:" + label);
        }

        var state = errors.Count > 0
            ? DependencyReleaseGateState.Blocked
            : missing.Count > 0
                ? DependencyReleaseGateState.WaitingForPublishedDependencies
                : DependencyReleaseGateState.ReadyForConsumerVerification;

        return new DependencyReleaseGateEvaluation(
            candidate.CandidateId, candidate.Repository, candidate.CommitSha, candidate.PackageId,
            candidate.PackageVersion, state, missing.AsReadOnly(), errors.AsReadOnly());
    }
}
