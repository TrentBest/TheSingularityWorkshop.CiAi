using System.Text.Json;
using System.Text.Json.Serialization;
using TheSingularityWorkshop.CiAi;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/CiAi.BenchmarkPolicy.Cli -- <evidence.json> [result.json]");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = args.Length == 2 ? Path.GetFullPath(args[1]) : null;
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Evidence file does not exist: {inputPath}");
    return 2;
}

try
{
    await using var stream = File.OpenRead(inputPath);
    var evidence = await JsonSerializer.DeserializeAsync<BenchmarkEvidenceInput>(stream,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (evidence is null)
        throw new InvalidDataException("Evidence JSON contained null.");

    var policyInput = new CiBenchmarkEvidence(
        evidence.SourceRepository ?? "",
        evidence.SourceSha ?? "",
        evidence.HarnessRepository ?? "",
        evidence.HarnessSha ?? "",
        evidence.RunId,
        evidence.RunAttempt,
        evidence.Lane ?? "",
        evidence.SourceTestsPassed,
        evidence.CorrectnessPassed,
        evidence.ReportsPresent,
        evidence.ReportCount,
        evidence.ProvenanceComplete,
        evidence.CandidateMeasurementsNanoseconds ?? [],
        evidence.BaselineMeasurementsNanoseconds ?? [],
        evidence.WorkloadIdentity ?? "");

    var result = CiBenchmarkEvidencePolicyEvaluator.Evaluate(policyInput);
    var output = new BenchmarkPolicyOutput(
        "ciai.benchmark-policy-result.v1",
        policyInput.SourceRepository,
        policyInput.SourceSha,
        policyInput.HarnessRepository,
        policyInput.HarnessSha,
        policyInput.RunId,
        policyInput.RunAttempt,
        policyInput.Lane,
        result.Verdict,
        result.Errors,
        result.CandidateMedianNanoseconds,
        result.BaselineMedianNanoseconds,
        result.MedianChangePercent,
        result.CandidateSampleCount,
        result.BaselineSampleCount,
        result.IsPassing);

    var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (outputPath is not null)
        await File.WriteAllTextAsync(outputPath, json + Environment.NewLine);
    return 0;
}
catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine($"CiAi benchmark policy evaluation failed to read evidence: {exception.Message}");
    return 2;
}

internal sealed record BenchmarkEvidenceInput(
    string? SourceRepository,
    string? SourceSha,
    string? HarnessRepository,
    string? HarnessSha,
    long RunId,
    int RunAttempt,
    string? Lane,
    bool SourceTestsPassed,
    bool CorrectnessPassed,
    bool ReportsPresent,
    int ReportCount,
    bool ProvenanceComplete,
    IReadOnlyList<double>? CandidateMeasurementsNanoseconds,
    IReadOnlyList<double>? BaselineMeasurementsNanoseconds,
    string? WorkloadIdentity);

internal sealed record BenchmarkPolicyOutput(
    string Schema,
    string SourceRepository,
    string SourceSha,
    string HarnessRepository,
    string HarnessSha,
    long RunId,
    int RunAttempt,
    string Lane,
    string Verdict,
    IReadOnlyList<string> Errors,
    double? CandidateMedianNanoseconds,
    double? BaselineMedianNanoseconds,
    double? MedianChangePercent,
    int CandidateSampleCount,
    int BaselineSampleCount,
    bool IsPassing);
