using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowRerunRequestLedgerContractTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-rerun-ledger-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "accepted.jsonl");
        try
        {
            var ledger = new FileCiWorkflowRerunRequestLedger(path);
            var policy = new CiWorkflowRerunRequestPolicy("foundry-host", "ciai",
                new[] { "TrentBest/TheSingularityWorkshop.CiAi" },
                new[] { ".github/workflows/contract-verification.yml" });
            var auth = NewAuthorization();
            var first = NewRequest(1, "abcdef0123456789abcdef0123456789abcdef01");
            var target1 = NewTarget(first.HeadSha);
            Assert(ledger.Accept(first, auth, target1, policy, Now).Outcome == CiWorkflowRerunAcceptanceOutcome.Accepted,
                "first valid request was not accepted");
            Assert(ledger.Accept(first, auth, target1, policy, Now).Outcome == CiWorkflowRerunAcceptanceOutcome.Duplicate,
                "identical request retry was not deduplicated");
            var changedAttempt = NewRequest(1, "123456789abcdef0123456789abcdef012345678", requestId: "request-conflict");
            Assert(ledger.Accept(changedAttempt, auth, NewTarget(changedAttempt.HeadSha), policy, Now).Outcome ==
                CiWorkflowRerunAcceptanceOutcome.Conflict, "same task attempt with changed candidate was not rejected");
            var second = NewRequest(2, "123456789abcdef0123456789abcdef012345678");
            Assert(ledger.Accept(second, auth, NewTarget(second.HeadSha), policy, Now).Outcome == CiWorkflowRerunAcceptanceOutcome.Accepted,
                "next sequential attempt was not accepted");
            var gapLedger = new FileCiWorkflowRerunRequestLedger(Path.Combine(root, "gap.jsonl"));
            Assert(gapLedger.Accept(first, auth, target1, policy, Now).Outcome == CiWorkflowRerunAcceptanceOutcome.Accepted,
                "gap fixture first attempt was not accepted");
            var gap = NewRequest(3, second.HeadSha);
            Assert(gapLedger.Accept(gap, auth, NewTarget(gap.HeadSha), policy, Now).BlockingReasons.Contains("rerun.attempt-sequence-gap"),
                "attempt sequence gap was not rejected");
            var reopened = new FileCiWorkflowRerunRequestLedger(path);
            Assert(reopened.ReadAccepted().Count == 2, "accepted requests were not durable across reopen");
            var denied = NewAuthorization(scopes: new[] { "read" });
            var third = NewRequest(3, second.HeadSha);
            Assert(reopened.Accept(third, denied, NewTarget(third.HeadSha), policy, Now).Outcome == CiWorkflowRerunAcceptanceOutcome.Rejected,
                "unauthorized request was accepted");
            var tornPath = Path.Combine(root, "torn.jsonl");
            File.WriteAllText(tornPath, "partial");
            Throws<InvalidDataException>(() => new FileCiWorkflowRerunRequestLedger(tornPath).ReadAccepted());
            Console.WriteLine("PASS: 6 durable rerun request ledger contract checks.");
            return 6;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-30T22:00:00Z");
    private static CiWorkflowRerunRequest NewRequest(int attempt, string sha, string? requestId = null) =>
        new(requestId ?? $"request-{attempt}", "correlation-1", "task-1", "TrentBest/TheSingularityWorkshop.CiAi",
            "refs/heads/feature/agent-fix", sha, ".github/workflows/contract-verification.yml",
            $"TrentBest/TheSingularityWorkshop.CiAi:task-1:{attempt}", attempt, 3, Now, Now.AddMinutes(10));
    private static CiWorkflowRerunTarget NewTarget(string sha) =>
        new("TrentBest/TheSingularityWorkshop.CiAi", "refs/heads/feature/agent-fix", sha,
            ".github/workflows/contract-verification.yml");
    private static CiDispatchAuthorizationContext NewAuthorization(IReadOnlyCollection<string>? scopes = null) =>
        new("foundry-host", "ciai", "foundry-service", "credential-123",
            scopes ?? new[] { CiDispatchAuthorizationContext.RequiredScope },
            new[] { "TrentBest/TheSingularityWorkshop.CiAi" }, Now.AddMinutes(-1), Now.AddMinutes(30));
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<TException>(Action action) where TException : Exception
    { try { action(); } catch (TException) { return; } throw new InvalidOperationException($"Expected {typeof(TException).Name}."); }
}
