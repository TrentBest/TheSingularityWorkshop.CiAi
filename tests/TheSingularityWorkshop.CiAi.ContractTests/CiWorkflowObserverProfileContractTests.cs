using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowObserverProfileContractTests
{
    public static void Run()
    {
        var profile = new CiWorkflowObserverProfile(
            "dispatch-test",
            ".github/workflows/ci.yml",
            "workflow-provenance-{attempt}",
            "test-results-{attempt}",
            "workflow-provenance.json",
            "results.trx",
            "dotnet-vstest",
            "build",
            "Build",
            "Resolve candidate",
            "Verify candidate checkout");
        Assert(profile.ResolutionStepName == "Resolve candidate" &&
            profile.VerificationStepName == "Verify candidate checkout",
            "observer profile did not preserve candidate checkout step names");
        Assert(profile.TestArtifactNameForDispatch(1) == "test-results-1",
            "pre-dispatch artifact name did not resolve the attempt-only template");

        var runIdProfile = new CiWorkflowObserverProfile(
            "run-id-test",
            ".github/workflows/ci.yml",
            "workflow-provenance-{runId}-{attempt}",
            "test-results-{runId}-{attempt}",
            "workflow-provenance.json",
            "results.trx",
            "dotnet-vstest",
            "build",
            "Build");
        AssertThrows<InvalidOperationException>(() => runIdProfile.TestArtifactNameForDispatch(1));
        AssertThrows<ArgumentException>(() => new CiWorkflowObserverProfile(
            "partial-step-pair",
            ".github/workflows/ci.yml",
            "workflow-provenance-{attempt}",
            "test-results-{attempt}",
            "workflow-provenance.json",
            "results.trx",
            "dotnet-vstest",
            "build",
            "Build",
            "Resolve candidate"));
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
