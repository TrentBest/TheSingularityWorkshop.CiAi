using System.Globalization;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A recomputed comparison between integer-backed and string-backed FSM execution.
/// Values are derived from the report's measured means; report-supplied derived fields are not trusted.
/// </summary>
public sealed record CiRuntimeBenchmarkComparison(
    string WorkloadIdentity,
    double ApplicationLogicMeanNanoseconds,
    double IntegerMeanNanoseconds,
    double StringMeanNanoseconds,
    double IntegerRuntimeOverheadNanoseconds,
    double IntegerRuntimeOverheadPercent,
    double StringRuntimeOverheadNanoseconds,
    double StringRuntimeOverheadPercent,
    double StringPenaltyOverIntegerNanoseconds,
    double StringPenaltyOverIntegerPercent,
    double? IntegerStandardDeviationNanoseconds,
    double? StringStandardDeviationNanoseconds,
    double? IntegerErrorNanoseconds,
    double? StringErrorNanoseconds,
    double? IntegerAllocatedBytesPerOperation,
    double? StringAllocatedBytesPerOperation);

/// <summary>
/// Validated summary of an FSM runtime benchmark report. Accepted means structurally and provenance
/// complete; it is not a pass/fail performance-regression verdict.
/// </summary>
public sealed record CiFsmRuntimeBenchmarkReportResult(
    string Verdict,
    IReadOnlyList<string> Errors,
    string? SourceRepository,
    string? SourceSha,
    string? HarnessRepository,
    string? HarnessSha,
    long? WorkflowRunId,
    int? WorkflowAttempt,
    string? Runtime,
    string? Runner,
    string? Processor,
    CiRuntimeBenchmarkComparison? DirectInstanceUpdate,
    CiRuntimeBenchmarkComparison? ScheduledGroupUpdate,
    string? ExecutiveSummary)
{
    public bool IsAccepted => Verdict == "accepted" && Errors.Count == 0;
}

/// <summary>
/// Validates and translates the executive FSM benchmark JSON into a provenance-bound CiAi evidence
/// projection. It recomputes every overhead and penalty from measured means and never fabricates
/// raw samples from aggregate statistics.
/// </summary>
public static class CiFsmRuntimeBenchmarkReportEvaluator
{
    public const string ExpectedSchema = "fsm-benchmark.executive-report.v1";
    public const string DefaultSourceRepository = "TrentBest/FSM_API";
    public const string DefaultHarnessRepository = "TrentBest/FSM_API_Benchmark";

    private sealed record Measurement(
        double MeanNanoseconds,
        double? StandardDeviationNanoseconds,
        double? ErrorNanoseconds,
        double? AllocatedBytesPerOperation);

    public static CiFsmRuntimeBenchmarkReportResult Evaluate(
        string? json,
        string expectedSourceRepository = DefaultSourceRepository,
        string expectedHarnessRepository = DefaultHarnessRepository)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return Incomplete("benchmark.report-missing");

        if (!RepositoryIdentity.IsValid(expectedSourceRepository))
            throw new ArgumentException("Expected source repository must be canonical owner/repository.", nameof(expectedSourceRepository));
        if (!RepositoryIdentity.IsValid(expectedHarnessRepository))
            throw new ArgumentException("Expected harness repository must be canonical owner/repository.", nameof(expectedHarnessRepository));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Incomplete("benchmark.report-json-invalid");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Incomplete("benchmark.report-root-invalid");

            var schema = ReadString(root, "schema");
            var status = ReadString(root, "status");
            if (!string.Equals(schema, ExpectedSchema, StringComparison.Ordinal))
                errors.Add("benchmark.report-schema-unsupported");
            if (!string.Equals(status, "measured", StringComparison.Ordinal))
                errors.Add("benchmark.report-not-measured");

            var source = ReadObject(root, "source");
            var sourceRepository = ReadString(source, "repository");
            var sourceSha = ReadString(source, "commitSha");
            var harnessRepository = ReadString(source, "harnessRepository");
            var harnessSha = ReadString(source, "harnessCommitSha");
            var runId = ReadInt64(source, "workflowRunId");
            var runAttempt = ReadInt32(source, "workflowAttempt");

