using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowDispatchRequestTests
{
    private const string Sha = "abcdef0123456789abcdef0123456789abcdef01";
    private const string BaseSha = "123456789abcdef0123456789abcdef012345678";

    public static int Run()
    {
        var passed = 0;
        var request = NewRequest();
        var authorization = NewAuthorization();

        Check("dispatch request binds repository, workflow, feature ref, candidate and base SHA", () =>
        {
            Assert(request.Schema == CiWorkflowDispatchRequest.CurrentSchema &&
                request.Repository == "TrentBest/MyVR" && request.CandidateSha == Sha &&
                request.BaseSha == BaseSha && request.TargetRef == "refs/heads/agent/task-42",
                "request did not preserve its exact candidate identity");
        });

        Check("dispatch intent rejects canonical branches and pull-request refs", () =>
        {
            Throws<ArgumentException>(() => NewRequest(targetRef: "refs/heads/development"));
            Throws<ArgumentException>(() => NewRequest(targetRef: "refs/heads/master"));
            Throws<ArgumentException>(() => NewRequest(targetRef: "refs/pull/42/merge"));
        });

        Check("dispatch intent rejects unsafe workflow paths and malformed source identities", () =>
        {
            Throws<ArgumentException>(() => NewRequest(workflowPath: ".github/workflows/../evil.yml"));
            Throws<ArgumentException>(() => NewRequest(workflowPath: ".github/workflows/build.txt"));
            Throws<ArgumentException>(() => NewRequest(candidateSha: "short"));
            Throws<ArgumentException>(() => NewRequest(candidateSha: BaseSha));
        });

        Check("dispatch intent bounds retries", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => NewRequest(maximumAttempts: 0));
            Throws<ArgumentOutOfRangeException>(() => NewRequest(maximumAttempts: 6));
        });

        Check("matching unexpired host authorization verifies without dispatching", () =>
        {
            var errors = CiWorkflowDispatchAuthorizationEvaluator.Verify(request, authorization,
                DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
            Assert(errors.Count == 0, "exact host authorization did not reconcile");
        });

        Check("dispatch authorization rejects missing authority", () =>
        {
            Assert(CiWorkflowDispatchAuthorizationEvaluator.Verify(request, null,
                DateTimeOffset.Parse("2026-11-01T00:00:00Z"))
                .Contains("dispatch.authorization-missing"), "missing authorization was not blocked");
        });

        Check("dispatch authorization rejects repository, ref and SHA substitution", () =>
        {
            var wrongRepository = new CiWorkflowDispatchAuthorization("auth", "host", request.RequestId,
                "TrentBest/Other", request.TargetRef, Sha, 2, DateTimeOffset.Parse("2026-11-02T00:00:00Z"));
            var wrongRef = new CiWorkflowDispatchAuthorization("auth", "host", request.RequestId,
                request.Repository, "refs/heads/agent/other", Sha, 2, DateTimeOffset.Parse("2026-11-02T00:00:00Z"));
            var wrongSha = new CiWorkflowDispatchAuthorization("auth", "host", request.RequestId,
                request.Repository, request.TargetRef, BaseSha, 2, DateTimeOffset.Parse("2026-11-02T00:00:00Z"));
            var now = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
            Assert(CiWorkflowDispatchAuthorizationEvaluator.Verify(request, wrongRepository, now).Contains("dispatch.repository-mismatch"),
                "wrong repository was authorized");
            Assert(CiWorkflowDispatchAuthorizationEvaluator.Verify(request, wrongRef, now).Contains("dispatch.ref-mismatch"),
                "wrong ref was authorized");
            Assert(CiWorkflowDispatchAuthorizationEvaluator.Verify(request, wrongSha, now).Contains("dispatch.sha-mismatch"),
                "wrong SHA was authorized");
        });

        Check("dispatch authorization rejects expired and undersized attempt budgets", () =>
        {
            var expired = new CiWorkflowDispatchAuthorization("auth", "host", request.RequestId,
                request.Repository, request.TargetRef, Sha, 2, DateTimeOffset.Parse("2026-10-31T23:59:00Z"));
            var insufficient = new CiWorkflowDispatchAuthorization("auth", "host", request.RequestId,
                request.Repository, request.TargetRef, Sha, 1, DateTimeOffset.Parse("2026-11-02T00:00:00Z"));
            var now = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
            Assert(CiWorkflowDispatchAuthorizationEvaluator.Verify(request, expired, now).Contains("dispatch.authorization-expired"),
                "expired authorization was accepted");
            Assert(CiWorkflowDispatchAuthorizationEvaluator.Verify(request, insufficient, now).Contains("dispatch.attempt-budget-exceeded"),
                "attempt budget escalation was accepted");
        });

        var root = Path.Combine(Path.GetTempPath(), "ciai-dispatch-ledger-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check("dispatch ledger durably reloads exact requests", () =>
            {
                var path = Path.Combine(root, "requests.jsonl");
                Assert(new FileCiWorkflowDispatchRequestLedger(path).Enqueue(request) ==
                    CiWorkflowDispatchRequestStoreOutcome.Enqueued, "request was not enqueued");
                var loaded = new FileCiWorkflowDispatchRequestLedger(path).ReadAll().Single();
                Assert(loaded.PayloadDigest == request.PayloadDigest &&
                    loaded.CorrelationId == request.CorrelationId && loaded.CandidateSha == Sha,
                    "reloaded request lost provenance");
            });

            Check("dispatch ledger suppresses exact retries after reopening", () =>
            {
                Assert(new FileCiWorkflowDispatchRequestLedger(Path.Combine(root, "requests.jsonl")).Enqueue(request) ==
                    CiWorkflowDispatchRequestStoreOutcome.Duplicate, "identical retry was not deduplicated");
            });

            Check("dispatch ledger rejects conflicting reuse of request identity", () =>
            {
                var changed = NewRequest(candidateSha: "fedcba9876543210fedcba9876543210fedcba98");
                Assert(new FileCiWorkflowDispatchRequestLedger(Path.Combine(root, "requests.jsonl")).Enqueue(changed) ==
                    CiWorkflowDispatchRequestStoreOutcome.Conflict, "changed request overwrote existing intent");
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS workflow dispatch request {passed}: {name}");
        }
    }

    private static CiWorkflowDispatchRequest NewRequest(
        string targetRef = "refs/heads/agent/task-42",
        string workflowPath = ".github/workflows/ci.yml",
        string candidateSha = Sha,
        int maximumAttempts = 2) =>
        new("request-42", "task-42", "TrentBest/MyVR", workflowPath, targetRef,
            candidateSha, BaseSha, DateTimeOffset.Parse("2026-11-01T00:00:00Z"), maximumAttempts);

    private static CiWorkflowDispatchAuthorization NewAuthorization() =>
        new("authorization-42", "trusted-foundry-host", "request-42", "TrentBest/MyVR",
            "refs/heads/agent/task-42", Sha, 2, DateTimeOffset.Parse("2026-11-02T00:00:00Z"));

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
