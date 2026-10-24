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
        repository, headSha, maxPages: 10, perPage: 100, CancellationToken.None);

    var observedRuns = new List<object>();
    foreach (var observation in runCollection.Runs)
    {
        // Each job query is tied to the exact attempt reported for this run; retries are not blended.
        var jobs = await jobCollector.GetJobsForAttemptAsync(
            repository, observation.Run.RunId, observation.RunAttempt,
            maxPages: 10, perPage: 100, CancellationToken.None);

        observedRuns.Add(new
        {
            run = new
            {
                observation.Run.Repository,
                observation.Run.RunId,
                observation.Run.WorkflowName,
                observation.Run.HeadSha,
                status = observation.Run.Status.ToString(),
                conclusion = observation.Run.Conclusion.ToString(),
                rawStatus = observation.Run.Status.ToString(),
                rawConclusion = observation.Run.Conclusion.ToString(),
                observation.Run.UpdatedAt,
                evidenceUrl = observation.Run.EvidenceUrl.ToString(),
                observation.RunAttempt,
                observation.WorkflowPath,
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
            jobsTruncated = jobs.IsTruncated
        });
    }

    var report = new
    {
        schema = "c iai.commit-observation.v1".Replace(" ", ""),
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
        complete = !runCollection.IsTruncated && observedRuns.All(item =>
            !(bool)item.GetType().GetProperty("jobsTruncated")!.GetValue(item)!),
        note = "Observation only: this report does not certify release readiness, inspect package contents, interpret security findings, or authorize publication.",
        runs = observedRuns
    };

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (outputPath is not null)
    {
        await File.WriteAllTextAsync(outputPath, json + Environment.NewLine);
        Console.Error.WriteLine($"Report written to: {Path.GetFullPath(outputPath)}");
    }

    // Incomplete discovery is not a pass. A complete observation is still not a release approval.
    return report.complete ? 0 : 3;
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
