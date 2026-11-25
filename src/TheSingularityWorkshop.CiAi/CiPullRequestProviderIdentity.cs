namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Provider-observed pull-request identity. The host must populate this from GitHub's pull-request
/// and commit APIs, not from the candidate workflow's provenance sidecar.
/// </summary>
public sealed record CiPullRequestProviderIdentity
{
    public CiPullRequestProviderIdentity(
        int number,
        string headRef,
        string headSha,
        string baseRef,
        string baseSha,
        string mergeCommitSha,
        IReadOnlyList<string> mergeParentShas)
    {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        ArgumentException.ThrowIfNullOrWhiteSpace(headRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRef);
        ValidateSha(headSha, nameof(headSha));
        ValidateSha(baseSha, nameof(baseSha));
        ValidateSha(mergeCommitSha, nameof(mergeCommitSha));
        ArgumentNullException.ThrowIfNull(mergeParentShas);
        var parents = mergeParentShas.ToArray();
        if (parents.Length != 2 || parents.Any(parent => !IsSha(parent)) ||
            parents.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            throw new ArgumentException("A GitHub pull-request merge commit must be independently observed with exactly two distinct parent SHAs.", nameof(mergeParentShas));
        if (!parents.Contains(headSha, StringComparer.OrdinalIgnoreCase) ||
            !parents.Contains(baseSha, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Merge commit parents must identify the independently observed PR head and base SHAs.", nameof(mergeParentShas));

        Number = number;
        HeadRef = headRef;
        HeadSha = headSha.ToLowerInvariant();
        BaseRef = baseRef;
        BaseSha = baseSha.ToLowerInvariant();
        MergeCommitSha = mergeCommitSha.ToLowerInvariant();
        MergeParentShas = Array.AsReadOnly(parents.Select(parent => parent.ToLowerInvariant()).ToArray());
    }

    public int Number { get; }
    public string HeadRef { get; }
    public string HeadSha { get; }
    public string BaseRef { get; }
    public string BaseSha { get; }
    public string MergeCommitSha { get; }
    public IReadOnlyList<string> MergeParentShas { get; }

    public bool Matches(CiProviderPullRequestObservation observation)
        => observation is not null &&
           observation.Number == Number &&
           string.Equals(observation.HeadRef, HeadRef, StringComparison.Ordinal) &&
           string.Equals(observation.HeadSha, HeadSha, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(observation.BaseRef, BaseRef, StringComparison.Ordinal) &&
           string.Equals(observation.BaseSha, BaseSha, StringComparison.OrdinalIgnoreCase);

    public bool Matches(CiWorkflowProvenanceRecord provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        var pr = provenance.PullRequest;
        return pr is not null &&
            pr.Number == Number &&
            string.Equals(pr.HeadRef, HeadRef, StringComparison.Ordinal) &&
            string.Equals(pr.HeadSha, HeadSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pr.BaseRef, BaseRef, StringComparison.Ordinal) &&
            string.Equals(pr.BaseSha, BaseSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(provenance.EventName, "pull_request", StringComparison.Ordinal) &&
            string.Equals(provenance.EventSha, MergeCommitSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(provenance.CheckoutSha, MergeCommitSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(provenance.EventRef, $"refs/pull/{Number}/merge", StringComparison.Ordinal) &&
            string.Equals(provenance.CheckoutRef, provenance.EventRef, StringComparison.Ordinal);
    }

    private static bool IsSha(string? value)
        => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static void ValidateSha(string value, string parameter)
    {
        if (!IsSha(value))
            throw new ArgumentException("Commit identity must be a full 40-character SHA.", parameter);
    }
}
