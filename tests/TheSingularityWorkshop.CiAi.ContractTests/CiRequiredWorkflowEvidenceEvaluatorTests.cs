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

        Throws<ArgumentException>(() => CiRequiredWorkflowEvidenceEvaluator.Evaluate(Array.Empty<string>(), new[] { pass }));
        Throws<ArgumentException>(() => CiRequiredWorkflowEvidenceEvaluator.Evaluate(new[] { build, build }, new[] { pass }));
        Console.WriteLine("PASS: 9 required-workflow policy integration checks.");
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
