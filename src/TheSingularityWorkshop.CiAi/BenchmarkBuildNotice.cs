using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Minimal source-build identity sent to a benchmark harness after a verified successful build.</summary>
public sealed record BenchmarkBuildNotice
{
    public const string DefaultEventType = "serialization-development-changed";

    public BenchmarkBuildNotice(CiRunSnapshot sourceRun, string benchmarkRepository)
    {
        ArgumentNullException.ThrowIfNull(sourceRun);
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkRepository);
        if (!RepositoryIdentity.IsValid(benchmarkRepository))
            throw new ArgumentException("Benchmark repository must be canonical owner/repository.", nameof(benchmarkRepository));
        if (!sourceRun.IsSuccessful)
            throw new ArgumentException("Only a completed successful source build may trigger benchmarking.", nameof(sourceRun));
        if (string.Equals(sourceRun.Repository, benchmarkRepository, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source and benchmark repositories must be different.", nameof(benchmarkRepository));

        SourceRepository = sourceRun.Repository;
        SourceCommitSha = sourceRun.HeadSha.ToLowerInvariant();
        SourceWorkflowRunId = sourceRun.RunId;
        SourceWorkflowName = sourceRun.WorkflowName;
        SourceWorkflowUrl = sourceRun.EvidenceUrl;
        SourceCompletedAtUtc = sourceRun.UpdatedAt;
        BenchmarkRepository = benchmarkRepository;
        NoticeId = $"{SourceRepository.ToLowerInvariant()}@{SourceCommitSha}";
    }

    public string SourceRepository { get; }
    public string SourceCommitSha { get; }
    public long SourceWorkflowRunId { get; }
    public string SourceWorkflowName { get; }
    public Uri SourceWorkflowUrl { get; }
    public DateTimeOffset SourceCompletedAtUtc { get; }
    public string BenchmarkRepository { get; }
    /// <summary>Stable per source repository+commit; benchmark harnesses should deduplicate this value.</summary>
    public string NoticeId { get; }
}

/// <summary>
/// Sends a narrowly scoped repository_dispatch event to a benchmark harness. It communicates
/// a successful build only; it does not create or alter benchmark definitions or publish packages.
/// The supplied token must have contents:write permission on the target benchmark repository.
/// </summary>
public sealed class GitHubActionsBenchmarkDispatchClient
{
    private readonly HttpClient _httpClient;
    private readonly string _token;

    public GitHubActionsBenchmarkDispatchClient(HttpClient httpClient, string token)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
    }

    public async Task DispatchSuccessfulBuildAsync(
        BenchmarkBuildNotice notice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var parts = notice.BenchmarkRepository.Split('/');
        var endpoint = new Uri($"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/dispatches");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("TheSingularityWorkshop-CiAi/0.1");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        var payload = new
        {
            event_type = BenchmarkBuildNotice.DefaultEventType,
            client_payload = new
            {
                schema = "ciai.benchmark-build-notice.v1",
                noticeId = notice.NoticeId,
                sourceRepository = notice.SourceRepository,
                sourceCommitSha = notice.SourceCommitSha,
                // Compatibility aliases consumed by the Serialization benchmark workflow.
                source_sha = notice.SourceCommitSha,
                source_ref = "development",
                sourceWorkflowRunId = notice.SourceWorkflowRunId,
                sourceWorkflowName = notice.SourceWorkflowName,
                sourceWorkflowUrl = notice.SourceWorkflowUrl.AbsoluteUri,
                sourceCompletedAtUtc = notice.SourceCompletedAtUtc
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
