using System.Net.Http.Headers;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// One artifact record independently returned by GitHub's Actions artifact API.
/// Its constructor is internal so callers cannot manufacture provider observations.
/// </summary>
public sealed record CiProviderArtifactObservation
{
    internal CiProviderArtifactObservation(
        string repository,
        long runId,
        string headSha,
        long artifactId,
        string artifactName,
        long archiveSizeBytes,
        string providerArchiveDigest,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        bool isExpired,
        Uri archiveDownloadUrl)
    {
        Repository = repository;
        RunId = runId;
        HeadSha = headSha;
        ArtifactId = artifactId;
        ArtifactName = artifactName;
        ArchiveSizeBytes = archiveSizeBytes;
        ProviderArchiveDigest = providerArchiveDigest;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        IsExpired = isExpired;
        ArchiveDownloadUrl = archiveDownloadUrl;
    }

    public string Repository { get; }
    public long RunId { get; }
    public string HeadSha { get; }
    public long ArtifactId { get; }
    public string ArtifactName { get; }
    public long ArchiveSizeBytes { get; }
    public string ProviderArchiveDigest { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsExpired { get; }
    public Uri ArchiveDownloadUrl { get; }
}

/// <summary>
/// Read-only GitHub Actions artifact metadata observer. It does not download archive bytes;
/// the host owns authenticated transport and must pass the exact bytes to the bounded archive reader.
/// </summary>
public sealed class GitHubActionsArtifactCollector
{
    private readonly HttpClient _httpClient;
    private readonly string? _token;

    public GitHubActionsArtifactCollector(HttpClient httpClient, string? token = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>
    /// Discovers the one artifact with the configured name from the exact workflow run.
    /// The artifact ID is selected from provider listing metadata, never from report JSON.
    /// Ambiguous, incomplete, or truncated listings fail closed.
    /// </summary>
    public async Task<CiProviderArtifactObservation> GetRequiredArtifactAsync(
        string repository,
        long expectedRunId,
        string expectedHeadSha,
        string expectedArtifactName,
        int maximumPages = 10,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArtifactName);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (expectedRunId <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRunId));
        if (expectedHeadSha is not { Length: 40 } || !expectedHeadSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected workflow SHA must be a full 40-character commit identifier.", nameof(expectedHeadSha));
        if (maximumPages is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumPages));
        if (pageSize is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var parts = repository.Split('/');
        var baseUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/runs/{expectedRunId}/artifacts";
        var artifactIds = new HashSet<long>();
        long? expectedTotal = null;
        long? matchingArtifactId = null;
        var returned = 0;

        for (var page = 1; page <= maximumPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}?per_page={pageSize}&page={page}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("TheSingularityWorkshop-CiAi/0.1");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (_token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("total_count", out var totalElement) ||
                !totalElement.TryGetInt64(out var total) || total < 0 ||
                !root.TryGetProperty("artifacts", out var artifacts) ||
                artifacts.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub artifact listing omitted its total count or artifact array.");
            if (expectedTotal.HasValue && expectedTotal.Value != total)
                throw new InvalidDataException("GitHub artifact listing total changed during pagination.");
            expectedTotal = total;

            foreach (var item in artifacts.EnumerateArray())
            {
                var id = RequiredInt64(item, "id");
                var name = RequiredString(item, "name");
                if (id <= 0 || !artifactIds.Add(id))
                    throw new InvalidDataException("GitHub artifact listing contains a nonpositive or duplicate artifact ID.");
                returned++;
                if (string.Equals(name, expectedArtifactName, StringComparison.Ordinal))
                {
                    if (matchingArtifactId.HasValue)
                        throw new InvalidDataException("More than one provider artifact matches the configured required artifact name.");
                    matchingArtifactId = id;
                }
            }

            if (returned >= total)
                break;
            if (page == maximumPages)
                throw new InvalidDataException("GitHub artifact listing is truncated; the required artifact cannot be selected unambiguously.");
        }

        if (expectedTotal is not { } total || returned != total)
            throw new InvalidDataException("GitHub artifact listing is incomplete.");
        if (matchingArtifactId is not { } selectedArtifactId)
            throw new InvalidDataException("No provider artifact matches the configured required artifact name.");

        var observed = await GetArtifactAsync(
            repository, selectedArtifactId, expectedRunId, expectedHeadSha, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(observed.ArtifactName, expectedArtifactName, StringComparison.Ordinal))
            throw new InvalidDataException("Provider artifact name changed between listing and detail observation.");
        return observed;
    }

