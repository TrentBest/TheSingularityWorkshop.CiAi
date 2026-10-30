using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Versioned, immutable evidence for one exact GitHub Actions workflow run attempt.
/// The packet preserves provider facts; it does not authorize a repair, merge, release, or publication.
/// </summary>
public sealed record CiWorkflowEvidencePacket
{
    public const int CurrentContractVersion = 1;
    public const string CurrentSchema = "ciai.workflow-evidence.v1";

    public CiWorkflowEvidencePacket(
        string repository,
        string workflowPath,
        long runId,
        int runAttempt,
        string headRef,
        string headSha,
        string @event,
        string workflowName,
        CiRunStatus status,
        CiRunConclusion conclusion,
        Uri runEvidenceUrl,
        DateTimeOffset observedAt,
        IReadOnlyList<CiWorkflowJobObservation> jobs,
        bool discoveryComplete,
        bool jobsComplete,
        IReadOnlyList<Uri>? artifactReferences = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(headRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentNullException.ThrowIfNull(runEvidenceUrl);
        ArgumentNullException.ThrowIfNull(jobs);

        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (!IsWorkflowPath(workflowPath))
            throw new ArgumentException("Workflow path must identify a .yml or .yaml file under .github/workflows.", nameof(workflowPath));
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (runAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(runAttempt));
        if (headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Head SHA must be a full 40-character hexadecimal commit identifier.", nameof(headSha));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(conclusion)) throw new ArgumentOutOfRangeException(nameof(conclusion));
        if ((status == CiRunStatus.Completed) == (conclusion == CiRunConclusion.None))
            throw new ArgumentException("Completed runs require a conclusion; non-completed runs must not have one.", nameof(conclusion));

        var expectedRunPath = $"/{repository}/actions/runs/{runId}";
        if (!IsSafeGitHubUrl(runEvidenceUrl) ||
            !string.Equals(runEvidenceUrl.AbsolutePath, expectedRunPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Run evidence URL must identify this exact GitHub Actions run.", nameof(runEvidenceUrl));

        var jobArray = jobs.ToArray();
        if (jobArray.Any(job => job is null))
            throw new ArgumentException("Job evidence cannot contain null entries.", nameof(jobs));
        if (jobArray.Any(job => job.RunId != runId || job.RunAttempt != runAttempt))
            throw new ArgumentException("Every job must belong to this exact run and attempt.", nameof(jobs));
        if (jobArray.Any(job => !IsSafeGitHubUrl(job.EvidenceUrl) ||
            !job.EvidenceUrl.AbsolutePath.StartsWith(expectedRunPath + "/job/", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Every job evidence URL must belong to this exact workflow run.", nameof(jobs));
        if (jobArray.Select(job => job.JobId).Distinct().Count() != jobArray.Length)
            throw new ArgumentException("Job IDs must be unique within a run attempt.", nameof(jobs));

        var artifacts = (artifactReferences ?? Array.Empty<Uri>()).ToArray();
        if (artifacts.Any(uri => uri is null || !IsSafeGitHubUrl(uri)))
            throw new ArgumentException("Artifact references must be safe HTTPS GitHub URLs.", nameof(artifactReferences));

        ContractVersion = CurrentContractVersion;
        Schema = CurrentSchema;
        Repository = repository;
        WorkflowPath = workflowPath;
        RunId = runId;
        RunAttempt = runAttempt;
        HeadRef = headRef;
        HeadSha = headSha.ToLowerInvariant();
        Event = @event;
        WorkflowName = workflowName;
        Status = status;
        Conclusion = conclusion;
        RunEvidenceUrl = runEvidenceUrl;
        ObservedAt = observedAt;
        Jobs = Array.AsReadOnly(jobArray.OrderBy(job => job.JobId).ToArray());
        DiscoveryComplete = discoveryComplete;
        JobsComplete = jobsComplete;
        ArtifactReferences = Array.AsReadOnly(artifacts);
    }

    public int ContractVersion { get; }
    public string Schema { get; }
    public string Repository { get; }
    public string WorkflowPath { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public string HeadRef { get; }
    public string HeadSha { get; }
    public string Event { get; }
    public string WorkflowName { get; }
    public CiRunStatus Status { get; }
    public CiRunConclusion Conclusion { get; }
    public Uri RunEvidenceUrl { get; }
    public DateTimeOffset ObservedAt { get; }
    public IReadOnlyList<CiWorkflowJobObservation> Jobs { get; }
    public bool DiscoveryComplete { get; }
    public bool JobsComplete { get; }
    public IReadOnlyList<Uri> ArtifactReferences { get; }

    /// <summary>True only when discovery and job collection are complete and the run is terminal.</summary>
    public bool EvidenceComplete =>
        DiscoveryComplete && JobsComplete && Jobs.Count > 0 &&
        Status == CiRunStatus.Completed && Conclusion != CiRunConclusion.None;

    /// <summary>Policy-neutral provider outcome; skipped, cancelled, unknown, and incomplete remain non-success.</summary>
    public bool IsSuccessful =>
        EvidenceComplete && Conclusion == CiRunConclusion.Success &&
        Jobs.All(job => job.Status == "completed" && job.Conclusion == "success");

    /// <summary>Stable, machine-readable reasons why this packet cannot be treated as complete success.</summary>
    public IReadOnlyList<string> BlockingReasons
    {
        get
        {
            var reasons = new List<string>();
            if (!DiscoveryComplete) reasons.Add("evidence.discovery-incomplete");
            if (!JobsComplete) reasons.Add("evidence.jobs-incomplete");
            if (Jobs.Count == 0) reasons.Add("evidence.jobs-missing");
            if (Status != CiRunStatus.Completed) reasons.Add("run.not-terminal");
            if (Status == CiRunStatus.Completed && Conclusion != CiRunConclusion.Success)
                reasons.Add("run.not-successful");
            foreach (var job in Jobs)
            {
                if (job.Status != "completed") reasons.Add($"job.{job.JobId}.not-terminal");
                else if (job.Conclusion != "success") reasons.Add($"job.{job.JobId}.not-successful");
                // Step outcomes are preserved verbatim. A skipped or continue-on-error step is
                // not independently a failed workflow; policy must identify required steps.
            }
            return Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>Serializes a stable versioned JSON contract suitable for durable evidence storage.</summary>
    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, JsonOptions(indented));

    public static CiWorkflowEvidencePacket FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("contractVersion", out var version) ||
            !version.TryGetInt32(out var parsedVersion) ||
            !root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.String ||
            parsedVersion != CurrentContractVersion ||
            !string.Equals(schema.GetString(), CurrentSchema, StringComparison.Ordinal))
            throw new InvalidDataException("Evidence packet contract version or schema is unsupported.");

        return JsonSerializer.Deserialize<CiWorkflowEvidencePacket>(json, JsonOptions(false))
            ?? throw new InvalidDataException("Evidence packet JSON produced no packet.");
    }

    private static JsonSerializerOptions JsonOptions(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static bool IsWorkflowPath(string path) =>
        path.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
        (path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal)) &&
        !path.Contains('\\') && !path.Contains('#') && !path.Contains('?') &&
        path.Split('/').Length >= 3 &&
        path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    private static bool IsSafeGitHubUrl(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);
}
