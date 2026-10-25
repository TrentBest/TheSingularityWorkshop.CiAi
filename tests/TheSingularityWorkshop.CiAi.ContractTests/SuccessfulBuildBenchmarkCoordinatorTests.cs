using System.Net;
using TheSingularityWorkshop.CiAi;

internal static class SuccessfulBuildBenchmarkCoordinatorTests
{
    public static int Run()
    {
        var passed = 0;
        CheckAsync("coordinator dispatches only after exact successful-run reconciliation", async () =>
        {
            var handler = new CountingHandler();
            using var http = new HttpClient(handler);
            var coordinator = new SuccessfulBuildBenchmarkCoordinator(
                new GitHubActionsBenchmarkDispatchClient(http, "test-token"),
                "TrentBest/FSM_Serialization_Benchmarks");
            var decision = await coordinator.DispatchVerifiedSuccessAsync(
                NewRun(), "TrentBest/FSM_Serialization", 77,
                "abcdef0123456789abcdef0123456789abcdef01");
            Assert(decision.Dispatched, "verified success did not dispatch");
            Assert(decision.VerificationErrors.Count == 0, "success returned verification errors");
            Assert(decision.NoticeId == "trentbest/fsm_serialization@abcdef0123456789abcdef0123456789abcdef01",
                "notice identity did not bind source repository and commit");
            Assert(handler.RequestCount == 1, "verified success should dispatch exactly once");
        });
        CheckAsync("coordinator fails closed and does not dispatch a SHA mismatch", async () =>
        {
            var handler = new CountingHandler();
            using var http = new HttpClient(handler);
            var coordinator = new SuccessfulBuildBenchmarkCoordinator(
                new GitHubActionsBenchmarkDispatchClient(http, "test-token"),
                "TrentBest/FSM_Serialization_Benchmarks");
            var decision = await coordinator.DispatchVerifiedSuccessAsync(
                NewRun(), "TrentBest/FSM_Serialization", 77,
                "123456789abcdef0123456789abcdef012345678");
            Assert(!decision.Dispatched, "SHA mismatch was dispatched");
            Assert(decision.VerificationErrors.Contains("headSha.mismatch"), "SHA mismatch diagnostic missing");
            Assert(handler.RequestCount == 0, "unverified run reached the dispatch endpoint");
        });
        CheckAsync("coordinator fails closed for a failed source run", async () =>
        {
            var handler = new CountingHandler();
            using var http = new HttpClient(handler);
            var coordinator = new SuccessfulBuildBenchmarkCoordinator(
                new GitHubActionsBenchmarkDispatchClient(http, "test-token"),
                "TrentBest/FSM_Serialization_Benchmarks");
            var decision = await coordinator.DispatchVerifiedSuccessAsync(
                NewRun(CiRunConclusion.Failure), "TrentBest/FSM_Serialization", 77,
                "abcdef0123456789abcdef0123456789abcdef01");
            Assert(!decision.Dispatched, "failed source run was dispatched");
            Assert(decision.VerificationErrors.Contains("run.not-successful"), "failure diagnostic missing");
            Assert(handler.RequestCount == 0, "failed run reached the dispatch endpoint");
        });
        return passed;

        void CheckAsync(string name, Func<Task> action)
        {
            action().GetAwaiter().GetResult();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
    }

    private static CiRunSnapshot NewRun(CiRunConclusion conclusion = CiRunConclusion.Success) =>
        new("TrentBest/FSM_Serialization", 77, "Build and Test",
            "abcdef0123456789abcdef0123456789abcdef01",
            CiRunStatus.Completed, conclusion,
            new Uri("https://github.com/TrentBest/FSM_Serialization/actions/runs/77"),
            DateTimeOffset.Parse("2026-10-25T21:00:00Z"));

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
