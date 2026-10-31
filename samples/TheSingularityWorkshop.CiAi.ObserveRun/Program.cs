using System.Text.Json;
using TheSingularityWorkshop.CiAi;

if (args.Length != 2 && args.Length != 3)
{
    PrintUsage();
    return 64;
}

var repository = args[0];
var target = args[1];
var outputPath = args.Length == 3 ? args[2] : null;
using var httpClient = new HttpClient();
var token = Environment.GetEnvironmentVariable("CIAI_GITHUB_TOKEN");

try
{
    if (long.TryParse(target, out var runId) && runId > 0)
    {
        var run = await new GitHubActionsRunClient(httpClient, token)
            .GetRunAsync(repository, runId, CancellationToken.None);
        Console.WriteLine($"Repository:  {run.Repository}");
        Console.WriteLine($"Run ID:      {run.RunId}");
        Console.WriteLine($"Workflow:    {run.WorkflowName}");
        Console.WriteLine($"Commit:      {run.HeadSha}");
        Console.WriteLine($"Status:      {run.Status}");
        Console.WriteLine($"Conclusion:  {run.Conclusion}");
        Console.WriteLine($"Updated UTC: {run.UpdatedAt:O}");
        Console.WriteLine($"Evidence:    {run.EvidenceUrl}");
        Console.WriteLine($"Successful:  {run.IsSuccessful}");
        return run.IsSuccessful ? 0 : 2;
    }

    if (target.Length != 40 || !target.All(Uri.IsHexDigit))
    {
        PrintUsage();
        return 64;
    }

    var headSha = target.ToLowerInvariant();
    var runCollector = new GitHubActionsWorkflowRunCollector(httpClient, token);
    var jobCollector = new GitHubActionsWorkflowJobCollector(httpClient, token);
    var runCollection = await runCollector.GetRunsForCommitAsync(
        repository, headSha, maxPages: 10, perPage: 100, cancellationToken: CancellationToken.None);

    var observedRuns = new List<object>();
    var policyEvidence = new List<CiRequiredWorkflowRunEvidence>();
    foreach (var observation in runCollection.Runs)
    {
        // Each job query is tied to the exact attempt reported for this run; retries are not blended.
        var jobs = await jobCollector.GetJobsForAttemptAsync(
            repository, observation.Run.RunId, observation.RunAttempt,
            maxPages: 10, perPage: 100, cancellationToken: CancellationToken.None);

        var jobsComplete = !jobs.IsTruncated &&
            jobs.TotalCount == jobs.Jobs.Count &&
            jobs.Jobs.Count > 0;
        policyEvidence.Add(new CiRequiredWorkflowRunEvidence(
            observation.WorkflowPath, observation.Run.RunId, observation.RunAttempt,
            observation.Run.Status, observation.Run.Conclusion, jobsComplete));
        observedRuns.Add(new
        {
            run = new
            {
                observation.Run.Repository,
                observation.Run.RunId,
                observation.Run.WorkflowName,
                observation.Run.HeadSha,
                normalizedStatus = observation.Run.Status.ToString(),
                providerStatus = observation.ProviderStatus,
                normalizedConclusion = observation.Run.Conclusion.ToString(),
                observation.Run.UpdatedAt,
                evidenceUrl = observation.Run.EvidenceUrl.ToString(),
                observation.RunAttempt,
                observation.WorkflowPath,
                observation.WorkflowRef,
                observation.Event,
                observation.PullRequestNumbers
            },
            jobs = jobs.Jobs.Select(job => new
            {
                job.JobId,
                job.RunId,
                job.RunAttempt,
                job.Name,
                job.Status,
                job.Conclusion,
                evidenceUrl = job.EvidenceUrl.ToString(),
                steps = job.Steps.Select(step => new
                {
                    step.Number,
                    step.Name,
                    step.Status,
                    step.Conclusion,
                    step.CompletedAt
                })
            }).ToArray(),
            jobsPagesRead = jobs.PagesRead,
            jobsTotalCount = jobs.TotalCount,
            jobsReturnedCount = jobs.Jobs.Count,
            jobsTruncated = jobs.IsTruncated,
            jobsComplete
        });
    }

    var requiredWorkflowPaths = (Environment.GetEnvironmentVariable("CIAI_REQUIRED_WORKFLOW_PATHS") ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var discoveryComplete = !runCollection.IsTruncated &&
        runCollection.Runs.Count == runCollection.TotalCount;
    var requiredWorkflowResults = requiredWorkflowPaths.Length == 0
        ? Array.Empty<CiRequiredWorkflowResult>()
        : CiRequiredWorkflowEvidenceEvaluator.Evaluate(requiredWorkflowPaths, policyEvidence).ToArray();
    var requiredEvidenceComplete = requiredWorkflowResults.Length > 0 &&
        requiredWorkflowResults.All(result => result.EvidenceComplete);
    var requiredWorkflowsTerminal = requiredWorkflowResults.Length > 0 &&
        requiredWorkflowResults.All(result => result.Terminal);
    var requiredWorkflowsSuccessful = requiredWorkflowResults.Length > 0 &&
        requiredWorkflowResults.All(result => result.Successful);
    var complete = CiRequiredWorkflowEvidenceEvaluator.IsPassing(discoveryComplete, requiredWorkflowResults);
    var report = new
    {
        schema = "ciai.commit-observation.v2",
        reportType = "read-only-workflow-observation",
        repository,
        headSha,
        retrievedAtUtc = DateTimeOffset.UtcNow,
        discovery = new
        {
            runCollection.PagesRead,
            runCollection.TotalCount,
            runCollection.IsTruncated,
            returnedRuns = runCollection.Runs.Count
        },
        discoveryComplete,
        requiredWorkflowPolicy = new { configured = requiredWorkflowPaths.Length > 0, workflowIdentities = requiredWorkflowPaths },
        requiredWorkflowResults,
        requiredEvidenceComplete,
        requiredWorkflowsTerminal,
        requiredWorkflowsSuccessful,
        complete,
        note = "Observation only: this report does not certify release readiness, inspect package contents, interpret security findings, or authorize publication. Workflow-definition ref and run head SHA are distinct facts; for trusted-ref candidate dispatch, query by workflow SHA and correlate candidate ref/SHA through evidence packet v2. API discovery completeness, required evidence completeness, required-workflow terminality, and required-workflow success are separate facts. Only explicitly required workflow identities block the policy; missing, ambiguous, non-terminal, unsuccessful, or incomplete required evidence fails closed. Set CIAI_REQUIRED_WORKFLOW_PATHS to a semicolon-separated list of exact .github/workflows/ paths, optionally suffixed with #<run-id>#<attempt>. Without a configured policy, complete is false.",
        runs = observedRuns
    };

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (outputPath is not null)
    {
        await File.WriteAllTextAsync(outputPath, json + Environment.NewLine);
        var markdownPath = Path.ChangeExtension(outputPath, ".md");
        var markdown = CommitObservationMarkdownRenderer.Render(json);
        await File.WriteAllTextAsync(markdownPath, markdown + Environment.NewLine);
        Console.Error.WriteLine($"JSON report written to: {Path.GetFullPath(outputPath)}");
        Console.Error.WriteLine($"Markdown report written to: {Path.GetFullPath(markdownPath)}");
    }

    // Incomplete discovery is not a pass. A complete observation is still not a release approval.
    return complete ? 0 : 3;
}
catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or ArgumentException or JsonException or IOException)
{
    Console.Error.WriteLine($"CiAi could not complete the requested observation: {exception.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- <owner/repository> <run-id>");
    Console.Error.WriteLine("  dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- <owner/repository> <full-40-character-commit-sha> [output.json]");
}
