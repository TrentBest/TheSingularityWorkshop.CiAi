using System.Net.Http.Headers;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Provider facts for a discovered workflow run, including its retry attempt and workflow identity.</summary>
public sealed record CiWorkflowRunObservation(
    CiRunSnapshot Run,
    int RunAttempt,
    string WorkflowPath,
    string Event,
    IReadOnlyList<int> PullRequestNumbers)
{
    /// <summary>The workflow-definition ref encoded in GitHub's workflow path, when supplied by the API.</summary>
    public string? WorkflowRef { get; init; }

    /// <summary>The exact provider status string, retained for diagnosis when GitHub adds new values.</summary>
    public string ProviderStatus { get; init; } = string.Empty;
}

/// <summary>A bounded page collection. Truncation is explicit and must not be interpreted as complete discovery.</summary>
public sealed record CiWorkflowRunCollection(
    string Repository,
    string HeadSha,
    IReadOnlyList<CiWorkflowRunObservation> Runs,
    int PagesRead,
    int TotalCount,
    bool IsTruncated);

/// <summary>
/// Read-only, exact-SHA GitHub Actions run discovery. Every returned row is independently checked
/// against the requested repository and commit, even though GitHub also receives a head_sha filter.
/// </summary>
public sealed class GitHubActionsWorkflowRunCollector
{
    private readonly HttpClient _httpClient;
    private readonly string? _token;

