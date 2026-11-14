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

    var provenanceRequired = string.Equals(
        Environment.GetEnvironmentVariable("CIAI_REQUIRE_WORKFLOW_PROVENANCE"), "true", StringComparison.OrdinalIgnoreCase);
    CiWorkflowProvenanceArtifactEvidence? workflowProvenance = null;
    if (provenanceRequired)
    {
        var archivePath = Environment.GetEnvironmentVariable("CIAI_WORKFLOW_PROVENANCE_ARCHIVE_PATH");
        var expectedWorkflowSha = Environment.GetEnvironmentVariable("CIAI_EXPECTED_WORKFLOW_SHA");
        if (requiredWorkflowPaths.Length != 1 ||
            string.IsNullOrWhiteSpace(expectedWorkflowRef) ||
            string.IsNullOrWhiteSpace(expectedWorkflowEvent) ||
            string.IsNullOrWhiteSpace(archivePath) ||
            string.IsNullOrWhiteSpace(expectedWorkflowSha))
            throw new InvalidDataException("Required workflow provenance policy is incomplete.");

        var identity = requiredWorkflowPaths[0].Split('#');
        if (identity.Length != 3 ||
            !string.Equals(identity[0], ".github/workflows/ci.yml", StringComparison.Ordinal) ||
            !long.TryParse(identity[1], out var provenanceRunId) || provenanceRunId <= 0 ||
            !int.TryParse(identity[2], out var provenanceRunAttempt) || provenanceRunAttempt <= 0)
            throw new InvalidDataException("Required workflow identity must bind ci.yml to one exact run ID and attempt.");

        var exactRun = runCollection.Runs
            .Where(item => item.Run.RunId == provenanceRunId && item.RunAttempt == provenanceRunAttempt)
            .ToArray();
        if (exactRun.Length != 1)
            throw new InvalidDataException("Workflow provenance requires exactly one independently observed source run attempt.");

        var artifactName = $"ciai-workflow-provenance-{provenanceRunAttempt}";
        var providerArtifact = await new GitHubActionsArtifactCollector(httpClient, token)
            .GetRequiredArtifactAsync(repository, provenanceRunId, headSha, artifactName,
                cancellationToken: CancellationToken.None);
        var archiveBytes = await File.ReadAllBytesAsync(archivePath);
        workflowProvenance = CiWorkflowProvenanceArtifactReader.Read(
            archiveBytes, providerArtifact, artifactName, "workflow-provenance.json",
            repository, provenanceRunId, provenanceRunAttempt, headSha,
            expectedWorkflowRef, expectedWorkflowSha, expectedWorkflowEvent, expectedWorkflowRef);
    }

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
                job.StartedAt,
                job.CompletedAt,
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
    var expectedWorkflowRef = Environment.GetEnvironmentVariable("CIAI_EXPECTED_WORKFLOW_REF");
    var expectedWorkflowEvent = Environment.GetEnvironmentVariable("CIAI_EXPECTED_WORKFLOW_EVENT");
    var exactRunBindingConfigured = !string.IsNullOrWhiteSpace(expectedWorkflowRef) ||
        !string.IsNullOrWhiteSpace(expectedWorkflowEvent);
    var exactRunBindingValid = !exactRunBindingConfigured ||
        CiExactWorkflowRunBindingEvaluator.IsValid(
            requiredWorkflowPaths, runCollection.Runs, repository, headSha, expectedWorkflowRef, expectedWorkflowEvent);
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
    var workflowProvenanceValid = !provenanceRequired || workflowProvenance is not null;
    var complete = CiRequiredWorkflowEvidenceEvaluator.IsPassing(discoveryComplete, requiredWorkflowResults) &&
        exactRunBindingValid && workflowProvenanceValid;
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
        exactRunBindingConfigured,
        exactRunBindingValid,
        workflowProvenance = new
        {
            required = provenanceRequired,
            valid = workflowProvenance is not null,
            providerArtifactId = workflowProvenance?.ProviderArtifact.ArtifactId,
            providerArchiveDigest = workflowProvenance?.ProviderArtifact.ProviderArchiveDigest,
            payloadSha256 = workflowProvenance?.PayloadSha256,
            payloadSizeBytes = workflowProvenance?.PayloadSizeBytes,
            workflowRef = workflowProvenance?.Provenance.WorkflowRef,
            workflowSha = workflowProvenance?.Provenance.WorkflowSha,
            eventName = workflowProvenance?.Provenance.EventName,
            eventRef = workflowProvenance?.Provenance.EventRef,
            eventSha = workflowProvenance?.Provenance.EventSha,
            checkoutRef = workflowProvenance?.Provenance.CheckoutRef,
            checkoutSha = workflowProvenance?.Provenance.CheckoutSha
        },
        workflowProvenanceValid,
        requiredEvidenceComplete,
        requiredWorkflowsTerminal,
        requiredWorkflowsSuccessful,
        complete,
        note = "Observation only: this report does not certify release readiness, inspect package contents, interpret security findings, or authorize publication. API discovery completeness, required evidence completeness, required-workflow terminality, and required-workflow success are separate facts. Only explicitly required workflow identities block the policy; missing, ambiguous, non-terminal, unsuccessful, or incomplete required evidence fails closed. Set CIAI_REQUIRED_WORKFLOW_PATHS to a semicolon-separated list of exact .github/workflows/ paths, optionally suffixed with #<run-id>#<attempt>. When CIAI_EXPECTED_WORKFLOW_REF and CIAI_EXPECTED_WORKFLOW_EVENT are configured, each exact required run must also match that provider-observed workflow ref and event. Without a configured required-workflow policy, complete is false.",
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
