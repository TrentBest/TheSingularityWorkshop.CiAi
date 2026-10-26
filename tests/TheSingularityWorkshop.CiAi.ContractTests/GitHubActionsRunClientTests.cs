using System.Net;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class GitHubActionsRunClientTests
{
    public static async Task RunAsync()
    {
        var successfulRun = """
        {
          "id": 42,
          "name": "Build",
          "head_sha": "abcdef0123456789abcdef0123456789abcdef01",
          "status": "completed",
          "conclusion": "success",
          "html_url": "https://github.com/TrentBest/SimpleDemos/actions/runs/42",
          "updated_at": "2026-10-23T21:00:00Z",
          "repository": { "full_name": "TrentBest/SimpleDemos" }
        }
        """;

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            Assert(request.Method == HttpMethod.Get, "CI observation must use GET.");
            Assert(request.RequestUri!.AbsoluteUri ==
                "https://api.github.com/repos/TrentBest/SimpleDemos/actions/runs/42", "unexpected API target");
            Assert(request.Headers.UserAgent.Any(), "GitHub request must identify the client.");
            return Task.FromResult(JsonResponse(successfulRun));
        })))
        {
            var run = await new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42);
            Assert(run.IsSuccessful, "successful provider result was not recognized");
            Assert(run.Identity == "TrentBest/SimpleDemos#42", "run identity was not preserved");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"full_name\": \"TrentBest/SimpleDemos\"", "\"full_name\": \"other/repository\""))))))
        {
            await ThrowsAsync<InvalidDataException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"conclusion\": \"success\"", "\"conclusion\": \"future-conclusion\""))))))
        {
            var run = await new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42);
            Assert(run.Conclusion == CiRunConclusion.Unknown, "future provider value was not kept explicitly unknown");
            Assert(!run.IsSuccessful, "unknown provider value was treated as success");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"id\": 42", "\"id\": 43"))))))
        {
            await ThrowsAsync<InvalidDataException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"head_sha\": \"abcdef0123456789abcdef0123456789abcdef01\"", "\"head_sha\": \"abcdef0123456789\""))))))
        {
            await ThrowsAsync<ArgumentException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42));
        }

        var malformedRepositoryWasSent = false;
        using (var client = new HttpClient(new StubHandler((_, _) =>
        {
            malformedRepositoryWasSent = true;
            return Task.FromResult(JsonResponse(successfulRun));
        })))
        {
            await ThrowsAsync<ArgumentException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/../Other", 42));
            Assert(!malformedRepositoryWasSent, "invalid repository must be rejected before HTTP");
        }

        var overlongRepositoryWasSent = false;
        using (var client = new HttpClient(new StubHandler((_, _) =>
        {
            overlongRepositoryWasSent = true;
            return Task.FromResult(JsonResponse(successfulRun));
        })))
        {
            var overlongRepository = new string('a', 101) + "/Repo";
            await ThrowsAsync<ArgumentException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync(overlongRepository, 42));
            Assert(!overlongRepositoryWasSent, "overlong repository component must be rejected before HTTP");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"html_url\": \"https://github.com/TrentBest/SimpleDemos/actions/runs/42\"", "\"html_url\": \"not-a-url\""))))))
        {
            await ThrowsAsync<InvalidDataException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"updated_at\": \"2026-10-23T21:00:00Z\"", "\"updated_at\": \"not-a-timestamp\""))))))
        {
            await ThrowsAsync<InvalidDataException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace(
                "\"html_url\": \"https://github.com/TrentBest/SimpleDemos/actions/runs/42\"",
                "\"html_url\": \"https://github.com/other/repository/actions/runs/42\""))))))
        {
            await ThrowsAsync<ArgumentException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace(
                "\"conclusion\": \"success\"",
                "\"conclusion\": \"failure\""))))))
        {
            var run = await new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42);
            Assert(run.Conclusion == CiRunConclusion.Failure, "failed provider conclusion was not preserved");
            Assert(!run.IsSuccessful, "failed provider run was treated as success");
        }

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            Assert(request.Headers.Authorization?.Scheme == "Bearer", "token authentication scheme must be Bearer");
            Assert(request.Headers.Authorization?.Parameter == "test-token", "configured token was not forwarded");
            Assert(request.Headers.Contains("X-GitHub-Api-Version"), "GitHub API version must be explicit");
            return Task.FromResult(JsonResponse(successfulRun));
        })))
        {
            await new GitHubActionsRunClient(client, "test-token").GetRunAsync("TrentBest/SimpleDemos", 42);
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
        {
            await ThrowsAsync<HttpRequestException>(() =>
                new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"status\": \"completed\"",
                "\"status\": \"pending\"").Replace("\"conclusion\": \"success\"",
                "\"conclusion\": null"))))))
        {
            var run = await new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42);
            Assert(run.Status == CiRunStatus.Pending && run.Conclusion == CiRunConclusion.None,
                "pending status must be represented as incomplete, not terminal");
            Assert(!run.IsSuccessful, "pending workflow was incorrectly treated as successful");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(successfulRun.Replace("\"status\": \"completed\"",
                "\"status\": \"future_provider_state\"").Replace("\"conclusion\": \"success\"",
                "\"conclusion\": null"))))))
        {
            var run = await new GitHubActionsRunClient(client).GetRunAsync("TrentBest/SimpleDemos", 42);
            Assert(run.Status == CiRunStatus.Unknown && run.Conclusion == CiRunConclusion.None,
                "unknown status should remain explicit rather than aborting observation");
            Assert(!run.IsSuccessful, "unknown workflow status was incorrectly treated as successful");
        }

        Console.WriteLine("PASS: 15 GitHub Actions client integration checks.");
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

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
