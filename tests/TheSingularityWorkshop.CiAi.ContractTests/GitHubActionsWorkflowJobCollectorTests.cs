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
                "steps must be preserved in numeric order");
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

        var pageRequests = 0;
        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            pageRequests++;
            var requestedPage = int.Parse(ParseQueryValue(request.RequestUri!.Query, "page"));
            Assert(requestedPage == pageRequests, "collector did not request pages in ascending order");
            Assert(ParseQueryValue(request.RequestUri.Query, "per_page") == "1",
                "configured page size was not honored");
            // Return distinct, request-count-bound fixtures to isolate pagination/order.
            var job = pageRequests switch
            {
                1 => Job(1202, 42, 2),
                2 => Job(1201, 42, 2),
                _ => throw new InvalidOperationException("Collector requested more than two pages.")
            };
            return Task.FromResult(JsonResponse($$"""{"total_count":2,"jobs":[{{job}}]}"""));
        })))
        {
            var result = await new GitHubActionsWorkflowJobCollector(client)
                .GetJobsForAttemptAsync("TrentBest/SimpleDemos", 42, 2, maxPages: 3, perPage: 1);
            Assert(pageRequests == 2 && result.PagesRead == 2 && result.TotalCount == 2 && !result.IsTruncated,
                "collector did not retrieve all pages");
            Assert(result.Jobs.Select(x => x.JobId).SequenceEqual(new long[] { 1201, 1202 }),
                "multi-page jobs were not stably ordered");
        }

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            var page = int.Parse(ParseQueryValue(request.RequestUri!.Query, "page"));
            var job = page == 1 ? Job(1201, 42, 2) : Job(1202, 42, 2);
            return Task.FromResult(JsonResponse($$"""{"total_count":2,"jobs":[{{job}}]}"""));
        })))
        {
            var result = await new GitHubActionsWorkflowJobCollector(client)
                .GetJobsForAttemptAsync("TrentBest/SimpleDemos", 42, 2, maxPages: 1, perPage: 1);
            Assert(result.PagesRead == 1 && result.Jobs.Count == 1 && result.IsTruncated,
                "page-cap truncation was not surfaced");
        }

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            // Simulate a provider pagination defect: the same job appears on both pages.
            var job = Job(1201, 42, 2);
            return Task.FromResult(JsonResponse($"{{\"total_count\":2,\"jobs\":[{job}]}}"));
        })))
        {
            var rejectedDuplicate = false;
            try
            {
                await new GitHubActionsWorkflowJobCollector(client)
                    .GetJobsForAttemptAsync("TrentBest/SimpleDemos", 42, 2, maxPages: 2, perPage: 1);
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("duplicate job ID 1201", StringComparison.Ordinal))
            {
                rejectedDuplicate = true;
            }

            Assert(rejectedDuplicate, "collector must reject duplicate job IDs across pages");
        }

        await ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new GitHubActionsWorkflowJobCollector(new HttpClient()).GetJobsForAttemptAsync(
                "TrentBest/SimpleDemos", 42, 0));

        Console.WriteLine("PASS: 8 GitHub Actions workflow job collector checks.");
    }

    private static string Job(long id, long runId, int attempt) =>
        $"{{\"id\":{id},\"run_id\":{runId},\"run_attempt\":{attempt},\"name\":\"job-{id}\",\"status\":\"completed\",\"conclusion\":\"success\",\"html_url\":\"https://github.com/TrentBest/SimpleDemos/actions/runs/{runId}/job/{id}\",\"steps\":[]}}";

    private static string JobPage(int total, string first, string second, bool includeOnlyFirst) =>
        $"{{\"total_count\":{total},\"jobs\":[{(includeOnlyFirst ? first : second)}]}}";

    private static string ParseQueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == name)
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        throw new InvalidOperationException($"Missing query parameter '{name}'.");
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
