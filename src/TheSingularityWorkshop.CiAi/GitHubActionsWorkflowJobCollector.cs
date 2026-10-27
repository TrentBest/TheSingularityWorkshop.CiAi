using System.Net.Http.Headers;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Raw step facts preserved from one GitHub Actions job attempt.</summary>
public sealed record CiWorkflowStepObservation(
    int Number,
    string Name,
    string Status,
    string? Conclusion,
    string? CompletedAt);

/// <summary>Raw job facts, including every reported step and the provider's evidence URL.</summary>
public sealed record CiWorkflowJobObservation(
    long JobId,
    long RunId,
    int RunAttempt,
    string Name,
    string Status,
    string? Conclusion,
    Uri EvidenceUrl,
    IReadOnlyList<CiWorkflowStepObservation> Steps);

/// <summary>A bounded job collection for one explicit workflow run attempt.</summary>
public sealed record CiWorkflowJobCollection(
    string Repository,
    long RunId,
    int RunAttempt,
    IReadOnlyList<CiWorkflowJobObservation> Jobs,
    int PagesRead,
    int TotalCount,
    bool IsTruncated);

/// <summary>
/// Read-only GitHub Actions job/step collection. The attempt-specific endpoint prevents
/// accidental blending of retry attempts. Raw provider conclusions are retained, not guessed.
/// </summary>
public sealed class GitHubActionsWorkflowJobCollector
{
    private readonly HttpClient _httpClient;
    private readonly string? _token;

    public GitHubActionsWorkflowJobCollector(HttpClient httpClient, string? token = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public async Task<CiWorkflowJobCollection> GetJobsForAttemptAsync(
        string repository,
        long runId,
        int runAttempt,
        int maxPages = 5,
        int perPage = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (runAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(runAttempt));
        if (maxPages is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maxPages));
        if (perPage is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(perPage));

        var parts = repository.Split('/');
        var jobs = new List<CiWorkflowJobObservation>();
        var totalCount = 0;
        var pagesRead = 0;
        var truncated = false;

        for (var page = 1; page <= maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/runs/{runId}/attempts/{runAttempt}/jobs?per_page={perPage}&page={page}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("TheSingularityWorkshop-CiAi/0.1");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (_token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            totalCount = RequiredInt32(root, "total_count");
            if (!root.TryGetProperty("jobs", out var pageJobs) || pageJobs.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub job collection is missing the jobs array.");

            pagesRead++;
            foreach (var item in pageJobs.EnumerateArray())
                jobs.Add(ParseJob(item, repository, runId, runAttempt));

            // GitHub may return a full page even when this page completes the reported collection.
            // Stop once the provider's total has been collected; otherwise an unnecessary next request
            // can turn a complete result into a failure (or waste the remaining page budget).
            if (jobs.Count >= totalCount || pageJobs.GetArrayLength() < perPage)
                break;
            if (page == maxPages)
                truncated = jobs.Count < totalCount;
        }

        var duplicateId = jobs.GroupBy(x => x.JobId).FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidDataException($"GitHub returned duplicate job ID {duplicateId.Key} for one attempt.");

        return new CiWorkflowJobCollection(repository, runId, runAttempt,
            Array.AsReadOnly(jobs.OrderBy(x => x.JobId).ToArray()), pagesRead, totalCount, truncated);
    }

    private static CiWorkflowJobObservation ParseJob(JsonElement root, string repository, long runId, int attempt)
    {
        var id = RequiredInt64(root, "id");
        if (id <= 0) throw new InvalidDataException("GitHub job ID must be positive.");

        if (root.TryGetProperty("run_id", out var returnedRun) &&
            returnedRun.ValueKind == JsonValueKind.Number && returnedRun.TryGetInt64(out var actualRun) && actualRun != runId)
            throw new InvalidDataException("GitHub returned a job belonging to a different workflow run.");
        if (root.TryGetProperty("run_attempt", out var returnedAttempt) &&
            returnedAttempt.ValueKind == JsonValueKind.Number && returnedAttempt.TryGetInt32(out var actualAttempt) && actualAttempt != attempt)
            throw new InvalidDataException("GitHub returned a job belonging to a different run attempt.");

        var name = RequiredString(root, "name");
        var status = RequiredString(root, "status");
        var conclusion = OptionalString(root, "conclusion");
        var htmlUrl = RequiredString(root, "html_url");
        var expectedPath = $"/{repository}/actions/runs/{runId}/job/{id}";
        if (!Uri.TryCreate(htmlUrl, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort ||
            !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(url.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) ||
            !string.IsNullOrEmpty(url.Fragment))
            throw new InvalidDataException("GitHub job URL does not identify the expected repository and job.");

        var steps = new List<CiWorkflowStepObservation>();
        if (root.TryGetProperty("steps", out var stepArray) && stepArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in stepArray.EnumerateArray())
            {
                var number = RequiredInt32(step, "number");
                if (number <= 0) throw new InvalidDataException("GitHub step number must be positive.");
                steps.Add(new CiWorkflowStepObservation(number, RequiredString(step, "name"),
                    RequiredString(step, "status"), OptionalString(step, "conclusion"), OptionalString(step, "completed_at")));
            }
        }

        return new CiWorkflowJobObservation(id, runId, attempt, name, status, conclusion, url,
            Array.AsReadOnly(steps.OrderBy(x => x.Number).ToArray()));
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"GitHub response is missing required field '{property}'.");
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static long RequiredInt64(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidDataException($"GitHub response is missing valid field '{property}'.");
        return result;
    }

    private static int RequiredInt32(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetInt32(out var result) || result < 0)
            throw new InvalidDataException($"GitHub response is missing valid field '{property}'.");
        return result;
    }
}
