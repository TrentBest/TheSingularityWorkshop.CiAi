using TheSingularityWorkshop.CiAi;

internal static class CiRequiredEvidenceEvaluatorTests
{
    private const string Repository = "TrentBest/SimpleDemos";
    private const string Sha = "abcdef0123456789abcdef0123456789abcdef01";

    public static int Run()
    {
        var passed = 0;
        Check("required evidence evaluator rejects an empty policy", () =>
            Throws<ArgumentException>(() => CiRequiredEvidenceEvaluator.Evaluate(
                Repository, Sha, Array.Empty<CiRequiredEvidence>(), Array.Empty<CiEvidenceFact>())));

        Check("required evidence evaluator rejects an all-optional policy", () =>
            Throws<ArgumentException>(() => CiRequiredEvidenceEvaluator.Evaluate(
                Repository, Sha, new[] { new CiRequiredEvidence("build", Required: false) },
                Array.Empty<CiEvidenceFact>())));

        Check("required evidence evaluator accepts one exact passing required gate", () =>
        {
            var result = CiRequiredEvidenceEvaluator.Evaluate(Repository, Sha,
                new[] { new CiRequiredEvidence("build") },
                new[] { new CiEvidenceFact("build", ReleaseEvidenceState.Passed, Repository, Sha) });
            Assert(result.IsPassing, "exact passing evidence did not pass");
        });

        Check("required evidence evaluator fails closed when required evidence is absent", () =>
        {
            var result = CiRequiredEvidenceEvaluator.Evaluate(Repository, Sha,
                new[] { new CiRequiredEvidence("build") }, Array.Empty<CiEvidenceFact>());
            Assert(!result.IsPassing && result.Requirements["build"] == ReleaseEvidenceState.Missing,
                "missing evidence was not kept non-passing");
        });

        Check("required evidence evaluator rejects duplicate facts", () =>
        {
            var fact = new CiEvidenceFact("build", ReleaseEvidenceState.Passed, Repository, Sha);
            var result = CiRequiredEvidenceEvaluator.Evaluate(Repository, Sha,
                new[] { new CiRequiredEvidence("build") }, new[] { fact, fact });
            Assert(!result.IsPassing && result.Requirements["build"] == ReleaseEvidenceState.Unknown,
                "duplicate facts were accepted as authoritative");
        });

        Check("required evidence evaluator rejects evidence from another SHA", () =>
        {
            var result = CiRequiredEvidenceEvaluator.Evaluate(Repository, Sha,
                new[] { new CiRequiredEvidence("build") },
                new[] { new CiEvidenceFact("build", ReleaseEvidenceState.Passed, Repository,
                    "123456789abcdef0123456789abcdef012345678") });
            Assert(!result.IsPassing && result.Requirements["build"] == ReleaseEvidenceState.Unknown,
                "evidence from a different candidate SHA was accepted");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS evidence policy {passed}: {name}");
        }
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
