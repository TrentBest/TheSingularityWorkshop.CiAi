using System.Net;
using System.Text.Json;
using TheSingularityWorkshop.CiAi;

internal static class BenchmarkBuildDispatchTests
{
    public static int Run()
    {
        var passed = 0;
        Check("benchmark notice accepts only completed successful source builds", () =>
        {
            var notice = new BenchmarkBuildNotice(NewRun(), "TrentBest/FSM_Serialization_Benchmarks");
            Assert(notice.SourceCommitSha == "abcdef0123456789abcdef0123456789abcdef01", "source SHA was not normalized");
            Assert(notice.NoticeId == "trentbest/simpledemos@abcdef0123456789abcdef0123456789abcdef01", "notice ID is not stable");
        });
        Check("benchmark notice rejects failed or in-progress source builds", () =>
        {
            Throws<ArgumentException>(() => new BenchmarkBuildNotice(
                NewRun(conclusion: CiRunConclusion.Failure), "TrentBest/FSM_Serialization_Benchmarks"));
            Throws<ArgumentException>(() => new BenchmarkBuildNotice(
                NewRun(status: CiRunStatus.InProgress, conclusion: CiRunConclusion.None), "TrentBest/FSM_Serialization_Benchmarks"));
        });
        Check("benchmark notice rejects source equal to benchmark repository", () =>
        {
            Throws<ArgumentException>(() => new BenchmarkBuildNotice(NewRun(), "TrentBest/SimpleDemos"));
        });
        Check("dispatch sends exact source identity to the benchmark repository", async () =>
        {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            var notice = new BenchmarkBuildNotice(NewRun(), "TrentBest/FSM_Serialization_Benchmarks");
            await new GitHubActionsBenchmarkDispatchClient(http, "test-token")
                .DispatchSuccessfulBuildAsync(notice);
            Assert(handler.RequestUri == "https://api.github.com/repos/TrentBest/FSM_Serialization_Benchmarks/dispatches",
                "dispatch endpoint did not target the selected benchmark repository");
            Assert(handler.Method == HttpMethod.Post, "dispatch did not use POST");
            Assert(handler.Authorization == "Bearer test-token", "dispatch did not use bearer authorization");
            using var payload = JsonDocument.Parse(handler.Body!);
            var root = payload.RootElement;
            Assert(root.GetProperty("event_type").GetString() == BenchmarkBuildNotice.DefaultEventType, "event type mismatch");
            var clientPayload = root.GetProperty("client_payload");
            Assert(clientPayload.GetProperty("sourceCommitSha").GetString() == notice.SourceCommitSha, "source SHA mismatch");
            Assert(clientPayload.GetProperty("sourceWorkflowRunId").GetInt64() == notice.SourceWorkflowRunId, "source run ID mismatch");
            Assert(clientPayload.GetProperty("noticeId").GetString() == notice.NoticeId, "stable notice ID mismatch");
        });
        Check("dispatch propagates target permission or delivery failure", async () =>
        {
            var handler = new RecordingHandler(HttpStatusCode.Forbidden);
            using var http = new HttpClient(handler);
            var notice = new BenchmarkBuildNotice(NewRun(), "TrentBest/FSM_Serialization_Benchmarks");
            await ThrowsAsync<HttpRequestException>(() =>
                new GitHubActionsBenchmarkDispatchClient(http, "test-token").DispatchSuccessfulBuildAsync(notice));
        });
        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        void Check(string name, Func<Task> action)
        {
            action().GetAwaiter().GetResult();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
    }

    private static CiRunSnapshot NewRun(
        CiRunStatus status = CiRunStatus.Completed,
        CiRunConclusion conclusion = CiRunConclusion.Success,
        string repository = "TrentBest/SimpleDemos") =>
        new(repository, 42, "CI", "abcdef0123456789abcdef0123456789abcdef01", status, conclusion,
            new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
            DateTimeOffset.Parse("2026-10-25T12:00:00Z"));

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.NoContent) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
