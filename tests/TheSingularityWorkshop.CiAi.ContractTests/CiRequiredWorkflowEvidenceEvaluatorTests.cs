using TheSingularityWorkshop.CiAi;

internal static class CiRequiredWorkflowEvidenceEvaluatorTests
{
    public static void Run()
    {
        const string build = ".github/workflows/build.yml";
        const string benchmark = ".github/workflows/benchmark.yml";
        const string observer = ".github/workflows/observe.yml";

        var pass = new CiRequiredWorkflowRunEvidence(build, 10, 1, CiRunStatus.Completed, CiRunConclusion.Success, true);
        var unrelatedPending = new CiRequiredWorkflowRunEvidence(observer, 11, 1, CiRunStatus.InProgress, CiRunConclusion.None, false);
        var results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build }, new[] { pass, unrelatedPending });
        Assert(CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results),
            "unrelated in-progress workflow must not block explicitly scoped required evidence");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build + "#12" },
            new[] { pass, pass with { RunId = 12, Status = CiRunStatus.InProgress, Conclusion = CiRunConclusion.None } });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results) &&
            results.Single().State == "incomplete",
            "an exact run-ID selector must evaluate that run, not a different successful run");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build + "#missing" }, new[] { pass });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results) &&
            results.Single().State == "missing",
            "an explicitly absent required identity must not fall back to another matching run");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build + "#10#2" }, new[] { pass });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results) &&
            results.Single().State == "missing",
            "a stale or different run attempt must not satisfy the required identity");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build, benchmark }, new[] { pass });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results) &&
            results.Single(result => result.WorkflowPath == benchmark).State == "missing",
            "missing required workflow must block");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build },
            new[] { pass with { Status = CiRunStatus.InProgress, Conclusion = CiRunConclusion.None } });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results),
            "in-progress required workflow must block");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build },
            new[] { pass with { Conclusion = CiRunConclusion.Failure } });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results),
            "failed required workflow must block");
        Assert(results.Single().EvidenceComplete && results.Single().Terminal && !results.Single().Successful,
            "a terminal failure must report complete evidence, terminality, and unsuccessful conclusion separately");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build },
            new[] { pass with { JobsComplete = false } });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results),
            "incomplete required job evidence must block");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build }, new[] { pass, pass with { RunId = 12 } });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results) &&
            results.Single().State == "ambiguous",
            "duplicate matching workflow evidence must be treated as ambiguous");

        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(false,
            CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build }, new[] { pass })),
            "truncated or incomplete discovery must block even when required run looks successful");

        results = CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build },
            new[] { pass with { RunId = 0 } });
        Assert(!CiRequiredWorkflowEvidenceEvaluator.IsPassing(true, results) &&
            results.Single().State == "incomplete",
            "a workflow observation without a positive run ID must not pass");

        Throws<ArgumentException>(() => CiRequiredWorkflowEvidenceEvaluator.Evaluate(Array.Empty<string>(), new[] { pass }));
        Throws<ArgumentException>(() => CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build, build }, new[] { pass }));
        Throws<ArgumentException>(() => CiRequiredWorkflowEvidenceEvaluator.Evaluate(
            new[] { ".github/workflows/../evil.yml" }, new[] { pass }));
        Throws<ArgumentException>(() => CiRequiredWorkflowEvidenceEvaluator.Evaluate(
            new[] { ".github/workflows/not-a-workflow.txt" }, new[] { pass }));
        Console.WriteLine("PASS: required-workflow policy integration checks.");
    }

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
