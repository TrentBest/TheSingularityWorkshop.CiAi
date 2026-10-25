namespace TheSingularityWorkshop.CiAi;

/// <summary>Deterministic verification that a packed NuGet candidate contains its expected package outputs.</summary>
public static class NuGetPackageArtifactVerifier
{
    /// <summary>
    /// Verifies that both the regular package and the NuGet symbol package were preserved in the inspected
    /// artifact inventory. This checks names only; callers must separately inspect package contents and provenance.
    /// </summary>
    public static NuGetPackageArtifactReport Verify(
        string packageId,
        string version,
        IEnumerable<string> observedRelativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(observedRelativePaths);

        ValidateFileNameSegment(packageId, nameof(packageId));
        ValidateFileNameSegment(version, nameof(version));

        var observed = observedRelativePaths
            .Select(NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var expected = new[]
        {
            $"{packageId}.{version}.nupkg",
            $"{packageId}.{version}.snupkg"
        };

        var observedNames = new HashSet<string>(
            observed.Select(path => path[(path.LastIndexOf('/') + 1)..]),
            StringComparer.OrdinalIgnoreCase);
        var missing = expected.Where(path => !observedNames.Contains(path)).ToArray();
        return new NuGetPackageArtifactReport(packageId, version, expected, observed, missing);
    }

    private static void ValidateFileNameSegment(string value, string parameterName)
    {
        if (value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains('/') || value.Contains('\\') ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException("Value must be a single safe filename segment.", parameterName);
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/', StringComparison.Ordinal) ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
            throw new ArgumentException("Artifact paths must be relative.", nameof(path));

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
                                    segment.Any(char.IsControl)))
            throw new ArgumentException("Artifact paths must not contain empty, dot, or parent segments.", nameof(path));

        return string.Join('/', segments);
    }
}

/// <summary>The result of checking required NuGet package outputs in an artifact inventory.</summary>
public sealed record NuGetPackageArtifactReport
{
    internal NuGetPackageArtifactReport(
        string packageId,
        string version,
        IReadOnlyList<string> expectedPaths,
        IReadOnlyList<string> observedPaths,
        IReadOnlyList<string> missingPaths)
    {
        PackageId = packageId;
        Version = version;
        ExpectedPaths = Array.AsReadOnly(expectedPaths.ToArray());
        ObservedPaths = Array.AsReadOnly(observedPaths.ToArray());
        MissingPaths = Array.AsReadOnly(missingPaths.ToArray());
    }

    public string PackageId { get; }
    public string Version { get; }
    public IReadOnlyList<string> ExpectedPaths { get; }
    public IReadOnlyList<string> ObservedPaths { get; }
    public IReadOnlyList<string> MissingPaths { get; }
    public bool IsComplete => MissingPaths.Count == 0;

    /// <summary>Returns a stable, human-readable diagnostic suitable for CI logs.</summary>
    public string ToReport()
    {
        var lines = new List<string>
        {
            $"NuGet artifact inventory: {PackageId} {Version}",
            $"Status: {(IsComplete ? "complete" : "incomplete")}"
        };

        lines.AddRange(ExpectedPaths.Select(path =>
            $"{(MissingPaths.Contains(path, StringComparer.OrdinalIgnoreCase) ? "MISSING" : "FOUND")}: {path}"));

        return string.Join(Environment.NewLine, lines);
    }
}
