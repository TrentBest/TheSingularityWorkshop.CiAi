namespace TheSingularityWorkshop.CiAi;

/// <summary>One machine-readable fact emitted by a benchmark host adapter.</summary>
public sealed record CiBenchmarkEvidence(
    string SourceRepository,
    string SourceSha,
    string HarnessRepository,
    string HarnessSha,
    long RunId,
    int RunAttempt,
    string Lane,
    bool SourceTestsPassed,
    bool CorrectnessPassed,
    bool ReportsPresent,
    int ReportCount,
    bool ProvenanceComplete,
    IReadOnlyList<double> CandidateMeasurementsNanoseconds,
    IReadOnlyList<double> BaselineMeasurementsNanoseconds,
    string WorkloadIdentity);

/// <summary>Policy outcome. Incomplete evidence is distinct from a measured regression.</summary>
public sealed record CiBenchmarkPolicyResult(
    string Verdict,
    IReadOnlyList<string> Errors,
    double? CandidateMedianNanoseconds,
    double? BaselineMedianNanoseconds,
    double? MedianChangePercent,
    int CandidateSampleCount,
    int BaselineSampleCount)
{
    public bool IsPassing => Verdict == "passed" && Errors.Count == 0;
}

/// <summary>
/// Conservative benchmark policy evaluator. It never calls a single smoke sample a performance
/// comparison: comparable, repeated measurements are required before a regression verdict exists.
/// </summary>
public static class CiBenchmarkEvidencePolicyEvaluator
{
    public static CiBenchmarkPolicyResult Evaluate(
        CiBenchmarkEvidence? evidence,
        int minimumComparableSamples = 5,
        double regressionTolerancePercent = 5.0)
    {
        var errors = new List<string>();
        if (evidence is null)
            return Incomplete("benchmark.evidence-missing");

        if (!RepositoryIdentity.IsValid(evidence.SourceRepository))
            errors.Add("benchmark.source-repository-invalid");
        if (!RepositoryIdentity.IsValid(evidence.HarnessRepository))
            errors.Add("benchmark.harness-repository-invalid");
        if (!IsSha(evidence.SourceSha))
            errors.Add("benchmark.source-sha-invalid");
        if (!IsSha(evidence.HarnessSha))
            errors.Add("benchmark.harness-sha-invalid");
        if (evidence.RunId <= 0 || evidence.RunAttempt <= 0)
            errors.Add("benchmark.run-identity-invalid");
        if (string.IsNullOrWhiteSpace(evidence.Lane))
            errors.Add("benchmark.lane-missing");
        if (string.IsNullOrWhiteSpace(evidence.WorkloadIdentity))
            errors.Add("benchmark.workload-identity-missing");
        if (!evidence.SourceTestsPassed)
            errors.Add("benchmark.source-tests-not-passing");
        if (!evidence.CorrectnessPassed)
            errors.Add("benchmark.correctness-not-passing");
        if (!evidence.ReportsPresent || evidence.ReportCount <= 0)
            errors.Add("benchmark.reports-missing");
        if (!evidence.ProvenanceComplete)
            errors.Add("benchmark.provenance-incomplete");

        if (minimumComparableSamples < 2)
            throw new ArgumentOutOfRangeException(nameof(minimumComparableSamples), "At least two measurements are required.");
        if (!double.IsFinite(regressionTolerancePercent) || regressionTolerancePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(regressionTolerancePercent));

        var candidate = ValidMeasurements(evidence.CandidateMeasurementsNanoseconds);
        var baseline = ValidMeasurements(evidence.BaselineMeasurementsNanoseconds);
        if (errors.Count > 0)
            return new("incomplete", errors.AsReadOnly(), MedianOrNull(candidate), MedianOrNull(baseline),
                PercentChange(candidate, baseline), candidate.Length, baseline.Length);

        if (candidate.Length < minimumComparableSamples || baseline.Length < minimumComparableSamples)
        {
            errors.Add("benchmark.comparable-repeated-measurements-required");
            return new("not-evaluated", errors.AsReadOnly(), MedianOrNull(candidate), MedianOrNull(baseline),
                PercentChange(candidate, baseline), candidate.Length, baseline.Length);
        }

        var candidateMedian = Median(candidate);
        var baselineMedian = Median(baseline);
        var change = ((candidateMedian - baselineMedian) / baselineMedian) * 100.0;
        var verdict = change > regressionTolerancePercent ? "regression" : "passed";
        if (verdict == "regression")
            errors.Add("benchmark.median-regression-exceeds-tolerance");
        return new(verdict, errors.AsReadOnly(), candidateMedian, baselineMedian, change,
            candidate.Length, baseline.Length);
    }

    private static CiBenchmarkPolicyResult Incomplete(string error) =>
        new("incomplete", Array.AsReadOnly(new[] { error }), null, null, null, 0, 0);

    private static bool IsSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static double[] ValidMeasurements(IReadOnlyList<double>? values) =>
        values?.Where(value => double.IsFinite(value) && value > 0).Order().ToArray() ?? Array.Empty<double>();

    private static double? MedianOrNull(double[] values) => values.Length == 0 ? null : Median(values);

    private static double? PercentChange(double[] candidate, double[] baseline) =>
        candidate.Length == 0 || baseline.Length == 0 ? null :
            ((Median(candidate) - Median(baseline)) / Median(baseline)) * 100.0;

    private static double Median(double[] sorted)
    {
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? sorted[middle - 1] / 2.0 + sorted[middle] / 2.0
            : sorted[middle];
    }
}