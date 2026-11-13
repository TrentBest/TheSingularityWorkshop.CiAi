using System.Xml;
using System.Xml.Linq;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Normalized outcome for one TRX test result. Diagnostic content remains untrusted input.
/// </summary>
public enum CiTestResultOutcome
{
    Passed,
    Failed,
    Skipped,
    Unknown
}

/// <summary>
/// One bounded test diagnostic parsed from a TRX document.
/// </summary>
public sealed record CiTestResult(
    string TestId,
    string ExecutionId,
    string TestName,
    CiTestResultOutcome Outcome,
    string ProviderOutcome,
    string? ErrorMessage,
    string? StackTrace,
    bool DiagnosticTruncated);

/// <summary>
/// A bounded, normalized view of one TRX document. This record is not provenance evidence;
/// callers must bind it to a verified repository, checkout SHA, workflow run attempt, job,
/// and artifact manifest before using it in a CI verdict or LLM handoff.
/// </summary>
public sealed record CiTestResultDocument(
    string Schema,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    int Unknown,
    IReadOnlyList<CiTestResult> Results,
    string DiagnosticTrust);

/// <summary>
/// Parses .NET TRX test results using bounded, DTD-disabled XML processing.
/// All provider-supplied strings are diagnostic data and must never be treated as instructions.
/// </summary>
public static class TrxTestResultParser
{
    public const string Schema = "ciai.trx-test-results.v1";
    public const string DiagnosticTrustLabel = "untrusted-diagnostic-data";

    /// <summary>
    /// Parse a TRX payload with explicit limits. The input bytes must already have been
    /// retrieved and verified against a provenance-bound artifact manifest.
    /// </summary>
    public static CiTestResultDocument Parse(
        ReadOnlyMemory<byte> payload,
        int maximumPayloadBytes = 4 * 1024 * 1024,
        int maximumResults = 10_000,
        int maximumDiagnosticCharacters = 4_096)
    {
        if (maximumPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        if (maximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        if (maximumDiagnosticCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDiagnosticCharacters));
        if (payload.IsEmpty) throw new InvalidDataException("trx.payload-empty");
        if (payload.Length > maximumPayloadBytes) throw new InvalidDataException("trx.payload-exceeds-limit");

        try
        {
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maximumPayloadBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });

            var document = XDocument.Load(reader, LoadOptions.None);
            var testRun = document.Root;
            if (testRun is null || testRun.Name.LocalName != "TestRun")
                throw new InvalidDataException("trx.root-invalid");

            var resultsElement = testRun.Elements().FirstOrDefault(element => element.Name.LocalName == "Results");
            if (resultsElement is null)
                throw new InvalidDataException("trx.results-missing");

            var resultElements = resultsElement.Elements()
                .Where(element => element.Name.LocalName == "UnitTestResult")
                .Take(maximumResults + 1)
                .ToArray();
            if (resultElements.Length > maximumResults)
                throw new InvalidDataException("trx.result-count-exceeds-limit");

            var results = new List<CiTestResult>(resultElements.Length);
            foreach (var element in resultElements)
            {
                var providerOutcome = Attribute(element, "outcome");
                var (outcome, normalized) = NormalizeOutcome(providerOutcome);
                var errorInfo = element.Descendants().FirstOrDefault(child => child.Name.LocalName == "ErrorInfo");
                var message = ChildValue(errorInfo, "Message");
                var stackTrace = ChildValue(errorInfo, "StackTrace");
                var boundedMessage = Bound(message, maximumDiagnosticCharacters, out var messageTruncated);
                var boundedStack = Bound(stackTrace, maximumDiagnosticCharacters, out var stackTruncated);

                results.Add(new CiTestResult(
                    Attribute(element, "testId"),
                    Attribute(element, "executionId"),
                    Bound(Attribute(element, "testName"), maximumDiagnosticCharacters, out _) ?? string.Empty,
                    outcome,
                    Bound(providerOutcome, maximumDiagnosticCharacters, out _) ?? string.Empty,
                    boundedMessage,
                    boundedStack,
                    messageTruncated || stackTruncated));
            }

            var passed = results.Count(result => result.Outcome == CiTestResultOutcome.Passed);
            var failed = results.Count(result => result.Outcome == CiTestResultOutcome.Failed);
            var skipped = results.Count(result => result.Outcome == CiTestResultOutcome.Skipped);
            var unknown = results.Count(result => result.Outcome == CiTestResultOutcome.Unknown);

            return new CiTestResultDocument(
                Schema,
                results.Count,
                passed,
                failed,
                skipped,
                unknown,
                results.AsReadOnly(),
                DiagnosticTrustLabel);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("trx.xml-invalid", exception);
        }
    }

    private static (CiTestResultOutcome Outcome, bool Recognized) NormalizeOutcome(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "passed" => (CiTestResultOutcome.Passed, true),
            "failed" or "timeout" or "error" => (CiTestResultOutcome.Failed, true),
            "notexecuted" or "aborted" or "inconclusive" or "notrunnable" or "disconnected" =>
                (CiTestResultOutcome.Skipped, true),
            _ => (CiTestResultOutcome.Unknown, false)
        };
    }

    private static string Attribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? string.Empty;

    private static string? ChildValue(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

    private static string? Bound(string? value, int maximumCharacters, out bool truncated)
    {
        truncated = value is not null && value.Length > maximumCharacters;
        if (value is null) return null;
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters];
    }
}
