using TheSingularityWorkshop.CiAi;

internal static class CiExactWorkflowRunBindingEvaluatorTests
{
    public static void Run()
    {
        const string path = ".github/workflows/ci.yml";
        const string workflowRef = "refs/heads/development";
        const string eventName = "push";
        const string selector = path + "#42#2";

        var valid = Observation();
        Assert(CiExactWorkflowRunBindingEvaluator.IsValid(
            new[] { selector }, new[] { valid }, "TrentBest/SimpleDemos", new string('a', 40), workflowRef, eventName),
            "exact workflow path, run, attempt, ref, and event should bind");

        Assert(!Evaluate(new[] { selector }, new[] { valid with { WorkflowRef = "refs/heads/master" } }),
            "wrong provider workflow ref must fail closed");
        Assert(!Evaluate(new[] { selector }, new[] { valid with { Event = "pull_request" } }),
            "wrong provider event must fail closed");
        Assert(!Evaluate(new[] { selector }, new[] { Observation(headSha: new string('b', 40)) }),
            "wrong source SHA must fail closed");
        Assert(!Evaluate(new[] { selector }, new[] { Observation(repository: "Other/Repo") }),
            "wrong source repository must fail closed");

        Assert(!Evaluate(new[] { selector }, Array.Empty<CiWorkflowRunObservation>()),
            "zero matching runs must fail closed");
        Assert(!Evaluate(new[] { selector }, new[] { valid, valid with { Event = "push" } }),
            "multiple matching run observations must fail closed");
        Assert(!Evaluate(new[] { path + "#42#1" }, new[] { valid }),
            "stale or ambiguous attempt must fail closed");
        Assert(!Evaluate(new[] { path + "#42#0" }, new[] { valid }),
            "zero attempt selector must fail closed");
        Assert(!Evaluate(new[] { path + "#not-a-run#2" }, new[] { valid }),
            "malformed run selector must fail closed");
        Assert(!CiExactWorkflowRunBindingEvaluator.IsValid(
            new[] { selector }, new[] { valid }, "TrentBest/SimpleDemos", new string('a', 40), null, eventName),
            "missing expected workflow ref must fail closed");
        Assert(!CiExactWorkflowRunBindingEvaluator.IsValid(
            new[] { selector }, new[] { valid }, "TrentBest/SimpleDemos", new string('a', 40), workflowRef, null),
            "missing expected event must fail closed");
        Assert(!Evaluate(new[] { selector, selector }, new[] { valid }),
            "duplicate selector must fail closed");
        Assert(!Evaluate(new[] { selector }, new[] { valid with { ProviderStatus = "in_progress" } }),
            "nonterminal provider status must fail closed");
        Assert(!Evaluate(new[] { selector }, new[] { valid with
        {
            Run = new CiRunSnapshot(
                "TrentBest/SimpleDemos", 42, "CiAi Contract Verification", new string('a', 40),
                CiRunStatus.InProgress, CiRunConclusion.None,
                new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
                DateTimeOffset.Parse("2026-11-13T12:00:00Z"))
        }}), "nonterminal normalized status must fail closed");

        Console.WriteLine("PASS: exact workflow run binding fail-closed matrix (13 checks).");
    }

    private static bool Evaluate(
        IReadOnlyCollection<string> selectors,
        IReadOnlyCollection<CiWorkflowRunObservation> observations)
        => CiExactWorkflowRunBindingEvaluator.IsValid(
            selectors, observations, "TrentBest/SimpleDemos", new string('a', 40),
            "refs/heads/development", "push");

    private static CiWorkflowRunObservation Observation(
        string repository = "TrentBest/SimpleDemos",
        string? headSha = null)
    {
        var run = new CiRunSnapshot(
            repository,
            42,
            "CiAi Contract Verification",
            headSha ?? new string('a', 40),
            CiRunStatus.Completed,
            CiRunConclusion.Success,
            new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
            DateTimeOffset.Parse("2026-11-13T12:00:00Z"));
        return new CiWorkflowRunObservation(run, 2, ".github/workflows/ci.yml", "push", Array.Empty<int>())
        {
            WorkflowRef = "refs/heads/development",
            ProviderStatus = "completed"
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
