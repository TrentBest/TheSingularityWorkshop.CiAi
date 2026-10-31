namespace TheSingularityWorkshop.CiAi;

/// <summary>One required workflow job, identified by its workflow YAML key and observed GitHub job name.</summary>
public sealed record CiRequiredJobDefinition
{
    public CiRequiredJobDefinition(string jobKey, string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        if (jobKey.Length > 100 ||
            !jobKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new ArgumentException("Workflow job key contains unsupported characters.", nameof(jobKey));
        if (jobName.Length > 200 || jobName.Any(char.IsControl))
            throw new ArgumentException("Workflow job name must be at most 200 characters without control characters.", nameof(jobName));
        JobKey = jobKey;
        JobName = jobName;
    }

    public string JobKey { get; }
    public string JobName { get; }
}

/// <summary>
/// CiAi-owned policy that maps configured workflow job keys to exact observed job names and
/// derives required-check status from the validated v3 run/job evidence.
/// </summary>
public sealed class CiRequiredJobPolicy
{
    private readonly IReadOnlyDictionary<string, string> _requiredJobs;

    public CiRequiredJobPolicy(IEnumerable<CiRequiredJobDefinition> requiredJobs)
    {
        ArgumentNullException.ThrowIfNull(requiredJobs);
        var definitions = requiredJobs.ToArray();
        if (definitions.Any(item => item is null))
            throw new ArgumentException("Required job policy cannot contain null definitions.", nameof(requiredJobs));
        if (definitions.Select(item => item.JobKey).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new ArgumentException("Required workflow job keys must be unique.", nameof(requiredJobs));
        _requiredJobs = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            definitions.ToDictionary(item => item.JobKey, item => item.JobName, StringComparer.Ordinal));
    }

    public IReadOnlyCollection<string> RequiredJobKeys => Array.AsReadOnly(_requiredJobs.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());

    public CiRequiredCheckStatus Evaluate(
        CiWorkflowEvidencePacketV3 evidence,
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
        if (!_requiredJobs.TryGetValue(jobKey, out var expectedJobName))
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
}
