using System.Globalization;
using System.Text.RegularExpressions;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Compares published NuGet versions and derives a conservative update classification.</summary>
/// <remarks>
/// This classifier accepts numeric NuGet versions with up to four release components, optional
/// prerelease labels, and optional build metadata. Invalid or unsupported version syntax is rejected
/// rather than guessed. Classification is not compatibility evidence.
/// </remarks>
public static class NuGetVersionUpdateClassifier
{
    private static readonly Regex VersionPattern = new(
        @"^(?<core>0|[1-9][0-9]*)(?:\.(?<minor>0|[1-9][0-9]*))?(?:\.(?<patch>0|[1-9][0-9]*))?(?:\.(?<revision>0|[1-9][0-9]*))?(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Creates a policy candidate from version strings instead of trusting caller-supplied ordering.</summary>
    public static DependencyUpdateCandidate CreateCandidate(string packageId, string currentVersion, string candidateVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!string.Equals(packageId, packageId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Package ID cannot have leading or trailing whitespace.", nameof(packageId));

        var current = ParsedVersion.Parse(currentVersion, nameof(currentVersion));
        var candidate = ParsedVersion.Parse(candidateVersion, nameof(candidateVersion));
        var comparison = candidate.CompareTo(current);
        var scope = Classify(current, candidate, comparison);

        return new DependencyUpdateCandidate(
            packageId,
            currentVersion,
            candidateVersion,
            scope,
            comparison > 0,
            candidate.IsPrerelease);
    }

    private static DependencyUpdateScope Classify(ParsedVersion current, ParsedVersion candidate, int comparison)
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

    private sealed record ParsedVersion(
        long Major, long Minor, long Patch, long Revision, string[] Prerelease, string? Build)
        : IComparable<ParsedVersion>
    {
        public bool IsPrerelease => Prerelease.Length > 0;

        public static ParsedVersion Parse(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("Version must be non-empty and have no surrounding whitespace.", parameterName);

            var match = VersionPattern.Match(value);
            if (!match.Success)
                throw new ArgumentException("Version is not a supported numeric NuGet version.", parameterName);

            static long Number(Group group) =>
                group.Success ? long.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture) : 0;

            var prerelease = match.Groups["pre"].Success
                ? match.Groups["pre"].Value.Split('.')
                : Array.Empty<string>();

            foreach (var identifier in prerelease)
            {
                if (identifier.Length == 0 ||
                    (identifier.Length > 1 && identifier.All(char.IsAsciiDigit) && identifier[0] == '0'))
                    throw new ArgumentException("Prerelease identifiers cannot contain leading zeroes when numeric.", parameterName);
            }

            return new ParsedVersion(
                Number(match.Groups["core"]),
                Number(match.Groups["minor"]),
                Number(match.Groups["patch"]),
                Number(match.Groups["revision"]),
                prerelease,
                match.Groups["build"].Success ? match.Groups["build"].Value : null);
        }

        public int CompareTo(ParsedVersion? other)
        {
            if (other is null) return 1;
            var result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;
            result = Revision.CompareTo(other.Revision);
            if (result != 0) return result;

            if (!IsPrerelease) return other.IsPrerelease ? 1 : 0;
            if (!other.IsPrerelease) return -1;

            for (var i = 0; i < Math.Min(Prerelease.Length, other.Prerelease.Length); i++)
            {
                var left = Prerelease[i];
                var right = other.Prerelease[i];
                var leftNumeric = left.All(char.IsAsciiDigit);
                var rightNumeric = right.All(char.IsAsciiDigit);
                if (leftNumeric && rightNumeric)
                {
                    var numericResult = CompareNumericIdentifiers(left, right);
                    if (numericResult != 0) return numericResult;
                }
                else if (leftNumeric != rightNumeric)
                {
                    return leftNumeric ? -1 : 1;
                }
                else
                {
                    var textResult = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
                    if (textResult != 0) return textResult;
                }
            }

            return Prerelease.Length.CompareTo(other.Prerelease.Length);
        }

        private static int CompareNumericIdentifiers(string left, string right)
        {
            var leftTrimmed = left.TrimStart('0');
            var rightTrimmed = right.TrimStart('0');
            var lengthResult = leftTrimmed.Length.CompareTo(rightTrimmed.Length);
            return lengthResult != 0 ? lengthResult :
                string.Compare(leftTrimmed, rightTrimmed, StringComparison.Ordinal);
        }
    }
}
