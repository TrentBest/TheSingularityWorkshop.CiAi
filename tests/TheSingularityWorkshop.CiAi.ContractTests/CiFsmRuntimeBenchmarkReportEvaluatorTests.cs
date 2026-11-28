using TheSingularityWorkshop.CiAi;

internal static class CiFsmRuntimeBenchmarkReportEvaluatorTests
{
    private const string Sha = "abcdef0123456789abcdef0123456789abcdef01";

    public static int Run()
    {
        var passed = 0;

        Check("valid exact-source report is accepted and both workloads are recomputed", () =>
        {
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(ValidJson());
            Assert(result.IsAccepted && result.Verdict == "accepted", "valid benchmark report was not accepted");
            Assert(result.SourceSha == Sha && result.HarnessSha == Sha, "exact source provenance was not preserved");
            Assert(result.DirectInstanceUpdate is not null && result.ScheduledGroupUpdate is not null,
                "both runtime views must be projected");
            Assert(Math.Abs(result.DirectInstanceUpdate!.StringPenaltyOverIntegerNanoseconds - 17.712) < 0.000001,
                "direct string penalty was not recomputed from means");
            Assert(Math.Abs(result.ScheduledGroupUpdate!.StringPenaltyOverIntegerNanoseconds - 162.491) < 0.000001,
                "scheduled string penalty was not recomputed from means");
            Assert(result.ExecutiveSummary!.Contains("Intel Xeon 6975P-C", StringComparison.Ordinal) &&
                result.ExecutiveSummary.Contains("shared/virtualized", StringComparison.Ordinal),
                "executive narrative must identify the machine and hosted-runner limitation");
        });

        Check("report-supplied derived fields are not trusted", () =>
        {
            var json = ValidJson().Replace("\"stringPenaltyOverIntegerNs\": 17.712", "\"stringPenaltyOverIntegerNs\": 999999", StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(result.IsAccepted && Math.Abs(result.DirectInstanceUpdate!.StringPenaltyOverIntegerNanoseconds - 17.712) < 0.000001,
                "evaluator trusted a tampered derived field");
        });

        Check("wrong source repository blocks evidence acceptance", () =>
        {
            var json = ValidJson().Replace("TrentBest/FSM_API", "TrentBest/OtherRepo", StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(!result.IsAccepted && result.Errors.Contains("benchmark.source-repository-mismatch"),
                "mismatched source repository was accepted");
        });

        Check("wrong harness repository blocks evidence acceptance", () =>
        {
            var json = ValidJson().Replace("TrentBest/FSM_API_Benchmark", "TrentBest/OtherHarness", StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(!result.IsAccepted && result.Errors.Contains("benchmark.harness-repository-mismatch"),
                "mismatched harness repository was accepted");
        });

        Check("invalid full commit SHA blocks evidence acceptance", () =>
        {
            var json = ValidJson().Replace(Sha, "not-a-full-commit-sha", StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(!result.IsAccepted && result.Errors.Contains("benchmark.source-sha-invalid"),
                "invalid source SHA was accepted");
        });

        Check("missing matched measurement lane blocks evidence acceptance", () =>
        {
            var json = ValidJson().Replace(
                "\"stringScheduled\": {\"meanNs\": 224.798, \"standardDeviationNs\": 0.8328, \"errorNs\": 0.9394, \"medianNs\": 224.7, \"allocatedBytesPerOperation\": 320},",
                string.Empty,
                StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(!result.IsAccepted && result.Errors.Contains("benchmark.integer-scheduled-mean-invalid") == false &&
                result.Errors.Contains("benchmark.string-scheduled-missing"),
                "missing string scheduled lane did not fail closed");
        });

        Check("sub-nanosecond baseline is rejected", () =>
        {
            var json = ValidJson().Replace("\"meanNs\": 5.607", "\"meanNs\": 0.5", StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(!result.IsAccepted && result.Errors.Contains("benchmark.application-baseline-implausible"),
                "implausible baseline was accepted");
        });

        Check("non-finite or negative secondary metrics block evidence", () =>
        {
            var json = ValidJson().Replace("\"allocatedBytesPerOperation\": 40", "\"allocatedBytesPerOperation\": -1", StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(!result.IsAccepted && result.Errors.Contains("benchmark.integer-handle-secondary-metric-invalid"),
                "negative allocation metric was accepted");
        });

        Check("unsupported runtime and missing hosted-runner identity block evidence", () =>
        {
            var json = ValidJson()
                .Replace(".NET 10.0.15", ".NET 8.0.0", StringComparison.Ordinal)
                .Replace("GitHub-hosted Actions runner (shared/virtualized)", "unknown", StringComparison.Ordinal);
            var result = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(json);
            Assert(!result.IsAccepted && result.Errors.Contains("benchmark.runtime-unsupported") &&
                result.Errors.Contains("benchmark.runner-identity-missing"),
                "unsupported runtime or unknown runner was accepted");
        });

        Check("invalid and missing JSON fail closed", () =>
        {
            var invalid = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate("{");
            var missing = CiFsmRuntimeBenchmarkReportEvaluator.Evaluate(null);
            Assert(!invalid.IsAccepted && invalid.Errors.Contains("benchmark.report-json-invalid") &&
                !missing.IsAccepted && missing.Errors.Contains("benchmark.report-missing"),
                "invalid or missing report was accepted");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS FSM runtime benchmark report {passed}: {name}");
        }
    }

    private static string ValidJson() => $$"""
    {
      "schema": "fsm-benchmark.executive-report.v1",
      "status": "measured",
      "generatedUtc": "2026-11-28T01:00:00+00:00",
      "source": {
        "repository": "TrentBest/FSM_API",
        "commitSha": "{{Sha}}",
        "harnessRepository": "TrentBest/FSM_API_Benchmark",
        "harnessCommitSha": "{{Sha}}",
        "workflowRunId": 46090769093,
        "workflowAttempt": 1
      },
      "environment": {
        "runtime": ".NET 10.0.15",
        "sdk": "10.0.401",
        "runner": "GitHub-hosted Actions runner (shared/virtualized)",
        "operatingSystem": "Ubuntu 24.04",
        "architecture": "X64",
        "processor": "Intel Xeon 6975P-C",
        "logicalProcessorCount": 4,
        "physicalCoreCount": 2
      },
      "workloads": {
        "instanceUpdate": { "identity": "direct handle update" },
        "scheduledGroupUpdate": { "identity": "scheduled processing-group update" }
      },
      "measurements": {
        "applicationLogic": { "meanNs": 5.607, "standardDeviationNs": 0.0070, "errorNs": 0.0084, "medianNs": 5.607, "allocatedBytesPerOperation": null },
        "integerHandle": { "meanNs": 38.525, "standardDeviationNs": 0.1287, "errorNs": 0.1541, "medianNs": 38.525, "allocatedBytesPerOperation": 40 },
        "stringHandle": { "meanNs": 56.237, "standardDeviationNs": 0.2864, "errorNs": 0.3231, "medianNs": 56.237, "allocatedBytesPerOperation": 40 },
        "integerScheduled": { "meanNs": 62.307, "standardDeviationNs": 1.4870, "errorNs": 1.2491, "medianNs": 62.307, "allocatedBytesPerOperation": 72 },
        "stringScheduled": { "meanNs": 224.798, "standardDeviationNs": 0.8328, "errorNs": 0.9394, "medianNs": 224.798, "allocatedBytesPerOperation": 320}
      },
      "derived": {
        "instanceUpdate": { "stringPenaltyOverIntegerNs": 17.712 },
        "scheduledGroupUpdate": { "stringPenaltyOverIntegerNs": 162.491 }
      },
      "interpretation": {
        "summary": "synthetic fixture summary",
        "caveat": "test fixture only"
      }
    }
    """;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
