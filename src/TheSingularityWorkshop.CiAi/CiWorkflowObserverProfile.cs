namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Declares the external workflow/artifact shape expected by an evidence observer.
/// A profile is configuration, not evidence and not a trust decision.
/// </summary>
public sealed record CiWorkflowObserverProfile
{
    public CiWorkflowObserverProfile(
        string id,
        string workflowPath,
        string provenanceArtifactNameTemplate,
        string testArtifactNameTemplate,
        string provenanceEntryName,
        string trxEntryName,
        string testProviderLabel,
        string requiredJobKey,
        string requiredJobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ValidateWorkflowPath(workflowPath);
        ValidateArtifactTemplate(provenanceArtifactNameTemplate, nameof(provenanceArtifactNameTemplate));
        ValidateArtifactTemplate(testArtifactNameTemplate, nameof(testArtifactNameTemplate));
        ValidateLeafName(provenanceEntryName, nameof(provenanceEntryName));
        ValidateLeafName(trxEntryName, nameof(trxEntryName));
        RequireText(testProviderLabel, nameof(testProviderLabel), 100);
        RequireIdentifier(requiredJobKey, nameof(requiredJobKey), 100);
        RequireText(requiredJobName, nameof(requiredJobName), 200);
        if (id.Length > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new ArgumentException("Profile ID is invalid.", nameof(id));

        Id = id;
        WorkflowPath = workflowPath;
        ProvenanceArtifactNameTemplate = provenanceArtifactNameTemplate;
        TestArtifactNameTemplate = testArtifactNameTemplate;
        ProvenanceEntryName = provenanceEntryName;
        TrxEntryName = trxEntryName;
        TestProviderLabel = testProviderLabel;
        RequiredJobKey = requiredJobKey;
        RequiredJobName = requiredJobName;
    }

    public string Id { get; }
    public string WorkflowPath { get; }
    public string ProvenanceArtifactNameTemplate { get; }
    public string TestArtifactNameTemplate { get; }
    public string ProvenanceEntryName { get; }
    public string TrxEntryName { get; }
    public string TestProviderLabel { get; }
    public string RequiredJobKey { get; }
    public string RequiredJobName { get; }

    public string ProvenanceArtifactName(long runId, int attempt)
        => Expand(ProvenanceArtifactNameTemplate, runId, attempt);

    public string TestArtifactName(long runId, int attempt)
        => Expand(TestArtifactNameTemplate, runId, attempt);

    /// <summary>Creates the exact required-job policy for one source run attempt.</summary>
    public CiRequiredJobPolicy CreateRequiredJobPolicy(long runId, int attempt)
        => new(new[]
        {
            new CiRequiredJobDefinition(
                WorkflowPath,
                RequiredJobKey,
                RequiredJobName,
                TestArtifactName(runId, attempt))
        });

    public static CiWorkflowObserverProfile CiAiDefault { get; } = new(
        "ciai-default",
        ".github/workflows/ci.yml",
        "ciai-workflow-provenance-{attempt}",
        "ciai-test-results-{attempt}",
        "workflow-provenance.json",
        "results.trx",
        "GitHub Actions",
        "contract",
        "contract");

    public static CiWorkflowObserverProfile RaWWar { get; } = new(
        "rawwar-contracts-v1",
        ".github/workflows/build.yml",
        "rawwar-workflow-provenance-{attempt}",
        "rawwar-contract-results-{runId}-{attempt}",
        "workflow-provenance.json",
        "results.trx",
        "GitHub Actions",
        "build-and-contracts",
        "build-and-contracts");

    public static CiWorkflowObserverProfile AnyApp { get; } = new(
        "anyapp-development-v1",
        ".github/workflows/dotnet.yml",
        "ciai-workflow-provenance-{runId}-{attempt}",
        "ciai-test-results-{runId}-{attempt}-build",
        "ciai-workflow-provenance.json",
        "anyapp.trx",
        "AnyApp .NET tests",
        "build",
        "build");

    public static CiWorkflowObserverProfile Resolve(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id switch
        {
            "ciai-default" => CiAiDefault,
            "rawwar-contracts-v1" => RaWWar,
            "anyapp-development-v1" => AnyApp,
            _ => throw new InvalidDataException($"Unknown built-in CiAi observer profile '{id}'.")
        };
    }

    private static string Expand(string template, long runId, int attempt)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (attempt <= 0) throw new ArgumentOutOfRangeException(nameof(attempt));
        return template.Replace("{runId}", runId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{attempt}", attempt.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static void ValidateWorkflowPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = value.Split('/');
        if (value.Length > 512 || !value.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(value.EndsWith(".yml", StringComparison.Ordinal) || value.EndsWith(".yaml", StringComparison.Ordinal)) ||
            value.Contains('\\') || value.Contains(':') || value.Contains('?') || value.Contains('#') ||
            parts.Any(part => part.Length == 0 || part is "." or ".."))
            throw new ArgumentException("Workflow path must be a safe path under .github/workflows.", nameof(value));
    }

    private static void ValidateArtifactTemplate(string value, string parameter)
    {
        RequireText(value, parameter, 200);
        var candidate = value.Replace("{runId}", "123", StringComparison.Ordinal)
            .Replace("{attempt}", "1", StringComparison.Ordinal);
        if (candidate.Contains('{') || candidate.Contains('}') ||
            candidate.Contains('/') || candidate.Contains('\\') ||
            candidate.Any(char.IsControl))
            throw new ArgumentException("Artifact template contains an unsupported placeholder or path syntax.", parameter);
    }

    private static void ValidateLeafName(string value, string parameter)
    {
        RequireText(value, parameter, 120);
        if (value.Contains('/') || value.Contains('\\') || value is "." or "..")
            throw new ArgumentException("Artifact entry name must be a leaf filename.", parameter);
    }

    private static void RequireText(string value, string parameter, int max)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > max || value.Any(char.IsControl))
            throw new ArgumentException("Value is invalid or too long.", parameter);
    }

    private static void RequireIdentifier(string value, string parameter, int max)
    {
        RequireText(value, parameter, max);
        if (value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Identifier cannot contain whitespace.", parameter);
    }
}
