namespace TheSingularityWorkshop.CiAi;

/// <summary>Observable lifecycle outcome for one executed pipeline step.</summary>
public enum CiStepOutcome { Succeeded, Failed, Skipped, Cancelled, Blocked, Indeterminate }

/// <summary>Execution capability supplied by the host. Approval is deliberately separate from step success.</summary>
public sealed record CiExecutionContext(
    string Repository,
    string CommitSha,
    string PlanId,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>Immutable outcome for one step; diagnostics are bounded to avoid untrusted output amplification.</summary>
public sealed record CiStepExecutionRecord(
    string StepId,
    string StepName,
    CiStepOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? Diagnostic = null);

/// <summary>Deterministic execution report. A successful report means all executed recipe steps succeeded; it is not release authorization.</summary>
public sealed record CiPipelineExecutionReport(
    string Repository,
    string CommitSha,
    string PlanId,
    IReadOnlyList<CiStepExecutionRecord> Steps,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt)
{
    public bool IsSuccessful => Steps.All(s => s.Outcome == CiStepOutcome.Succeeded);
}

/// <summary>Host-owned authorization for one exact candidate and plan.</summary>
public sealed record CiActionAuthorization(
    string Repository,
    string CommitSha,
    string PackageId,
    string PackageVersion,
    string PackageDigest,
    string PlanId,
    string Approver,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset ExpiresAt)
{
    public IReadOnlyList<string> Validate(
        string repository, string commitSha, string packageId, string packageVersion,
        string packageDigest, string planId, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (!string.Equals(Repository, repository, StringComparison.Ordinal)) errors.Add("authorization.repository-mismatch");
        if (!string.Equals(CommitSha, commitSha, StringComparison.OrdinalIgnoreCase)) errors.Add("authorization.commit-mismatch");
        if (!string.Equals(PackageId, packageId, StringComparison.Ordinal)) errors.Add("authorization.package-id-mismatch");
        if (!string.Equals(PackageVersion, packageVersion, StringComparison.Ordinal)) errors.Add("authorization.package-version-mismatch");
        if (!string.Equals(PackageDigest, packageDigest, StringComparison.OrdinalIgnoreCase)) errors.Add("authorization.digest-mismatch");
        if (!string.Equals(PlanId, planId, StringComparison.Ordinal)) errors.Add("authorization.plan-mismatch");
        if (string.IsNullOrWhiteSpace(Approver)) errors.Add("authorization.approver-missing");
        if (ExpiresAt <= AuthorizedAt || now < AuthorizedAt || now >= ExpiresAt) errors.Add("authorization.expired-or-not-yet-valid");
        return errors.AsReadOnly();
    }
}

/// <summary>Required evidence for one release gate. Missing and non-passing states remain failures.</summary>
public sealed record CiRequiredEvidence(string Id, bool Required = true);

public sealed record CiEvidenceFact(string Id, ReleaseEvidenceState State, string Repository, string CommitSha, string? Diagnostic = null);

public sealed record CiEvidenceEvaluation(
    string Repository, string CommitSha, IReadOnlyDictionary<string, ReleaseEvidenceState> Requirements,
    IReadOnlyList<string> Errors)
{
    public bool IsPassing => Errors.Count == 0 && Requirements.Values.All(v => v == ReleaseEvidenceState.Passed);
}

/// <summary>Fail-closed evaluator that binds every evidence fact to one repository and full commit SHA.</summary>
public static class CiRequiredEvidenceEvaluator
{
    public static CiEvidenceEvaluation Evaluate(string repository, string commitSha,
        IEnumerable<CiRequiredEvidence> required, IEnumerable<CiEvidenceFact> facts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (!RepositoryIdentity.IsValid(repository)) throw new ArgumentException("Repository must be canonical owner/repository.", nameof(repository));
        if (commitSha is null || commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character commit SHA is required.", nameof(commitSha));
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(facts);
        var requirements = required.ToArray();
        if (requirements.Any(r => string.IsNullOrWhiteSpace(r.Id)))
            throw new ArgumentException("Evidence requirement IDs cannot be empty.", nameof(required));
        if (requirements.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != requirements.Length)
            throw new ArgumentException("Evidence requirement IDs must be unique.", nameof(required));
        var supplied = facts.ToArray();
        var errors = new List<string>();
        var states = new Dictionary<string, ReleaseEvidenceState>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            if (!requirement.Required) continue;
            var matches = supplied.Where(f => string.Equals(f.Id, requirement.Id, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
            {
                states[requirement.Id] = ReleaseEvidenceState.Missing;
                errors.Add($"evidence.missing:{requirement.Id}");
                continue;
            }
            if (matches.Length != 1)
            {
                states[requirement.Id] = ReleaseEvidenceState.Unknown;
                errors.Add($"evidence.ambiguous:{requirement.Id}");
                continue;
            }
            var fact = matches[0];
            if (!string.Equals(fact.Repository, repository, StringComparison.Ordinal) ||
                !string.Equals(fact.CommitSha, commitSha, StringComparison.OrdinalIgnoreCase))
            {
                states[requirement.Id] = ReleaseEvidenceState.Unknown;
                errors.Add($"evidence.identity-mismatch:{requirement.Id}");
                continue;
            }
            states[requirement.Id] = fact.State;
            if (fact.State != ReleaseEvidenceState.Passed)
                errors.Add($"evidence.not-passing:{requirement.Id}:{fact.State}");
        }
        return new CiEvidenceEvaluation(repository, commitSha, states, errors.AsReadOnly());
    }
}

/// <summary>Executes ordinary in-process recipe steps sequentially. It never grants privileged capability.</summary>
public sealed class CiPipelineExecutor
{
    private const int MaxDiagnosticLength = 2048;

    public async Task<CiPipelineExecutionReport> ExecuteAsync(
        CiPipelineDefinition definition,
        string commitSha,
        string planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (commitSha is null || commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character commit SHA is required.", nameof(commitSha));
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        var started = DateTimeOffset.UtcNow;
        var records = new List<CiStepExecutionRecord>();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var context = new CiExecutionContext(definition.Repository, commitSha.ToLowerInvariant(), planId, values);
        var blocked = false;

        foreach (var step in definition.Steps)
        {
            var stepStarted = DateTimeOffset.UtcNow;
            if (blocked)
            {
                records.Add(new(step.Id, step.Name, CiStepOutcome.Skipped, stepStarted, DateTimeOffset.UtcNow,
                    "A prior step failed, was blocked, or was cancelled."));
                continue;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                records.Add(new(step.Id, step.Name, CiStepOutcome.Cancelled, stepStarted, DateTimeOffset.UtcNow, "Cancellation requested."));
                blocked = true;
                continue;
            }
            if (step.Capability != CiStepCapability.None || step.RequiresApproval)
            {
                records.Add(new(step.Id, step.Name, CiStepOutcome.Blocked, stepStarted, DateTimeOffset.UtcNow,
                    "Privileged capability cannot execute in the ordinary in-process executor."));
                blocked = true;
                continue;
            }

            try
            {
                var result = step.Operation.DynamicInvoke();
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                    result = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null;
                }
                if (result is bool boolean && !boolean)
                {
                    records.Add(new(step.Id, step.Name, CiStepOutcome.Failed, stepStarted, DateTimeOffset.UtcNow, "Operation returned false."));
                    blocked = true;
                    continue;
                }
                values[step.Id] = result;
                records.Add(new(step.Id, step.Name, CiStepOutcome.Succeeded, stepStarted, DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                records.Add(new(step.Id, step.Name, CiStepOutcome.Cancelled, stepStarted, DateTimeOffset.UtcNow, "Operation cancelled."));
                blocked = true;
            }
            catch (Exception exception)
            {
                var cause = exception is System.Reflection.TargetInvocationException tie
                    ? tie.InnerException ?? exception : exception;
                var diagnostic = cause.GetType().Name + ": " + cause.Message;
                if (diagnostic.Length > MaxDiagnosticLength) diagnostic = diagnostic[..MaxDiagnosticLength];
                records.Add(new(step.Id, step.Name, CiStepOutcome.Failed, stepStarted, DateTimeOffset.UtcNow, diagnostic));
                blocked = true;
            }
        }
        return new(definition.Repository, context.CommitSha, planId, records.AsReadOnly(), started, DateTimeOffset.UtcNow);
    }
}
