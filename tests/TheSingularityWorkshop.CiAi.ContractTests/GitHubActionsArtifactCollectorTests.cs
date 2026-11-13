using System.Net;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class GitHubActionsArtifactCollectorTests
{
    private const string Repository = "TrentBest/Experiences";
    private const long ArtifactId = 7001;
    private const long RunId = 41234078182;
    private const string Sha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static async Task RunAsync()
    {
        var valid = Fixture();

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            Assert(request.Method == HttpMethod.Get, "artifact collector must be read-only");
            Assert(request.RequestUri!.AbsolutePath == $"/repos/{Repository}/actions/artifacts/{ArtifactId}",
                "artifact collector did not request the exact provider artifact");
            return Task.FromResult(JsonResponse(valid));
        })))
        {
            var artifact = await new GitHubActionsArtifactCollector(client)
                .GetArtifactAsync(Repository, ArtifactId, RunId, Sha);
            Assert(artifact.Repository == Repository && artifact.RunId == RunId &&
                artifact.HeadSha == Sha && artifact.ArtifactId == ArtifactId,
                "provider artifact was not bound to exact repository/run/SHA identity");
            Assert(artifact.ArtifactName == "minesweeper-test-results" &&
                artifact.ArchiveSizeBytes == 4096 &&
                artifact.ProviderArchiveDigest == "sha256:" + new string('a', 64) &&
                artifact.ArchiveDownloadUrl.AbsolutePath == $"/repos/{Repository}/actions/artifacts/{ArtifactId}/zip",
                "provider artifact metadata or archive URL was not preserved");
            Assert(!artifact.IsExpired &&
                artifact.CreatedAt == DateTimeOffset.Parse("2026-10-31T01:00:00Z") &&
                artifact.ExpiresAt == DateTimeOffset.Parse("2026-11-07T01:00:00Z"),
                "provider artifact retention metadata was not preserved");
        }

        await Reject(valid.Replace("\"id\":7001", "\"id\":7002", StringComparison.Ordinal),
            "different artifact ID");
        await Reject(valid.Replace("\"id\":41234078182", "\"id\":41234078183", StringComparison.Ordinal),
            "different workflow run");
        await Reject(valid.Replace(Sha, new string('c', 40), StringComparison.Ordinal),
            "different workflow SHA");
        await Reject(valid.Replace("sha256:" + new string('a', 64), "invalid-digest", StringComparison.Ordinal),
            "malformed provider digest");
        await Reject(valid.Replace($"/actions/artifacts/{ArtifactId}/zip", "/actions/artifacts/9999/zip", StringComparison.Ordinal),
            "substituted archive URL");
        await Reject(valid.Replace("2026-11-07T01:00:00Z", "not-a-time", StringComparison.Ordinal),
            "invalid provider expiry");
        await Reject(valid.Replace("\"size_in_bytes\":4096", "\"size_in_bytes\":20971521", StringComparison.Ordinal),
            "oversized provider artifact");

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(valid.Replace("\"expired\":false", "\"expired\":true", StringComparison.Ordinal))))))
        {
            var artifact = await new GitHubActionsArtifactCollector(client)
                .GetArtifactAsync(Repository, ArtifactId, RunId, Sha);
            Assert(artifact.IsExpired, "provider expired flag was not retained for the source binding gate");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(valid.Replace("\"workflow_run\"", "\"other\"", StringComparison.Ordinal))))))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsArtifactCollector(client)
                .GetArtifactAsync(Repository, ArtifactId, RunId, Sha));
        }

        await ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new GitHubActionsArtifactCollector(new HttpClient()).GetArtifactAsync(Repository, 0, RunId, Sha));

        Console.WriteLine("PASS: 11 GitHub Actions artifact observation checks.");
    }

    private static string Fixture() => $$"""
    {
      "id": {{ArtifactId}},
      "name": "minesweeper-test-results",
      "size_in_bytes": 4096,
      "digest": "sha256:{{new string('a', 64)}}",
      "expired": false,
      "created_at": "2026-10-31T01:00:00Z",
      "expires_at": "2026-11-07T01:00:00Z",
      "archive_download_url": "https://api.github.com/repos/{{Repository}}/actions/artifacts/{{ArtifactId}}/zip",
      "workflow_run": { "id": {{RunId}}, "head_sha": "{{Sha}}" }
    }
    """;

    private static async Task Reject(string json, string scenario)
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(JsonResponse(json))));
        await ThrowsAsync<InvalidDataException>(() => new GitHubActionsArtifactCollector(client)
            .GetArtifactAsync(Repository, ArtifactId, RunId, Sha));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
