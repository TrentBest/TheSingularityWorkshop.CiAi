using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowRerunRequestContractTests
{
    public static int Run()
    {
        var passed = 0;
        var now = DateTimeOffset.Parse("2026-10-30T22:00:00Z");
        var request = NewRequest();
        var auth = NewAuthorization();
        var policy = NewPolicy();
        var target = NewTarget();

        Check("rerun request accepts an authorized exact feature-branch candidate", () =>
            Assert(policy.Evaluate(request, auth, target, now).Count == 0, "valid request was blocked"));
        Check("rerun request normalizes full SHA", () =>
            Assert(request.HeadSha == "abcdef0123456789abcdef0123456789abcdef01", "SHA was not normalized"));
        Check("rerun request rejects development and master refs", () =>
        {
            Throws<ArgumentException>(() => NewRequest(headRef: "refs/heads/development"));
            Throws<ArgumentException>(() => NewRequest(headRef: "refs/heads/master"));
        });
        Check("rerun request rejects malformed and non-workflow paths", () =>
        {
            Throws<ArgumentException>(() => NewRequest(workflowPath: "../build.yml"));
            Throws<ArgumentException>(() => NewRequest(workflowPath: ".github/workflows/../build.yml"));
        });
        Check("rerun request bounds attempts to five", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => NewRequest(maximumAttempts: 6));
            Throws<ArgumentOutOfRangeException>(() => NewRequest(attemptNumber: 3, maximumAttempts: 2));
        });
        Check("rerun request bounds lifetime to thirty minutes", () =>
            Throws<ArgumentException>(() => NewRequest(expiresAt: now.AddMinutes(31))));
        Check("policy rejects a different repository and workflow", () =>
        {
            var wrongRepository = NewPolicy(allowedRepositories: new[] { "TrentBest/Other" });
            Assert(wrongRepository.Evaluate(request, auth, target, now).Contains("rerun.repository-not-allowed"),
                "repository mismatch was not reported");
            var wrongWorkflow = NewPolicy(allowedWorkflowPaths: new[] { ".github/workflows/release.yml" });
            Assert(wrongWorkflow.Evaluate(request, auth, target, now).Contains("rerun.workflow-not-allowed"),
                "workflow mismatch was not reported");
        });
        Check("policy rejects stale branch SHA and mismatched ref", () =>
        {
            var stale = new CiWorkflowRerunTarget(request.Repository, request.HeadRef,
                "123456789abcdef0123456789abcdef012345678", request.WorkflowPath);
            Assert(policy.Evaluate(request, auth, stale, now).Contains("rerun.target-mismatch-or-stale"),
                "stale SHA was accepted");
            var wrongRef = new CiWorkflowRerunTarget(request.Repository, "refs/heads/feature/other",
                request.HeadSha, request.WorkflowPath);
            Assert(policy.Evaluate(request, auth, wrongRef, now).Contains("rerun.target-mismatch-or-stale"),
                "mismatched ref was accepted");
        });
        Check("policy requires issuer, audience, and dispatch scope", () =>
        {
            var wrong = NewAuthorization(issuer: "untrusted", audience: "other", scopes: new[] { "read" });
            var reasons = policy.Evaluate(request, wrong, target, now);
            Assert(reasons.Contains("rerun.authorization-issuer-mismatch") &&
                reasons.Contains("rerun.authorization-audience-mismatch") &&
                reasons.Contains("rerun.authorization-scope-missing"), "authorization claims were not enforced");
        });
        Check("policy requires repository-specific authorization", () =>
        {
            var wrong = NewAuthorization(authorizedRepositories: new[] { "TrentBest/Other" });
            Assert(policy.Evaluate(request, wrong, target, now).Contains("rerun.repository-not-authorized"),
                "repository authorization was not enforced");
        });
        Check("policy rejects expired requests and expired authorization", () =>
        {
            Assert(policy.Evaluate(request, auth, target, now.AddMinutes(11))
                .Contains("rerun.request-expired-or-not-yet-valid"), "expired request was accepted");
            var expired = NewAuthorization(expiresAt: now.AddSeconds(-30));
            Assert(policy.Evaluate(request, expired, target, now).Contains("rerun.authorization-expired-or-not-yet-valid"),
                "expired authorization was accepted");
        });
        Check("policy rejects missing request or authorization", () =>
        {
            Assert(policy.Evaluate(null, auth, target, now).Contains("rerun.request-required"), "missing request accepted");
            Assert(policy.Evaluate(request, null, target, now).Contains("rerun.authorization-required"), "missing auth accepted");
            Assert(policy.Evaluate(request, auth, null, now).Contains("rerun.current-target-required"), "missing target accepted");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS rerun {passed}: {name}");
        }
    }

    private static CiWorkflowRerunRequest NewRequest(
        string headRef = "refs/heads/feature/agent-fix",
        string workflowPath = ".github/workflows/contract-verification.yml",
        int attemptNumber = 1,
        int maximumAttempts = 3,
        DateTimeOffset? expiresAt = null) =>
        new("request-1", "correlation-1", "task-1", "TrentBest/TheSingularityWorkshop.CiAi",
            headRef, "ABCDEF0123456789ABCDEF0123456789ABCDEF01", workflowPath, "task-1:attempt-1",
            attemptNumber, maximumAttempts, DateTimeOffset.Parse("2026-10-30T22:00:00Z"),
            expiresAt ?? DateTimeOffset.Parse("2026-10-30T22:10:00Z"));

    private static CiWorkflowRerunTarget NewTarget() =>
        new("TrentBest/TheSingularityWorkshop.CiAi", "refs/heads/feature/agent-fix",
            "abcdef0123456789abcdef0123456789abcdef01",
            ".github/workflows/contract-verification.yml");

    private static CiDispatchAuthorizationContext NewAuthorization(
        string issuer = "foundry-host",
        string audience = "ciai",
        IReadOnlyCollection<string>? scopes = null,
        IReadOnlyCollection<string>? authorizedRepositories = null,
        DateTimeOffset? expiresAt = null) =>
        new(issuer, audience, "foundry-service", "credential-123",
            scopes ?? new[] { CiDispatchAuthorizationContext.RequiredScope },
            authorizedRepositories ?? new[] { "TrentBest/TheSingularityWorkshop.CiAi" },
            DateTimeOffset.Parse("2026-10-30T21:59:00Z"),
            expiresAt ?? DateTimeOffset.Parse("2026-10-30T22:30:00Z"));

    private static CiWorkflowRerunRequestPolicy NewPolicy(
        IEnumerable<string>? allowedRepositories = null,
        IEnumerable<string>? allowedWorkflowPaths = null) =>
        new("foundry-host", "ciai",
            allowedRepositories ?? new[] { "TrentBest/TheSingularityWorkshop.CiAi" },
            allowedWorkflowPaths ?? new[] { ".github/workflows/contract-verification.yml" });

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
