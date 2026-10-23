using System.Net.Http.Headers;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Reads authoritative workflow-run facts from the GitHub Actions REST API.</summary>
public sealed class GitHubActionsRunClient
{
    private readonly HttpClient _httpClient;
    private readonly string? _token;

    /// <summary>Creates a read-only client. Supply a token only when the target repository requires authentication.</summary>
    public GitHubActionsRunClient(HttpClient httpClient, string? token = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>Fetches one run and normalizes its provider facts into a validated snapshot.</summary>
    public async Task<CiRunSnapshot> GetRunAsync(
        string repository,
        long runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 ||
            part is "." or ".." ||
            part.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (runId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId));

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/runs/{runId}");
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

        var returnedRepository = RequiredString(root, "repository", "full_name");
        var returnedId = RequiredInt64(root, "id");
        if (!string.Equals(returnedRepository, repository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned a workflow run for a different repository.");
        if (returnedId != runId)
            throw new InvalidDataException("GitHub returned a different workflow run ID.");

        var status = ParseStatus(RequiredString(root, "status"));
        var conclusion = ParseConclusion(root);
        return new CiRunSnapshot(
            returnedRepository,
            returnedId,
            RequiredString(root, "name"),
            RequiredString(root, "head_sha"),
            status,
            conclusion,
            new Uri(RequiredString(root, "html_url"), UriKind.Absolute),
            DateTimeOffset.Parse(RequiredString(root, "updated_at"), System.Globalization.CultureInfo.InvariantCulture));
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
            throw new InvalidDataException($"GitHub workflow run is missing valid field '{property}'.");
        return result;
    }

    private static CiRunStatus ParseStatus(string value) => value switch
    {
        "queued" or "requested" or "waiting" => CiRunStatus.Queued,
        "in_progress" => CiRunStatus.InProgress,
        "completed" => CiRunStatus.Completed,
        _ => CiRunStatus.Unknown
    };

    private static CiRunConclusion ParseConclusion(JsonElement root)
    {
        if (!root.TryGetProperty("conclusion", out var value) || value.ValueKind == JsonValueKind.Null)
            return CiRunConclusion.None;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("GitHub workflow run conclusion must be a string or null.");

        return value.GetString() switch
        {
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
    }
}