    public GitHubActionsWorkflowRunCollector(HttpClient httpClient, string? token = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>
    /// Lists workflow runs for one exact commit. Page and page-size limits prevent unbounded API work.
    /// If matching runs exceed maxPages, IsTruncated is true and the caller must report incomplete discovery.
    /// </summary>
    public async Task<CiWorkflowRunCollection> GetRunsForCommitAsync(
        string repository,
        string headSha,
        int maxPages = 5,
        int perPage = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Head SHA must be a full 40-character hexadecimal Git commit identifier.", nameof(headSha));
        if (maxPages is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maxPages));
        if (perPage is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(perPage));

        var parts = repository.Split('/');
        var runs = new List<CiWorkflowRunObservation>();
        var totalCount = 0;
        var pagesRead = 0;
        var truncated = false;

        for (var page = 1; page <= maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/runs?head_sha={headSha.ToLowerInvariant()}&per_page={perPage}&page={page}";
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
            if (!root.TryGetProperty("workflow_runs", out var pageRuns) || pageRuns.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub workflow collection is missing the workflow_runs array.");

            pagesRead++;
            foreach (var item in pageRuns.EnumerateArray())
                runs.Add(ParseObservation(item, repository, headSha));

            if (pageRuns.GetArrayLength() < perPage)
                break;

            if (page == maxPages)
                truncated = runs.Count < totalCount;
        }

        // Stable output order: newest update first, then run ID. We do not collapse reruns;
        // callers need each run ID and attempt to resolve retry history explicitly.
        var ordered = runs.OrderByDescending(x => x.Run.UpdatedAt).ThenByDescending(x => x.Run.RunId).ToArray();
        return new CiWorkflowRunCollection(repository, headSha.ToLowerInvariant(),
            Array.AsReadOnly(ordered), pagesRead, totalCount, truncated);
    }

    private static CiWorkflowRunObservation ParseObservation(JsonElement root, string expectedRepository, string expectedSha)
    {
        var repository = RequiredString(root, "repository", "full_name");
        if (!string.Equals(repository, expectedRepository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned a workflow run for a different repository.");

        var sha = RequiredString(root, "head_sha");
        if (!string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned a workflow run for a different commit SHA.");

        var runId = RequiredInt64(root, "id");
        if (runId <= 0) throw new InvalidDataException("GitHub workflow run ID must be positive.");
        var attempt = root.TryGetProperty("run_attempt", out var attemptValue) && attemptValue.TryGetInt32(out var parsedAttempt)
            ? parsedAttempt : 0;
        if (attempt <= 0) throw new InvalidDataException("GitHub workflow run attempt must be a positive integer.");

        var urlText = RequiredString(root, "html_url");
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
            throw new InvalidDataException("GitHub workflow run contains an invalid evidence URL.");
        var name = RequiredString(root, "name");
        var statusText = RequiredString(root, "status");
        var status = statusText switch
        {
            "queued" or "requested" or "waiting" => CiRunStatus.Queued,
            "pending" => CiRunStatus.Pending,
            "in_progress" => CiRunStatus.InProgress,
            "completed" => CiRunStatus.Completed,
            _ => CiRunStatus.Unknown
        };

        var conclusionText = root.TryGetProperty("conclusion", out var conclusionValue) &&
            conclusionValue.ValueKind == JsonValueKind.String ? conclusionValue.GetString()! :
            conclusionValue.ValueKind == JsonValueKind.Null ? string.Empty : string.Empty;
        var conclusion = conclusionText switch
        {
            "" => CiRunConclusion.None,
            "success" => CiRunConclusion.Success,
            "failure" => CiRunConclusion.Failure,
            "cancelled" => CiRunConclusion.Cancelled,
            "timed_out" => CiRunConclusion.TimedOut,
            "action_required" => CiRunConclusion.ActionRequired,
            "neutral" => CiRunConclusion.Neutral,
            "skipped" => CiRunConclusion.Skipped,
            "stale" => CiRunConclusion.Stale,
            _ => CiRunConclusion.Unknown
        };
        if ((status == CiRunStatus.Completed) == (conclusion == CiRunConclusion.None))
            throw new InvalidDataException("GitHub workflow status and conclusion are inconsistent.");

        var updatedAtText = RequiredString(root, "updated_at");
        if (!DateTimeOffset.TryParse(updatedAtText, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var updatedAt))
            throw new InvalidDataException("GitHub workflow run contains an invalid updated_at timestamp.");

        var path = RequiredString(root, "path");
        // GitHub suffixes the workflow file path with the ref that supplied the workflow definition.
        // Preserve that trust-boundary fact separately from the run's head SHA.
        string? workflowRef = null;
        var refSuffix = path.IndexOf('@');
        if (refSuffix > 0)
        {
            workflowRef = path[(refSuffix + 1)..];
            if (!workflowRef.StartsWith("refs/", StringComparison.Ordinal) ||
                workflowRef.Any(char.IsControl) ||
                workflowRef.Any(char.IsWhiteSpace))
                throw new InvalidDataException("GitHub workflow path contains an invalid workflow-definition ref.");
            path = path[..refSuffix];
        }
        var eventName = RequiredString(root, "event");
        var snapshot = new CiRunSnapshot(repository, runId, name, sha, status, conclusion, url, updatedAt);
        var prNumbers = new List<int>();
        if (root.TryGetProperty("pull_requests", out var prs) && prs.ValueKind == JsonValueKind.Array)
        {
            foreach (var pr in prs.EnumerateArray())
                if (pr.TryGetProperty("number", out var number) && number.TryGetInt32(out var prNumber) && prNumber > 0)
                    prNumbers.Add(prNumber);
        }

        return new CiWorkflowRunObservation(snapshot, attempt, path, eventName,
            Array.AsReadOnly(prNumbers.Distinct().ToArray()))
        {
            ProviderStatus = statusText,
            WorkflowRef = workflowRef
        };
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"GitHub workflow run is missing required field '{property}'.");
        return value.GetString()!;
    }

    private static string RequiredString(JsonElement root, string parent, string property)
    {
        if (!root.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"GitHub workflow run is missing required object '{parent}'.");
        return RequiredString(nested, property);
    }

    private static long RequiredInt64(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidDataException($"GitHub workflow collection is missing valid field '{property}'.");
        return result;
    }

    private static int RequiredInt32(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetInt32(out var result) || result < 0)
            throw new InvalidDataException($"GitHub workflow collection is missing valid field '{property}'.");
        return result;
    }
}
