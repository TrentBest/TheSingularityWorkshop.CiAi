namespace TheSingularityWorkshop.CiAi;

/// <summary>Evaluates one dependency update from a read-only NuGet version observation.</summary>
/// <remarks>
/// The evaluator chooses the highest version it can compare from the observed feed, then applies
/// the configured policy to that candidate. It does not edit dependency declarations, run builds,
/// establish compatibility, approve adoption, or publish packages.
/// </remarks>
public sealed class NuGetDependencyUpdateEvaluator
{
    private readonly INuGetVersionCatalog _catalog;

    public NuGetDependencyUpdateEvaluator(INuGetVersionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>
    /// Observes published versions and evaluates the highest observed version against the current
    /// version and policy. Any unsupported version string makes the observation non-decisive.
    /// </summary>
    public async Task<NuGetDependencyUpdateEvaluation> EvaluateAsync(
        DependencyUpdatePolicy policy,
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(currentVersion) ||
            !string.Equals(currentVersion, currentVersion.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Current version must be non-empty and have no surrounding whitespace.",
                nameof(currentVersion));

        // Validate the current version before making a network request.
        _ = NuGetVersionUpdateClassifier.CreateCandidate(policy.PackageId, currentVersion, currentVersion);
        var observation = await _catalog.ObserveAsync(policy.PackageId, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(observation.PackageId, policy.PackageId, StringComparison.Ordinal))
            return NuGetDependencyUpdateEvaluation.Blocked(observation,
                "dependency.catalog-package-id-mismatch");

        if (observation.Versions.Count == 0)
            return NuGetDependencyUpdateEvaluation.Blocked(observation,
                "dependency.catalog-version-list-empty");

        string? highestVersion = null;
        foreach (var version in observation.Versions)
        {
            DependencyUpdateCandidate candidate;
            try
            {
                candidate = NuGetVersionUpdateClassifier.CreateCandidate(policy.PackageId, currentVersion, version);
            }
            catch (ArgumentException)
            {
                // We cannot prove this is not the newest release, so do not silently skip it.
                return NuGetDependencyUpdateEvaluation.Blocked(observation,
                    "dependency.catalog-contains-unsupported-version");
            }

            if (!candidate.IsNewerVersion)
                continue;

            if (highestVersion is null)
            {
                highestVersion = version;
                continue;
            }

            DependencyUpdateCandidate comparison;
            try
            {
                comparison = NuGetVersionUpdateClassifier.CreateCandidate(policy.PackageId, highestVersion, version);
            }
            catch (ArgumentException)
            {
                return NuGetDependencyUpdateEvaluation.Blocked(observation,
                    "dependency.catalog-contains-unsupported-version");
            }

            if (comparison.IsNewerVersion)
                highestVersion = version;
        }

        if (highestVersion is null)
        {
            var unchanged = NuGetVersionUpdateClassifier.CreateCandidate(
                policy.PackageId, currentVersion, currentVersion);
            return new NuGetDependencyUpdateEvaluation(observation, unchanged,
                new DependencyUpdatePolicyDecision(DependencyUpdateDisposition.Blocked,
                    "dependency.no-newer-version-observed"));
        }

        var selected = NuGetVersionUpdateClassifier.CreateCandidate(policy.PackageId, currentVersion, highestVersion);
        return new NuGetDependencyUpdateEvaluation(observation, selected, policy.Evaluate(selected));
    }
}

/// <summary>Connects the exact feed observation, selected candidate, and policy decision.</summary>
public sealed record NuGetDependencyUpdateEvaluation(
    NuGetVersionCatalogObservation Observation,
    DependencyUpdateCandidate Candidate,
    DependencyUpdatePolicyDecision Decision)
{
    internal static NuGetDependencyUpdateEvaluation Blocked(
        NuGetVersionCatalogObservation observation,
        string reasonCode)
    {
        var candidate = new DependencyUpdateCandidate(
            observation.PackageId, string.Empty, string.Empty, DependencyUpdateScope.Unknown,
            IsNewerVersion: false, IsPrerelease: false);
        return new NuGetDependencyUpdateEvaluation(observation, candidate,
            new DependencyUpdatePolicyDecision(DependencyUpdateDisposition.Blocked, reasonCode));
    }
}
