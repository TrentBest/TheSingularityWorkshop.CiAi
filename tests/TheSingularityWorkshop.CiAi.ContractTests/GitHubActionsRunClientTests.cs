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

        Console.WriteLine("PASS: 3 GitHub Actions client integration checks.");
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
