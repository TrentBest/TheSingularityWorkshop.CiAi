namespace TheSingularityWorkshop.CiAi;

/// <summary>One required job, scoped to an exact workflow path and its YAML job key/name.</summary>
public sealed record CiRequiredJobDefinition
{
    public CiRequiredJobDefinition(string workflowPath, string jobKey, string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        if (!IsValidWorkflowPath(workflowPath))
            throw new ArgumentException("Required workflow path must be a canonical .github/workflows YAML path.", nameof(workflowPath));
        if (jobKey.Length > 100 ||
            !jobKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new ArgumentException("Workflow job key contains unsupported characters.", nameof(jobKey));
        if (jobName.Length > 200 || jobName.Any(char.IsControl))
            throw new ArgumentException("Workflow job name must be at most 200 characters without control characters.", nameof(jobName));
        WorkflowPath = workflowPath;
        JobKey = jobKey;
        JobName = jobName;
    }

    public string WorkflowPath { get; }
    public string JobKey { get; }
    public string JobName { get; }

    private static bool IsValidWorkflowPath(string path)
    {
        if (!path.StartsWith(".github/workflows/", StringComparison.Ordinal) ||
            !(path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal)) ||
            path.Contains('\\') || path.Contains('#') || path.Contains('?') || path.Contains(':'))
            return false;
        var segments = path.Split('/');
        return segments.Length >= 3 &&
            segments.All(segment => segment.Length > 0 && segment != "." && segment != ".." &&
                segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }
}

/// <summary>
/// CiAi-owned policy that maps exact workflow paths and job keys to observed job names, then
/// derives required-check status from the validated v3 run/job evidence.
/// </summary>
public sealed class CiRequiredJobPolicy
{
    private readonly IReadOnlyDictionary<string, string> _requiredJobs;
    private readonly IReadOnlyList<CiRequiredJobDefinition> _definitions;

    public CiRequiredJobPolicy(IEnumerable<CiRequiredJobDefinition> requiredJobs)
    {
        ArgumentNullException.ThrowIfNull(requiredJobs);
        var definitions = requiredJobs.ToArray();
        if (definitions.Any(item => item is null))
            throw new ArgumentException("Required job policy cannot contain null definitions.", nameof(requiredJobs));
        if (definitions.Select(item => GetKey(item.WorkflowPath, item.JobKey))
            .Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new ArgumentException("Required workflow path/job key pairs must be unique.", nameof(requiredJobs));
        _definitions = Array.AsReadOnly(definitions);
        _requiredJobs = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            definitions.ToDictionary(item => GetKey(item.WorkflowPath, item.JobKey), item => item.JobName, StringComparer.Ordinal));
    }

    public IReadOnlyList<CiRequiredJobDefinition> RequiredJobs => _definitions;

    public CiRequiredCheckStatus Evaluate(
        CiWorkflowEvidencePacket evidence,
        long jobId,
        string jobKey)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobKey);
        if (jobKey.Length > 100 ||
            !jobKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new ArgumentException("Workflow job key contains unsupported characters.", nameof(jobKey));
        if (jobId <= 0)
            throw new ArgumentOutOfRangeException(nameof(jobId));
        if (!_requiredJobs.TryGetValue(GetKey(evidence.Provenance.WorkflowPath, jobKey), out var expectedJobName))
            return CiRequiredCheckStatus.NotRequired;
        if (!evidence.WorkflowDefinitionTrusted || !evidence.CheckoutVerified || !evidence.EvidenceComplete)
            return CiRequiredCheckStatus.Incomplete;

        var matches = evidence.ObservedRun.Jobs.Where(job => job.JobId == jobId).ToArray();
        if (matches.Length != 1)
            return CiRequiredCheckStatus.Incomplete;
        var observed = matches[0];
        if (!string.Equals(observed.Name, expectedJobName, StringComparison.Ordinal) ||
            observed.RunId != evidence.Provenance.RunId ||
            observed.RunAttempt != evidence.Provenance.RunAttempt ||
            observed.Status != "completed")
            return CiRequiredCheckStatus.Incomplete;

        return observed.Conclusion switch
        {
            "success" => CiRequiredCheckStatus.Passed,
            "failure" => CiRequiredCheckStatus.Failed,
            _ => CiRequiredCheckStatus.Incomplete
        };
    }

    private static string GetKey(string workflowPath, string jobKey) => workflowPath + "\0" + jobKey;
}