            if (!RepositoryIdentity.IsValid(sourceRepository))
                errors.Add("benchmark.source-repository-invalid");
            else if (!string.Equals(sourceRepository, expectedSourceRepository, StringComparison.OrdinalIgnoreCase))
                errors.Add("benchmark.source-repository-mismatch");
            if (!IsSha(sourceSha))
                errors.Add("benchmark.source-sha-invalid");
            if (!RepositoryIdentity.IsValid(harnessRepository))
                errors.Add("benchmark.harness-repository-invalid");
            else if (!string.Equals(harnessRepository, expectedHarnessRepository, StringComparison.OrdinalIgnoreCase))
                errors.Add("benchmark.harness-repository-mismatch");
            if (!IsSha(harnessSha))
                errors.Add("benchmark.harness-sha-invalid");
            if (runId is null or <= 0 || runAttempt is null or <= 0)
                errors.Add("benchmark.run-identity-invalid");

            var environment = ReadObject(root, "environment");
            var runtime = ReadString(environment, "runtime");
            var runner = ReadString(environment, "runner");
            var processor = ReadString(environment, "processor");
            var operatingSystem = ReadString(environment, "operatingSystem");
            var architecture = ReadString(environment, "architecture");
            if (string.IsNullOrWhiteSpace(runtime) || !runtime.StartsWith(".NET 10", StringComparison.Ordinal))
                errors.Add("benchmark.runtime-unsupported");
            if (string.IsNullOrWhiteSpace(runner) || !runner.Contains("GitHub-hosted", StringComparison.OrdinalIgnoreCase))
                errors.Add("benchmark.runner-identity-missing");
            if (string.IsNullOrWhiteSpace(processor))
                errors.Add("benchmark.processor-missing");
            if (string.IsNullOrWhiteSpace(operatingSystem) || string.IsNullOrWhiteSpace(architecture))
                errors.Add("benchmark.environment-incomplete");

            var workloads = ReadObject(root, "workloads");
            if (!string.Equals(ReadString(ReadObject(workloads, "instanceUpdate"), "identity"),
                    "direct handle update", StringComparison.Ordinal))
                errors.Add("benchmark.direct-workload-identity-invalid");
            if (!string.Equals(ReadString(ReadObject(workloads, "scheduledGroupUpdate"), "identity"),
                    "scheduled processing-group update", StringComparison.Ordinal))
                errors.Add("benchmark.scheduled-workload-identity-invalid");

            var measurements = ReadObject(root, "measurements");
            var baseline = ReadMeasurement(measurements, "applicationLogic", "benchmark.application-baseline", errors);
            var integerHandle = ReadMeasurement(measurements, "integerHandle", "benchmark.integer-handle", errors);
            var stringHandle = ReadMeasurement(measurements, "stringHandle", "benchmark.string-handle", errors);
            var integerScheduled = ReadMeasurement(measurements, "integerScheduled", "benchmark.integer-scheduled", errors);
            var stringScheduled = ReadMeasurement(measurements, "stringScheduled", "benchmark.string-scheduled", errors);

            if (baseline is not null && baseline.MeanNanoseconds < 1.0)
                errors.Add("benchmark.application-baseline-implausible");

            CiRuntimeBenchmarkComparison? direct = null;
            CiRuntimeBenchmarkComparison? scheduled = null;
            if (baseline is not null && integerHandle is not null && stringHandle is not null)
                direct = Compare("direct handle update", baseline, integerHandle, stringHandle);
            if (baseline is not null && integerScheduled is not null && stringScheduled is not null)
                scheduled = Compare("scheduled processing-group update", baseline, integerScheduled, stringScheduled);

            var summary = direct is not null && scheduled is not null && errors.Count == 0
                ? BuildExecutiveSummary(processor!, runtime!, baseline!, direct, scheduled, runner!)
                : null;

