using NuGet.Versioning;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Compares published NuGet versions and derives a conservative update classification.</summary>
/// <remarks>
/// Version parsing and precedence follow NuGet's own <see cref="NuGetVersion"/> and
/// <see cref="VersionComparer"/> implementation. Unsupported input is rejected rather than guessed.
/// Classification is not compatibility evidence.
/// </remarks>
public static class NuGetVersionUpdateClassifier
{
    /// <summary>Creates a policy candidate from version strings instead of trusting caller-supplied ordering.</summary>
    public static DependencyUpdateCandidate CreateCandidate(string packageId, string currentVersion, string candidateVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!string.Equals(packageId, packageId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Package ID cannot have leading or trailing whitespace.", nameof(packageId));

        var current = ParseVersion(currentVersion, nameof(currentVersion));
        var candidate = ParseVersion(candidateVersion, nameof(candidateVersion));
        var comparison = VersionComparer.VersionRelease.Compare(candidate, current);
        var scope = Classify(current, candidate, comparison);

        return new DependencyUpdateCandidate(
            packageId,
            currentVersion,
            candidateVersion,
            scope,
            comparison > 0,
            candidate.IsPrerelease);
    }

    private static NuGetVersion ParseVersion(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !NuGetVersion.TryParse(value, out var parsed) ||
            parsed is null)
        {
            throw new ArgumentException("Version is not a supported NuGet version.", parameterName);
        }

        return parsed;
    }

    private static DependencyUpdateScope Classify(NuGetVersion current, NuGetVersion candidate, int comparison)
    {
        if (comparison <= 0)
            return DependencyUpdateScope.Unknown;

        if (candidate.Major != current.Major)
            return DependencyUpdateScope.Major;
        if (candidate.Minor != current.Minor)
            return DependencyUpdateScope.Minor;
        if (candidate.Patch != current.Patch)
            return DependencyUpdateScope.Patch;

        // A prerelease-to-prerelease advance or prerelease-to-stable promotion within the
        // same numeric release lane is a patch-lane update. Revision-only changes remain unknown.
        if (candidate.Revision == current.Revision && (candidate.IsPrerelease || current.IsPrerelease))
            return DependencyUpdateScope.Patch;

        // Revision-only changes and other differences outside policy lanes must not be guessed.
        return DependencyUpdateScope.Unknown;
    }
}
