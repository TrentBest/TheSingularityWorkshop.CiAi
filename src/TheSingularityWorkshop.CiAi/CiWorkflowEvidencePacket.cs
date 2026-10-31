using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Versioned, immutable evidence for one exact GitHub Actions workflow run attempt.
/// Workflow-definition provenance and candidate-code provenance are distinct when a trusted
/// workflow checks out a separately authorized feature-branch commit.
/// </summary>
public sealed record CiWorkflowEvidencePacket
{
    public const int CurrentContractVersion = 2;
    public const string CurrentSchema = "ciai.workflow-evidence.v2";

    public CiWorkflowEvidencePacket(
        string repository,
        string workflowPath,
        long runId,
        int runAttempt,
        string workflowRef,
        string workflowSha,
        string @event,
        string workflowName,
        CiRunStatus status,
        CiRunConclusion conclusion,
        Uri runEvidenceUrl,
        DateTimeOffset observedAt,
        IReadOnlyList<CiWorkflowJobObservation> jobs,
        bool discoveryComplete,
        bool jobsComplete,
        IReadOnlyList<Uri>? artifactReferences = null,
        CiWorkflowCandidateCheckoutEvidence? candidateCheckoutEvidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowSha);
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
        if (workflowSha.Length != 40 || !workflowSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Workflow SHA must be a full 40-character hexadecimal commit identifier.", nameof(workflowSha));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(conclusion)) throw new ArgumentOutOfRangeException(nameof(conclusion));
        if ((status == CiRunStatus.Completed) == (conclusion == CiRunConclusion.None))
            throw new ArgumentException("Completed runs require a conclusion; non-completed runs must not have one.", nameof(conclusion));

        if (candidateCheckoutEvidence is not null &&
            !string.Equals(workflowRef, CiWorkflowDispatchInputContract.TrustedWorkflowRef, StringComparison.Ordinal))
            throw new ArgumentException("Separate candidate checkout evidence requires the trusted workflow-definition ref.", nameof(workflowRef));
        if (candidateCheckoutEvidence is not null &&
            !string.Equals(@event, "workflow_dispatch", StringComparison.Ordinal))
            throw new ArgumentException("Separate candidate checkout evidence requires a trusted workflow_dispatch run.", nameof(@event));

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
        WorkflowRef = workflowRef;
        WorkflowSha = workflowSha.ToLowerInvariant();
        CandidateCheckoutEvidence = candidateCheckoutEvidence;
        CandidateRef = candidateCheckoutEvidence?.CandidateRef ?? WorkflowRef;
        CandidateSha = candidateCheckoutEvidence?.CandidateSha ?? WorkflowSha;
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
        CandidateCheckoutVerified = IsCandidateCheckoutVerified(candidateCheckoutEvidence, Jobs);
    }

    public int ContractVersion { get; }
    public string Schema { get; }
    public string Repository { get; }
    public string WorkflowPath { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public string WorkflowRef { get; }
    public string WorkflowSha { get; }
    public string CandidateRef { get; }
    public string CandidateSha { get; }
    public CiWorkflowCandidateCheckoutEvidence? CandidateCheckoutEvidence { get; }
    public bool CandidateCheckoutVerified { get; }
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

    /// <summary>Compatibility aliases for GitHub's run-level head fields; these are workflow provenance, not candidate provenance.</summary>
    [JsonIgnore]
    public string HeadRef => WorkflowRef;

    [JsonIgnore]
    public string HeadSha => WorkflowSha;

    /// <summary>True only when discovery and job collection are complete and the run is terminal.</summary>
    public bool EvidenceComplete =>
        DiscoveryComplete && JobsComplete && Jobs.Count > 0 &&
        Status == CiRunStatus.Completed && Conclusion != CiRunConclusion.None;

    /// <summary>
    /// True only when the workflow succeeded, every observed job succeeded, and the code-under-test
    /// candidate is either the workflow event commit or has explicit successful checkout provenance.
    /// </summary>
    public bool IsSuccessful =>
        EvidenceComplete && CandidateCheckoutVerified &&
        Conclusion == CiRunConclusion.Success &&
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
            if (!CandidateCheckoutVerified) reasons.Add("candidate.checkout-unverified");
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

        var packet = JsonSerializer.Deserialize<CiWorkflowEvidencePacket>(json, JsonOptions(false))
            ?? throw new InvalidDataException("Evidence packet JSON produced no packet.");
        if (!HasMatchingString(root, "workflowRef", packet.WorkflowRef, ignoreCase: false) ||
            !HasMatchingString(root, "workflowSha", packet.WorkflowSha, ignoreCase: true) ||
            !HasMatchingString(root, "candidateRef", packet.CandidateRef, ignoreCase: false) ||
            !HasMatchingString(root, "candidateSha", packet.CandidateSha, ignoreCase: true) ||
            !root.TryGetProperty("candidateCheckoutVerified", out var verified) ||
            verified.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            verified.GetBoolean() != packet.CandidateCheckoutVerified)
            throw new InvalidDataException("Evidence packet flattened workflow/candidate provenance does not match its validated nested evidence.");
        return packet;
    }

    private static bool HasMatchingString(
        JsonElement root,
        string propertyName,
        string expected,
        bool ignoreCase)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        return string.Equals(value.GetString(), expected,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsCandidateCheckoutVerified(
        CiWorkflowCandidateCheckoutEvidence? evidence,
        IReadOnlyList<CiWorkflowJobObservation> jobs)
    {
        if (evidence is null)
            return true; // Ordinary CI run: candidate is the workflow event commit itself.

        var matchingJobs = jobs.Where(job =>
            string.Equals(job.Name, evidence.JobName, StringComparison.Ordinal)).ToArray();
        if (matchingJobs.Length != 1)
            return false;

        var job = matchingJobs[0];
        // Checkout provenance is independent from later build/test outcome. A failed or
        // cancelled job can still prove which candidate was checked out before the failure.

        var resolutionSteps = job.Steps.Where(step =>
            string.Equals(step.Name, evidence.ResolutionStepName, StringComparison.Ordinal)).ToArray();
        var verificationSteps = job.Steps.Where(step =>
            string.Equals(step.Name, evidence.VerificationStepName, StringComparison.Ordinal)).ToArray();

        return resolutionSteps.Length == 1 &&
            resolutionSteps[0].Status == "completed" &&
            resolutionSteps[0].Conclusion == "success" &&
            verificationSteps.Length == 1 &&
            verificationSteps[0].Status == "completed" &&
            verificationSteps[0].Conclusion == "success";
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
