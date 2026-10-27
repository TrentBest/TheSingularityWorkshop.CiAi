namespace TheSingularityWorkshop.CiAi;

/// <summary>A package identity and feed source actually resolved by a consumer restore.</summary>
public sealed record ResolvedPackageEvidence(
    string PackageId,
    string PackageVersion,
    string FeedUri);

/// <summary>
/// Consumer validation evidence correlated to the original release candidate and one workflow attempt.
/// A successful report is evidence for release review, not publication authorization.
/// </summary>
public sealed record ConsumerVerificationEvidence(
    string CandidateId,
    string Repository,
    string CommitSha,
    string WorkflowRunId,
    int WorkflowAttempt,
    bool RestorePassed,
    bool BuildPassed,
    bool TestsPassed,
    IReadOnlyList<ResolvedPackageEvidence> ResolvedPackages);

public enum ConsumerVerificationState
{
    Blocked = 0,
    VerificationFailed = 1,
    ReadyForReleaseReview = 2
}

/// <summary>Fail-closed result for verifying a preserved candidate against exact published dependencies.</summary>
public sealed record ConsumerVerificationEvaluation(
    string CandidateId,
    string Repository,
    string CommitSha,
    ConsumerVerificationState State,
    IReadOnlyList<string> MissingPackages,
    IReadOnlyList<string> Errors)
{
    /// <summary>True only when identity, exact package resolution, restore, build, and tests all pass.</summary>
    public bool ReadyForReleaseReview =>
        State == ConsumerVerificationState.ReadyForReleaseReview && Errors.Count == 0 && MissingPackages.Count == 0;
}

/// <summary>
/// Validates consumer verification evidence against the original candidate. It does not run CI,
/// query a feed, persist state, edit dependency files, or authorize a release.
/// </summary>
public static class ConsumerVerificationEvaluator
{
    public static ConsumerVerificationEvaluation Evaluate(
        DependencyReleaseCandidate candidate,
        ConsumerVerificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(evidence);

        var errors = new List<string>();
        var missing = new List<string>();

        if (!string.Equals(evidence.CandidateId, candidate.CandidateId, StringComparison.Ordinal) ||
            !string.Equals(evidence.Repository, candidate.Repository, StringComparison.Ordinal) ||
            !string.Equals(evidence.CommitSha, candidate.CommitSha, StringComparison.OrdinalIgnoreCase))
            errors.Add("consumer.candidate-identity-mismatch");

        if (string.IsNullOrWhiteSpace(evidence.WorkflowRunId) || evidence.WorkflowAttempt < 1)
            errors.Add("consumer.workflow-identity-missing");

        ArgumentNullException.ThrowIfNull(evidence.ResolvedPackages);
        foreach (var dependency in candidate.Dependencies)
        {
            var matches = evidence.ResolvedPackages.Where(package =>
                string.Equals(package.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(package.PackageVersion, dependency.PackageVersion, StringComparison.Ordinal)).ToArray();

            var label = dependency.PackageId + "@" + dependency.PackageVersion;
            if (matches.Length == 0)
            {
                missing.Add(label);
                continue;
            }

            if (matches.Length != 1)
            {
                errors.Add("consumer.package-resolution-ambiguous:" + label);
                continue;
            }

            if (!Uri.TryCreate(matches[0].FeedUri, UriKind.Absolute, out var feedUri) ||
                feedUri.Scheme != Uri.UriSchemeHttps)
                errors.Add("consumer.package-feed-invalid:" + label);
        }

        if (!evidence.RestorePassed)
            errors.Add("consumer.restore-failed");
        if (!evidence.BuildPassed)
            errors.Add("consumer.build-failed");
        if (!evidence.TestsPassed)
            errors.Add("consumer.tests-failed");

        var state = errors.Any(error => error == "consumer.candidate-identity-mismatch" ||
                                        error == "consumer.workflow-identity-missing" ||
                                        error.StartsWith("consumer.package-resolution-ambiguous:", StringComparison.Ordinal) ||
                                        error.StartsWith("consumer.package-feed-invalid:", StringComparison.Ordinal))
            ? ConsumerVerificationState.Blocked
            : errors.Count > 0 || missing.Count > 0
                ? ConsumerVerificationState.VerificationFailed
                : ConsumerVerificationState.ReadyForReleaseReview;

        return new ConsumerVerificationEvaluation(
            candidate.CandidateId, candidate.Repository, candidate.CommitSha, state,
            missing.AsReadOnly(), errors.AsReadOnly());
    }
}