    public async Task<CiProviderArtifactObservation> GetArtifactAsync(
        string repository,
        long artifactId,
        long expectedRunId,
        string expectedHeadSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identifiers.", nameof(repository));
        if (artifactId <= 0 || expectedRunId <= 0)
            throw new ArgumentOutOfRangeException(nameof(artifactId));
        if (expectedHeadSha is not { Length: 40 } || !expectedHeadSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected workflow SHA must be a full 40-character commit identifier.", nameof(expectedHeadSha));

        var parts = repository.Split('/');
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/artifacts/{artifactId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("TheSingularityWorkshop-CiAi/0.1");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var returnedId = RequiredInt64(root, "id");
        if (returnedId != artifactId)
            throw new InvalidDataException("GitHub returned a different artifact ID than requested.");
        var name = RequiredString(root, "name");
        var size = RequiredInt64(root, "size_in_bytes");
        if (size is <= 0 or > CiTestResultArtifactArchive.DefaultMaximumArchiveBytes)
            throw new InvalidDataException("Provider artifact size is outside the bounded ZIP limit.");
        var digest = RequiredString(root, "digest");
        if (digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
            !digest.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new InvalidDataException("Provider artifact metadata is missing a valid SHA-256 digest.");
        var expired = RequiredBoolean(root, "expired");
        var createdAt = RequiredTimestamp(root, "created_at");
        var expiresAt = RequiredTimestamp(root, "expires_at");
        if (expiresAt <= createdAt)
            throw new InvalidDataException("Provider artifact creation and expiry timestamps are inconsistent.");

        if (!root.TryGetProperty("workflow_run", out var workflowRun) || workflowRun.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Provider artifact metadata is missing its workflow_run identity.");
        var runId = RequiredInt64(workflowRun, "id");
        var headSha = RequiredString(workflowRun, "head_sha");
        if (runId != expectedRunId ||
            !string.Equals(headSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Provider artifact does not belong to the expected workflow run and exact SHA.");

        var downloadText = RequiredString(root, "archive_download_url");
        var expectedPath = $"/repos/{repository}/actions/artifacts/{artifactId}/zip";
        if (!Uri.TryCreate(downloadText, UriKind.Absolute, out var downloadUrl) ||
            downloadUrl.Scheme != Uri.UriSchemeHttps || !downloadUrl.IsDefaultPort ||
            !string.Equals(downloadUrl.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(downloadUrl.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(downloadUrl.UserInfo) || !string.IsNullOrEmpty(downloadUrl.Query) ||
            !string.IsNullOrEmpty(downloadUrl.Fragment))
            throw new InvalidDataException("Provider artifact archive URL does not identify the exact GitHub artifact.");

        return new CiProviderArtifactObservation(
            repository, runId, headSha.ToLowerInvariant(), artifactId, name, size,
            digest.ToLowerInvariant(), createdAt, expiresAt, expired, downloadUrl);
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"GitHub artifact response is missing required field '{property}'.");
        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidDataException($"GitHub artifact response is missing valid field '{property}'.");
        return result;
    }

    private static long RequiredInt64(JsonElement root, string parent, string property)
    {
        if (!root.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"GitHub artifact response is missing object '{parent}'.");
        return RequiredInt64(nested, property);
    }

    private static string RequiredString(JsonElement root, string parent, string property)
    {
        if (!root.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"GitHub artifact response is missing object '{parent}'.");
        return RequiredString(nested, property);
    }

    private static bool RequiredBoolean(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"GitHub artifact response is missing boolean '{property}'.");
        return value.GetBoolean();
    }

    private static DateTimeOffset RequiredTimestamp(JsonElement root, string property)
    {
        var value = RequiredString(root, property);
        var hasZone = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            (value.Contains('T') && (value.LastIndexOf('+') > value.IndexOf('T') || value.LastIndexOf('-') > value.IndexOf('T')));
        if (!hasZone || !DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var timestamp))
            throw new InvalidDataException($"GitHub artifact timestamp '{property}' must include an explicit UTC offset.");
        return timestamp;
    }
}
