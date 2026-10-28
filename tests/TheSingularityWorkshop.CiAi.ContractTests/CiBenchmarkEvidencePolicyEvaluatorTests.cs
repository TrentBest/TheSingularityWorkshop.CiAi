using TheSingularityWorkshop.CiAi;

internal static class CiBenchmarkEvidencePolicyEvaluatorTests
{
    private const string Sha = "abcdef0123456789abcdef0123456789abcdef01";

    public static int Run()
    {
        var passed = 0;
        Check("missing evidence is incomplete", () =>
        {
            var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(null);
            Assert(result.Verdict == "incomplete" && !result.IsPassing,
                "missing benchmark evidence must fail closed");
        });

        Check("smoke evidence without repeated comparable samples is not evaluated", () =>
        {
            var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(Valid() with
            {
                CandidateMeasurementsNanoseconds = new[] { 100.0 },
                BaselineMeasurementsNanoseconds = Array.Empty<double>()
            });
            Assert(result.Verdict == "not-evaluated" && !result.IsPassing &&
                result.Errors.Contains("benchmark.comparable-repeated-measurements-required"),
                "single or missing baseline samples must not produce a performance verdict");
        });

        Check("failed correctness blocks performance verdict", () =>
        {
            var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(Valid() with { CorrectnessPassed = false });
            Assert(result.Verdict == "incomplete" &&
                result.Errors.Contains("benchmark.correctness-not-passing"),
                "incorrect benchmark output must block performance analysis");
        });

        Check("missing report and provenance block performance verdict", () =>
        {
            var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(Valid() with
            {
                ReportsPresent = false,
                ReportCount = 0,
                ProvenanceComplete = false
            });
            Assert(result.Verdict == "incomplete" &&
                result.Errors.Contains("benchmark.reports-missing") &&
                result.Errors.Contains("benchmark.provenance-incomplete"),
                "missing reports or provenance must block");
        });

        Check("repeated comparable measurements pass within tolerance", () =>
        {
            var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(Valid());
            Assert(result.IsPassing && result.Verdict == "passed" &&
                Math.Abs(result.MedianChangePercent!.Value) < 0.001,
                "equivalent medians should pass");
        });

        Check("repeated median regression above tolerance blocks", () =>
        {
            var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(Valid() with
            {
                CandidateMeasurementsNanoseconds = new[] { 120.0, 121.0, 119.0, 120.0, 122.0 }
            });
            Assert(result.Verdict == "regression" && !result.IsPassing &&
                result.Errors.Contains("benchmark.median-regression-exceeds-tolerance"),
                "a repeatable median regression beyond tolerance must block");
        });

        Check("invalid timing values are discarded rather than treated as measurements", () =>
        {
            var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(Valid() with
            {
                CandidateMeasurementsNanoseconds = new[] { 100.0, double.NaN, double.PositiveInfinity, -1.0 }
            });
            Assert(result.Verdict == "not-evaluated" && result.CandidateSampleCount == 1,
                "non-finite and non-positive values must not count as samples");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS benchmark evidence policy {passed}: {name}");
        }
    }

    private static CiBenchmarkEvidence Valid() => new(
        "TrentBest/FSM_Serialization", Sha,
        "TrentBest/FSM_Serialization_Benchmarks", Sha,
        42, 1, "exact-source-project",
        SourceTestsPassed: true, CorrectnessPassed: true, ReportsPresent: true,
        ReportCount: 3, ProvenanceComplete: true,
        CandidateMeasurementsNanoseconds: new[] { 100.0, 101.0, 99.0, 100.0, 102.0 },
        BaselineMeasurementsNanoseconds: new[] { 100.0, 101.0, 99.0, 100.0, 102.0 },
        WorkloadIdentity: "SER-RT-001");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}