            return new CiFsmRuntimeBenchmarkReportResult(
                errors.Count == 0 ? "accepted" : "incomplete",
                errors.AsReadOnly(),
                sourceRepository,
                sourceSha,
                harnessRepository,
                harnessSha,
                runId,
                runAttempt,
                runtime,
                runner,
                processor,
                direct,
                scheduled,
                summary);
        }
    }

    private static Measurement? ReadMeasurement(JsonElement measurements, string key, string errorPrefix, List<string> errors)
    {
        var item = ReadObject(measurements, key);
        if (item.ValueKind != JsonValueKind.Object)
        {
            errors?.Add(errorPrefix + "-missing");
            return null;
        }

        var mean = ReadDouble(item, "meanNs");
        var deviation = ReadOptionalDouble(item, "standardDeviationNs");
        var error = ReadOptionalDouble(item, "errorNs");
        var allocation = ReadOptionalDouble(item, "allocatedBytesPerOperation");
        if (mean is null || !double.IsFinite(mean.Value) || mean.Value <= 0)
        {
            errors?.Add(errorPrefix + "-mean-invalid");
            return null;
        }
        if (IsInvalidNonNegative(deviation) || IsInvalidNonNegative(error) || IsInvalidNonNegative(allocation))
        {
            errors?.Add(errorPrefix + "-secondary-metric-invalid");
            return null;
        }
        return new Measurement(mean.Value, deviation, error, allocation);
    }

    private static CiRuntimeBenchmarkComparison Compare(
        string identity,
        Measurement baseline,
        Measurement integer,
        Measurement text)
    {
        var integerOverhead = integer.MeanNanoseconds - baseline.MeanNanoseconds;
        var stringOverhead = text.MeanNanoseconds - baseline.MeanNanoseconds;
        var stringPenalty = text.MeanNanoseconds - integer.MeanNanoseconds;
        return new CiRuntimeBenchmarkComparison(
            identity,
            baseline.MeanNanoseconds,
            integer.MeanNanoseconds,
            text.MeanNanoseconds,
            integerOverhead,
            Percent(integerOverhead, baseline.MeanNanoseconds),
            stringOverhead,
            Percent(stringOverhead, baseline.MeanNanoseconds),
            stringPenalty,
            Percent(stringPenalty, integer.MeanNanoseconds),
            integer.StandardDeviationNanoseconds,
            text.StandardDeviationNanoseconds,
            integer.ErrorNanoseconds,
            text.ErrorNanoseconds,
            integer.AllocatedBytesPerOperation,
            text.AllocatedBytesPerOperation);
    }

    private static string BuildExecutiveSummary(
        string processor,
        string runtime,
        Measurement baseline,
        CiRuntimeBenchmarkComparison direct,
        CiRuntimeBenchmarkComparison scheduled,
        string runner)
    {
        static string N(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
        return $"On {processor} running {runtime}, the application-logic baseline averaged {N(baseline.MeanNanoseconds)} ns. " +
            $"For a direct handle update, integer-backed execution averaged {N(direct.IntegerMeanNanoseconds)} ns and " +
            $"string-backed execution averaged {N(direct.StringMeanNanoseconds)} ns; the string path added " +
            $"{N(direct.StringPenaltyOverIntegerNanoseconds)} ns ({N(direct.StringPenaltyOverIntegerPercent)}%) per operation. " +
            $"For a scheduled group update, integer-backed execution averaged {N(scheduled.IntegerMeanNanoseconds)} ns and " +
            $"string-backed execution averaged {N(scheduled.StringMeanNanoseconds)} ns; the string path added " +
            $"{N(scheduled.StringPenaltyOverIntegerNanoseconds)} ns ({N(scheduled.StringPenaltyOverIntegerPercent)}%) per operation. " +
            $"These measurements came from a {runner}; they are comparative evidence for this workload and machine, not a universal performance guarantee.";
    }

    private static CiFsmRuntimeBenchmarkReportResult Incomplete(string error) =>
        new("incomplete", Array.AsReadOnly(new[] { error }), null, null, null, null, null, null, null, null, null, null, null, null);

    private static JsonElement ReadObject(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? ReadString(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? ReadDouble(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    private static double? ReadOptionalDouble(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : double.NaN;
    }

    private static long? ReadInt64(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static int? ReadInt32(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static bool IsInvalidNonNegative(double? value) =>
        value is { } number && (!double.IsFinite(number) || number < 0);

    private static bool IsSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static double Percent(double delta, double baseline) => baseline > 0 ? delta / baseline * 100.0 : double.NaN;
}
