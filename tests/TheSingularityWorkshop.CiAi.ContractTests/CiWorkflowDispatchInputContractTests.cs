using System.Collections.Generic;
using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowDispatchInputContractTests
{
    public static int Run()
    {
        var request = new CiWorkflowRerunRequest(
            "request-1", "correlation-1", "task-1", "TrentBest/SimpleDemos",
            "refs/heads/feature/agent-fix",
            "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            ".github/workflows/ci.yml",
            "TrentBest/SimpleDemos:task-1:2",
            2, 5,
            DateTimeOffset.Parse("2026-10-31T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-31T00:10:00Z"));

        var inputs = CiWorkflowDispatchInputContract.CreateInputs(request);
        Assert(inputs.Count == 6, "dispatch mapping did not produce the exact six-input contract");
        Assert(inputs[CiWorkflowDispatchInputContract.TargetBranchInput] == "feature/agent-fix",
            "full Git ref was not converted to the workflow branch input");
        Assert(inputs[CiWorkflowDispatchInputContract.RequestIdInput] == "request-1",
            "request identity was not preserved");
        Assert(inputs[CiWorkflowDispatchInputContract.CorrelationIdInput] == "correlation-1",
            "correlation identity was not preserved");
        Assert(inputs[CiWorkflowDispatchInputContract.TaskIdInput] == "task-1",
            "task identity was not preserved");
        Assert(inputs[CiWorkflowDispatchInputContract.CandidateShaInput] ==
            "abcdef0123456789abcdef0123456789abcdef01",
            "candidate SHA was not normalized");
        Assert(inputs[CiWorkflowDispatchInputContract.AttemptNumberInput] == "2",
            "attempt number was not formatted invariantly");
        Assert(!inputs.ContainsKey("token") && !inputs.ContainsKey("authorization"),
            "dispatch input mapping exposed a credential field");

        if (inputs is IDictionary<string, string> mutable)
            AssertThrows<NotSupportedException>(() => mutable[CiWorkflowDispatchInputContract.RequestIdInput] = "changed");

        AssertThrows<ArgumentNullException>(() => CiWorkflowDispatchInputContract.CreateInputs(null!));
        Console.WriteLine("PASS: 10 correlated workflow dispatch input contract checks.");
        return 10;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }
}
