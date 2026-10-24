using TheSingularityWorkshop.CiAi;

var passed = 0;
Check("valid completed success is authoritative", () =>
{
    var run = NewRun();
    Assert(run.IsSuccessful, "successful run was not recognized");
    Assert(run.Identity == "TrentBest/SimpleDemos#42", "run identity is not stable");
});
Check("in-progress run cannot claim a final conclusion", () =>
{
    Throws<ArgumentException>(() => NewRun(status: CiRunStatus.InProgress, conclusion: CiRunConclusion.Success));
});
Check("completed run requires a conclusion", () =>
{
    Throws<ArgumentException>(() => NewRun(status: CiRunStatus.Completed, conclusion: CiRunConclusion.None));
});
Check("repository identity must be owner/repository", () =>
{
    Throws<ArgumentException>(() => NewRun(repository: "SimpleDemos"));
});
Check("evidence must be an HTTPS GitHub Actions run URL", () =>
{
    Throws<ArgumentException>(() => NewRun(url: new Uri("https://example.com/run/42")));
});
Check("evidence must identify the exact requested run", () =>
{
    Throws<ArgumentException>(() => NewRun(url: new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/43")));
});
Check("commit SHA must be a full 40-character hexadecimal identifier", () =>
{
    Throws<ArgumentException>(() => NewRun(headSha: "abcdef0123456789"));
});
Check("evidence URLs reject query strings and fragments", () =>
{
    Throws<ArgumentException>(() => NewRun(url: new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42?redirect=elsewhere")));
});
Check("repository names reject path and URL syntax", () =>
{
    Throws<ArgumentException>(() => NewRun(repository: "TrentBest/../Other"));
});
Check("unknown provider conclusion is not treated as success", () =>
{
    var run = NewRun(conclusion: CiRunConclusion.Unknown);
    Assert(!run.IsSuccessful, "unknown conclusion was treated as success");
});

Check("evidence verifier accepts exact successful observation", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/SimpleDemos", 42, "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Count == 0, "exact successful run should reconcile");
});
Check("evidence verifier rejects a different target repository", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/OtherRepo", 42, "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Contains("repository.mismatch"), "wrong repository was not rejected");
});
Check("evidence verifier rejects a different run ID", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/SimpleDemos", 43, "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Contains("runId.mismatch"), "wrong run ID was not rejected");
});
Check("evidence verifier rejects a different commit SHA", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(), "TrentBest/SimpleDemos", 42, "123456789abcdef0123456789abcdef012345678");
    Assert(errors.Contains("headSha.mismatch"), "wrong commit SHA was not rejected");
});
Check("evidence verifier rejects a failed run", () =>
{
    var errors = CiRunEvidenceVerifier.VerifySuccessfulRun(
        NewRun(conclusion: CiRunConclusion.Failure), "TrentBest/SimpleDemos", 42,
        "abcdef0123456789abcdef0123456789abcdef01");
    Assert(errors.Contains("run.not-successful"), "failed run was not rejected");
});

Console.WriteLine($"PASS: {passed} CiAi contract checks.");
await GitHubActionsRunClientTests.RunAsync();

void Check(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine($"PASS {passed}: {name}");
}

static CiRunSnapshot NewRun(
    string repository = "TrentBest/SimpleDemos",
    long runId = 42,
    CiRunStatus status = CiRunStatus.Completed,
    CiRunConclusion conclusion = CiRunConclusion.Success,
    Uri? url = null,
    string headSha = "abcdef0123456789abcdef0123456789abcdef01") =>
    new(repository, runId, "CiAi contract smoke test", headSha,
        status, conclusion, url ?? new Uri("https://github.com/TrentBest/SimpleDemos/actions/runs/42"),
        DateTimeOffset.Parse("2026-10-23T21:00:00Z"));

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
