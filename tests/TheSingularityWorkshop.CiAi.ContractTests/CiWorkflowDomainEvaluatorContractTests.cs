using TheSingularityWorkshop.CiAi;

internal static class CiWorkflowDomainEvaluatorContractTests
{
    public static int Run()
    {
        var passed = 0;
        const string repository = "TrentBest/SimpleDemos";
        const string sha = "abcdef0123456789abcdef0123456789abcdef01";

        Check("domain evaluator passes declared exact-commit evidence", () =>
        {
            var result = CiWorkflowDomainEvaluator.Evaluate("build-test", repository, sha,
                new[] { new CiRequiredEvidence("build"), new CiRequiredEvidence("test") },
                new[] {
                    new CiEvidenceFact("build", ReleaseEvidenceState.Passed, repository, sha),
                    new CiEvidenceFact("test", ReleaseEvidenceState.Passed, repository, sha)
                });
            Assert(result.IsPassing && result.DomainName == "Build and test", "valid domain evidence did not pass");
        });
        Check("domain evaluator fails closed for missing evidence", () =>
        {
            var result = CiWorkflowDomainEvaluator.Evaluate("security", repository, sha,
                new[] { new CiRequiredEvidence("security-report") },
                Array.Empty<CiEvidenceFact>());
            Assert(!result.IsPassing && result.Evidence.Errors.Contains("evidence.missing:security-report"),
                "missing security evidence did not fail closed");
        });
        Check("domain evaluator rejects vocabulary from a different workflow family", () =>
        {
            Throws<ArgumentException>(() => CiWorkflowDomainEvaluator.Evaluate("build-test", repository, sha,
                new[] { new CiRequiredEvidence("authorization") },
                Array.Empty<CiEvidenceFact>()));
        });
        Check("domain evaluator preserves exact repository and SHA binding", () =>
        {
            var result = CiWorkflowDomainEvaluator.Evaluate("build-test", repository, sha,
                new[] { new CiRequiredEvidence("build") },
                new[] { new CiEvidenceFact("build", ReleaseEvidenceState.Passed, repository, "123456789abcdef0123456789abcdef012345678") });
            Assert(!result.IsPassing && result.Evidence.Errors.Contains("evidence.identity-mismatch:build"),
                "evidence from another commit was accepted");
        });
        Check("domain evaluator rejects unknown domain IDs", () =>
        {
            Throws<KeyNotFoundException>(() => CiWorkflowDomainEvaluator.Evaluate("not-a-domain", repository, sha,
                Array.Empty<CiRequiredEvidence>(), Array.Empty<CiEvidenceFact>()));
        });
        return passed;

        void Check(string name, Action action)
        {
            action();
            Console.WriteLine($"PASS {name}");
            passed++;
        }
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }
    }
}
