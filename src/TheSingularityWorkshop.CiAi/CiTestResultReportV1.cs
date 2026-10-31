using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Normalized, bounded test results from one digest-verified artifact on one exact workflow attempt.</summary>
public sealed record CiTestResultReportV1
{
    public const int CurrentContractVersion = 1;
    public const string CurrentSchema = "ciai.test-results.v1";
    public const int MaximumArtifactBytes = 2 * 1024 * 1024;
    public const int MaximumResults = 10_000;
    public const int MaximumFieldLength = 4_096;

    private CiTestResultReportV1(string repository, long runId, int runAttempt, long jobId,
        string jobName, string artifactId, string artifactSha256, IReadOnlyList<CiTestResultRecordV1> results)
    {
        Repository = repository;
        RunId = runId;
        RunAttempt = runAttempt;
        JobId = jobId;
        JobName = jobName;
        ArtifactId = artifactId;
        ArtifactSha256 = artifactSha256;
        Results = results;
        Passed = results.Count(x => x.Outcome == CiTestOutcome.Passed);
        Failed = results.Count(x => x.Outcome == CiTestOutcome.Failed);
        Skipped = results.Count(x => x.Outcome == CiTestOutcome.Skipped);
        Other = results.Count - Passed - Failed - Skipped;
        ContentSha256 = Digest(string.Join("\n", results.Select(x => x.RecordSha256)));
    }

    public int ContractVersion => CurrentContractVersion;
    public string Schema => CurrentSchema;
    public string Repository { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public long JobId { get; }
    public string JobName { get; }
    public string ArtifactId { get; }
    public string ArtifactSha256 { get; }
    public string ContentSha256 { get; }
    public IReadOnlyList<CiTestResultRecordV1> Results { get; }
    public int Total => Results.Count;
    public int Passed { get; }
    public int Failed { get; }
    public int Skipped { get; }
    public int Other { get; }

    /// <summary>
    /// The trusted host supplies the provenance parsed from the separately verified provenance
    /// artifact, plus provider-observed job/artifact identity. TRX claims are never provenance.
    /// </summary>
    public static CiTestResultReportV1 ParseTrx(byte[] artifactBytes, string expectedArtifactSha256,
        CiWorkflowProvenanceRecord provenance, long jobId, string jobName, string artifactId)
    {
        ArgumentNullException.ThrowIfNull(artifactBytes);
        ArgumentNullException.ThrowIfNull(provenance);
        if (artifactBytes.Length == 0 || artifactBytes.Length > MaximumArtifactBytes)
            throw new InvalidDataException("TRX artifact is empty or exceeds the 2 MiB limit.");
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        ValidateText(jobName, nameof(jobName), 256);
        ValidateText(artifactId, nameof(artifactId), 256);
        if (expectedArtifactSha256 is null || expectedArtifactSha256.Length != 64 ||
            !expectedArtifactSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected artifact digest must be SHA-256 hexadecimal.", nameof(expectedArtifactSha256));

        var actualDigest = Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualDigest),
            Convert.FromHexString(expectedArtifactSha256)))
            throw new InvalidDataException("TRX bytes do not match the trusted artifact digest.");

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumArtifactBytes, MaxCharactersFromEntities = 0,
            IgnoreComments = true, IgnoreProcessingInstructions = true };
        XDocument document;
        using (var stream = new MemoryStream(artifactBytes, writable: false))
        using (var reader = XmlReader.Create(stream, settings))
            document = XDocument.Load(reader, LoadOptions.None);

        var root = document.Root;
        if (root is null || root.Name.LocalName != "TestRun")
            throw new InvalidDataException("Artifact is not a TRX TestRun document.");

        var resultElements = root.Descendants().Where(x => x.Name.LocalName == "UnitTestResult").ToArray();
        if (resultElements.Length == 0 || resultElements.Length > MaximumResults)
            throw new InvalidDataException("TRX result count is empty or exceeds the 10,000 result limit.");

        var records = new List<CiTestResultRecordV1>(resultElements.Length);
        foreach (var element in resultElements)
        {
            var testName = RequiredAttribute(element, "testName");
            var testId = OptionalAttribute(element, "testId") ?? testName;
            var rawOutcome = RequiredAttribute(element, "outcome");
            var outcome = rawOutcome.ToUpperInvariant() switch
            {
                "PASSED" => CiTestOutcome.Passed,
                "FAILED" => CiTestOutcome.Failed,
                "NOTEXECUTED" or "NOTRUN" or "DISABLED" => CiTestOutcome.Skipped,
                _ => CiTestOutcome.Other
            };
            var message = BoundDescendant(element, "Message");
            var stack = BoundDescendant(element, "StackTrace");
            var error = BoundDescendant(element, "ErrorInfo");
            var expected = BoundDescendant(error, "Message");
            var actual = BoundDescendant(error, "StackTrace");
            records.Add(CiTestResultRecordV1.Create(testId, testName, outcome, rawOutcome,
                message ?? expected, actual, stack));
        }

        return new CiTestResultReportV1(provenance.Repository, provenance.RunId, provenance.RunAttempt,
            jobId, jobName, artifactId, actualDigest, Array.AsReadOnly(records.ToArray()));
    }

    private static string? BoundDescendant(XElement? parent, string name)
    {
        if (parent is null) return null;
        var value = parent.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value;
        if (value is null) return null;
        value = value.Trim();
        return value.Length <= MaximumFieldLength ? value : value[..MaximumFieldLength];
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"TRX result is missing '{name}'.");
        return value;
    }

    private static string? OptionalAttribute(XElement element, string name)
    {
        var value = (string?)element.Attribute(name);
        if (value is null) return null;
        value = value.Trim();
        if (value.Length > MaximumFieldLength) throw new InvalidDataException($"TRX attribute '{name}' exceeds its limit.");
        return value;
    }

    private static void ValidateText(string value, string parameter, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException("Value is missing or exceeds its allowed bounds.", parameter);
    }

    internal static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public enum CiTestOutcome { Passed, Failed, Skipped, Other }

/// <summary>One normalized test observation; message and stack data are untrusted diagnostic text.</summary>
public sealed record CiTestResultRecordV1
{
    private CiTestResultRecordV1(string testId, string testName, CiTestOutcome outcome, string providerOutcome,
        string? message, string? actual, string? stackTrace, string recordSha256)
    {
        TestId = testId; TestName = testName; Outcome = outcome; ProviderOutcome = providerOutcome;
        Message = message; Actual = actual; StackTrace = stackTrace; RecordSha256 = recordSha256;
    }

    public string TestId { get; }
    public string TestName { get; }
    public CiTestOutcome Outcome { get; }
    public string ProviderOutcome { get; }
    public string? Message { get; }
    public string? Actual { get; }
    public string? StackTrace { get; }
    public string RecordSha256 { get; }

    internal static CiTestResultRecordV1 Create(string id, string name, CiTestOutcome outcome,
        string providerOutcome, string? message, string? actual, string? stack)
    {
        var canonical = string.Join("|", new[] { id, name, outcome.ToString(), providerOutcome, message, actual, stack }
            .Select(value => value is null ? "-1:" : Encoding.UTF8.GetByteCount(value) + ":" + value));
        return new CiTestResultRecordV1(id, name, outcome, providerOutcome, message, actual, stack,
            CiTestResultReportV1.Digest(canonical));
    }
}
