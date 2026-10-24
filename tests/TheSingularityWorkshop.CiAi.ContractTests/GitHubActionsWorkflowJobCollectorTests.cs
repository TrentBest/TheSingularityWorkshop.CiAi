using System.Net;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class GitHubActionsWorkflowJobCollectorTests
{
    public static async Task RunAsync()
    {
        const string response = """
        {
          "total_count": 1,
          "jobs": [{
            "id": 1201,
            "run_id": 42,
            "run_attempt": 2,
            "name": "build-and-test",
            "status": "completed",
            "conclusion": "failure",
            "html_url": "https://github.com/TrentBest/SimpleDemos/actions/runs/42/job/1201",
            "steps": [
              {"number": 2, "name": "Build", "status": "completed", "conclusion": "skipped", "completed_at": "2026-10-24T04:00:00Z"},
              {"number": 1, "name": "Branding preflight", "status": "completed", "conclusion": "failure", "completed_at": "2026-10-24T03:59:00Z"}
            ]
          }]
        }
        """;

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            Assert(request.Method == HttpMethod.Get, "job collector must be read-only");
            Assert(request.RequestUri!.AbsolutePath ==
                "/repos/TrentBest/SimpleDemos/actions/runs/42/attempts/2/jobs",
                "collector did not use the exact run-attempt endpoint");
            Assert(request.RequestUri.Query.Contains("per_page=100", StringComparison.Ordinal),
                "page size must be explicit");
            return Task.FromResult(JsonResponse(response));
        })))
        {
            var result = await new GitHubActionsWorkflowJobCollector(client)
                .GetJobsForAttemptAsync("TrentBest/SimpleDemos", 42, 2);
            Assert(result.Jobs.Count == 1 && !result.IsTruncated, "job collection was incomplete");
            Assert(result.Jobs[0].RunAttempt == 2 && result.Jobs[0].Conclusion == "failure",
                "attempt or raw conclusion was lost");
            Assert(result.Jobs[0].Steps.Count == 2 &&
                result.Jobs[0].Steps[0].Name == "Branding preflight" &&
                result.Jobs[0].Steps[1].Conclusion == "skipped",
                "step order or skipped status was not preserved");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
        {
            var wrongRun = response.Replace("\"run_id\": 42", "\"run_id\": 99");
            return Task.FromResult(JsonResponse(wrongRun));
        })))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowJobCollector(client)
                .GetJobsForAttemptAsync("TrentBest/SimpleDemos", 42, 2));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
        {
            var wrongAttempt = response.Replace("\"run_attempt\": 2", "\"run_attempt\": 1");
            return Task.FromResult(JsonResponse(wrongAttempt));
        })))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowJobCollector(client)
                .GetJobsForAttemptAsync("TrentBest/SimpleDemos", 42, 2));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
        {
            var wrongUrl = response.Replace("/actions/runs/42/job/1201", "/actions/runs/99/job/1201");
            return Task.FromResult(JsonResponse(wrongUrl));
        })))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowJobCollector(client)
                .GetJobsForAttemptAsync("TrentBest/SimpleDemos", 42, 2));
        }

        await ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new GitHubActionsWorkflowJobCollector(new HttpClient()).GetJobsForAttemptAsync(
                "TrentBest/SimpleDemos", 42, 0));

        Console.WriteLine("PASS: 5 GitHub Actions workflow job collector checks.");
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
