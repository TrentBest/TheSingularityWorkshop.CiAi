using System.Net;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class GitHubActionsWorkflowRunCollectorTests
{
    private const string Sha = "abcdef0123456789abcdef0123456789abcdef01";

    public static async Task RunAsync()
    {
        var page = """
        {
          "total_count": 1,
          "workflow_runs": [{
            "id": 42, "name": "Pack and publish", "path": ".github/workflows/package.yml@refs/heads/development",
            "head_sha": "abcdef0123456789abcdef0123456789abcdef01",
            "status": "completed", "conclusion": "failure", "run_attempt": 2,
            "event": "push", "html_url": "https://github.com/TrentBest/SimpleDemos/actions/runs/42",
            "updated_at": "2026-10-24T04:00:00Z",
            "repository": { "full_name": "TrentBest/SimpleDemos" },
            "pull_requests": [{ "number": 35 }]
          }]
        }
        """;

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            Assert(request.Method == HttpMethod.Get, "collector must be read-only");
            Assert(request.RequestUri!.AbsolutePath == "/repos/TrentBest/SimpleDemos/actions/runs",
                "unexpected collection endpoint");
            Assert(request.RequestUri.Query.Contains("head_sha=" + Sha, StringComparison.Ordinal),
                "query must be pinned to the requested SHA");
            Assert(request.RequestUri.Query.Contains("per_page=100", StringComparison.Ordinal),
                "page size must be explicit");
            return Task.FromResult(JsonResponse(page));
        })))
        {
            var collection = await new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha);
            Assert(collection.Runs.Count == 1 && collection.TotalCount == 1, "matching run was not collected");
            Assert(!collection.IsTruncated && collection.PagesRead == 1, "single page was marked incomplete");
            Assert(collection.Runs[0].Run.RunId == 42 && collection.Runs[0].RunAttempt == 2,
                "run ID or attempt was not retained");
            Assert(collection.Runs[0].WorkflowPath == ".github/workflows/package.yml",
                "workflow path was not retained");
            Assert(collection.Runs[0].PullRequestNumbers.SequenceEqual(new[] { 35 }),
                "PR correlation was not retained");
            Assert(collection.Runs[0].Run.Conclusion == CiRunConclusion.Failure,
                "failed conclusion was not preserved");
            Assert(collection.Runs[0].WorkflowRef == "refs/heads/development",
                "provider-observed workflow-definition ref was not preserved independently");
        }

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            var wrongRepo = page.Replace("\"full_name\": \"TrentBest/SimpleDemos\"", "\"full_name\": \"Other/Repo\"");
            return Task.FromResult(JsonResponse(wrongRepo));
        })))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha));
        }

        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            var wrongSha = page.Replace(Sha, "123456789abcdef0123456789abcdef012345678");
            return Task.FromResult(JsonResponse(wrongSha));
        })))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha));
        }

        using (var client = new HttpClient(new StubHandler((request, _) =>
            Task.FromResult(JsonResponse(page.Replace("\"run_attempt\": 2", "\"run_attempt\": 0"))))))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha));
        }

        var pagesRequested = new List<int>();
        using (var client = new HttpClient(new StubHandler((request, _) =>
        {
            var query = request.RequestUri!.Query;
            var pageNumber = query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            pagesRequested.Add(pageNumber);
            var fullPage = JsonResponse(page.Replace("\"total_count\": 1", "\"total_count\": 2")
                .Replace("\"id\": 42", pageNumber == 1 ? "\"id\": 42" : "\"id\": 43"));
            return Task.FromResult(fullPage);
        })))
        {
            var collection = await new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha, maxPages: 1, perPage: 1);
            Assert(collection.IsTruncated && collection.PagesRead == 1,
                "page cap did not clearly report incomplete collection");
        }

        await ThrowsAsync<ArgumentOutOfRangeException>(() => new GitHubActionsWorkflowRunCollector(new HttpClient())
            .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha, maxPages: 11));
        await ThrowsAsync<ArgumentException>(() => new GitHubActionsWorkflowRunCollector(new HttpClient())
            .GetRunsForCommitAsync("TrentBest/SimpleDemos", "short"));

        using (var client = new HttpClient(new StubHandler((_, _) =>
        {
            var pending = page.Replace("\"status\": \"completed\", \"conclusion\": \"failure\"",
                "\"status\": \"pending\", \"conclusion\": null");
            return Task.FromResult(JsonResponse(pending));
        })))
        {
            var collection = await new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha);
            Assert(collection.Runs[0].Run.Status == CiRunStatus.Pending,
                "pending provider status was not represented as nonterminal");
            Assert(collection.Runs[0].ProviderStatus == "pending",
                "raw pending provider status was not preserved");
            Assert(collection.Runs[0].Run.Conclusion == CiRunConclusion.None,
                "pending run must not be assigned a final conclusion");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
        {
            var unknown = page.Replace("\"status\": \"completed\", \"conclusion\": \"failure\"",
                "\"status\": \"future_provider_state\", \"conclusion\": null");
            return Task.FromResult(JsonResponse(unknown));
        })))
        {
            var collection = await new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha);
            Assert(collection.Runs[0].Run.Status == CiRunStatus.Unknown,
                "unknown provider status should remain explicit instead of crashing discovery");
            Assert(collection.Runs[0].ProviderStatus == "future_provider_state",
                "raw unknown provider status was not preserved for diagnosis");
        }

        var terminalConclusions = new[]
        {
            ("cancelled", CiRunConclusion.Cancelled),
            ("skipped", CiRunConclusion.Skipped),
            ("stale", CiRunConclusion.Stale),
            ("timed_out", CiRunConclusion.TimedOut),
            ("action_required", CiRunConclusion.ActionRequired),
            ("neutral", CiRunConclusion.Neutral)
        };
        foreach (var (providerConclusion, expectedConclusion) in terminalConclusions)
        {
            using var client = new HttpClient(new StubHandler((_, _) =>
                Task.FromResult(JsonResponse(page.Replace("\\"conclusion\\": \\"failure\\"",
                    "\\"conclusion\\": \\"" + providerConclusion + "\\"", StringComparison.Ordinal)))));
            var result = await new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha);
            Assert(result.Runs[0].Run.Status == CiRunStatus.Completed &&
                result.Runs[0].Run.Conclusion == expectedConclusion &&
                !result.Runs[0].Run.IsSuccessful,
                $"terminal provider conclusion '{providerConclusion}' was not preserved as non-success");
        }

        foreach (var (providerStatus, expectedStatus) in new[]
        {
            ("queued", CiRunStatus.Queued),
            ("in_progress", CiRunStatus.InProgress),
            ("pending", CiRunStatus.Pending)
        })
        {
            using var client = new HttpClient(new StubHandler((_, _) =>
                Task.FromResult(JsonResponse(page.Replace(
                    "\\"status\\": \\"completed\\", \\"conclusion\\": \\"failure\\"",
                    "\\"status\\": \\"" + providerStatus + "\\", \\"conclusion\\": null", StringComparison.Ordinal)))));
            var result = await new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha);
            Assert(result.Runs[0].Run.Status == expectedStatus &&
                result.Runs[0].Run.Conclusion == CiRunConclusion.None &&
                !result.Runs[0].Run.IsSuccessful,
                $"nonterminal provider status '{providerStatus}' was not failed closed");
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(page.Replace(
                ".yml@refs/heads/development", ".yml@not-a-ref", StringComparison.Ordinal))))))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(page.Replace(
                "https://github.com/TrentBest/SimpleDemos/actions/runs/42",
                "https://evil.example/TrentBest/SimpleDemos/actions/runs/42", StringComparison.Ordinal))))))
        {
            await ThrowsAsync<ArgumentException>(() => new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(page.Replace(
                "\\"updated_at\\": \\"2026-10-24T04:00:00Z\\"",
                "\\"updated_at\\": \\"not-a-timestamp\\"", StringComparison.Ordinal))))))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(page.Replace(
                "\\"conclusion\\": \\"failure\\"", "\\"conclusion\\": null", StringComparison.Ordinal))))))
        {
            await ThrowsAsync<InvalidDataException>(() => new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha));
        }

        using (var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse("{\\"total_count\\":0,\\"workflow_runs\\":[]}")))))
        {
            var empty = await new GitHubActionsWorkflowRunCollector(client)
                .GetRunsForCommitAsync("TrentBest/SimpleDemos", Sha);
            Assert(empty.Runs.Count == 0 && empty.TotalCount == 0 && !empty.IsTruncated,
                "zero-run discovery was not represented as a complete empty collection");
        }

        Console.WriteLine("PASS: expanded GitHub Actions workflow collector checks.");
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
