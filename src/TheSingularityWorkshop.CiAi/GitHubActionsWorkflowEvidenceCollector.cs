using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Evidence independently retrieved from GitHub for one completed Actions job.</summary>
public sealed record CiVerifiedWorkflowEvidence(
    string TestedCheckoutSha,
    string WorkflowDefinitionSha256,
    string EvidenceSha256,
    string EvidenceReference,
    string WorkflowDefinitionRef);

/// <summary>
/// Retrieves the exact workflow definition and attempt-specific job log archive from GitHub.
/// It derives checkout identity from the recorded checkout log, not from the workflow run head SHA.
/// </summary>
public sealed class GitHubActionsWorkflowEvidenceCollector
{
    private const int MaximumArchiveBytes = 64 * 1024 * 1024;
    private const int MaximumEntryBytes = 16 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string? _token;

    public GitHubActionsWorkflowEvidenceCollector(HttpClient httpClient, string? token = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public async Task<CiVerifiedWorkflowEvidence> CollectAsync(
        string repository,
        CiWorkflowRunObservation run,
        CiWorkflowJobObservation job,
        string workflowDefinitionRepository,
        string expectedCheckoutRepository,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCheckoutRepository);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(job);

        if (!RepositoryIdentity.IsValid(repository) ||
            !RepositoryIdentity.IsValid(workflowDefinitionRepository) ||
            !RepositoryIdentity.IsValid(expectedCheckoutRepository))
            throw new ArgumentException("Repositories must use canonical owner/repository identifiers.");
        if (!string.Equals(repository, run.Run.Repository, StringComparison.OrdinalIgnoreCase) ||
            job.RunId != run.Run.RunId || job.RunAttempt != run.RunAttempt)
            throw new InvalidDataException("Selected run and job do not belong to the requested repository and attempt.");
        if (!string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Job log evidence is not final until the selected job is completed.");

        var workflowRef = $"{run.WorkflowPath}@{run.Run.HeadSha}";
        var workflowBytes = await GetBytesAsync(
            $"https://raw.githubusercontent.com/{workflowDefinitionRepository}/{run.Run.HeadSha}/{run.WorkflowPath}",
            MaximumEntryBytes, cancellationToken).ConfigureAwait(false);
        if (workflowBytes.Length == 0)
            throw new InvalidDataException("Workflow definition is empty.");
        var workflowDigest = Convert.ToHexString(SHA256.HashData(workflowBytes)).ToLowerInvariant();

        var parts = repository.Split('/');
        var logArchive = await GetBytesAsync(
            $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/jobs/{job.JobId}/logs",
            MaximumArchiveBytes, cancellationToken, githubApi: true).ConfigureAwait(false);

        using var archiveStream = new MemoryStream(logArchive, writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name) && entry.Length <= MaximumEntryBytes)
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length == 0 || archive.Entries.Any(entry => entry.Length > MaximumEntryBytes))
            throw new InvalidDataException("Job log archive is empty or contains an oversized entry.");

        using var evidenceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var logText = new StringBuilder();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nameBytes = Encoding.UTF8.GetBytes(entry.FullName);
            evidenceHash.AppendData(nameBytes);
            evidenceHash.AppendData([0]);
            await using var entryStream = entry.Open();
            using var content = new MemoryStream();
            await entryStream.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
            var bytes = content.ToArray();
            evidenceHash.AppendData(bytes);
            evidenceHash.AppendData([0]);
            logText.Append(Encoding.UTF8.GetString(bytes)).Append('\n');
        }

        var checkoutSha = FindCheckoutSha(logText.ToString(), expectedCheckoutRepository);
        return new CiVerifiedWorkflowEvidence(
            checkoutSha,
            workflowDigest,
            Convert.ToHexString(evidenceHash.GetHashAndReset()).ToLowerInvariant(),
            job.EvidenceUrl.AbsoluteUri,
            workflowRef);
    }

    private async Task<byte[]> GetBytesAsync(
        string url,
        int maximumBytes,
        CancellationToken cancellationToken,
        bool githubApi = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("TheSingularityWorkshop-CiAi/0.1");
        if (githubApi)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        if (_token is not null && new Uri(url).Host is "api.github.com" or "raw.githubusercontent.com")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidDataException("GitHub evidence response exceeds the configured size limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maximumBytes)
                throw new InvalidDataException("GitHub evidence response exceeds the configured size limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string FindCheckoutSha(string logs, string expectedRepository)
    {
        var repositoryMarker = $"Syncing repository: {expectedRepository}";
        var markerIndex = logs.IndexOf(repositoryMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidDataException("Job logs do not identify the expected checkout repository.");

        var nextRepository = logs.IndexOf("Syncing repository: ", markerIndex + repositoryMarker.Length,
            StringComparison.Ordinal);
        var section = nextRepository < 0 ? logs[markerIndex..] : logs[markerIndex..nextRepository];
        var command = "[command]/usr/bin/git log -1 --format=%H";
        var commandIndex = section.IndexOf(command, StringComparison.Ordinal);
        if (commandIndex < 0)
            throw new InvalidDataException("Job logs do not record the expected checkout commit.");
        var afterCommand = section[(commandIndex + command.Length)..];
        var match = Regex.Match(afterCommand, @"^\s*([0-9a-fA-F]{40})\s*$", RegexOptions.Multiline);
        if (!match.Success)
            throw new InvalidDataException("Checkout commit in job logs is missing or ambiguous.");
        return match.Groups[1].Value.ToLowerInvariant();
    }
}
