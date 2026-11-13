using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Outcome from CiAi's configured required-check policy, not a caller inference from raw job status.</summary>
public enum CiRequiredCheckStatus
{
    Passed,
    Failed,
    Incomplete,
    NotRequired
}

public enum CiTestResultVerdict
{
    CompletePass,
    ActionableFailure,
    IncompleteOrUntrusted,
    NonActionableFailure
}

/// <summary>
/// Trusted-host metadata for one GitHub Actions artifact containing a single TRX payload.
/// The host must verify the provider's SHA-256 digest over the downloaded artifact ZIP and
/// safely extract the named TRX before constructing this source descriptor.
/// </summary>
public sealed record CiTestResultArtifactSource
{
    private CiTestResultArtifactSource(
        string repository,
        long runId,
        int runAttempt,
        long jobId,
        string jobName,
        string jobKey,
        long artifactId,
        string artifactName,
        string providerArchiveDigest,
        long archiveSizeBytes,
        string payloadSha256,
        long payloadSizeBytes,
        string testFramework,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Artifact repository must use canonical owner/repository identity.", nameof(repository));
        if (runId <= 0 || runAttempt <= 0 || jobId <= 0 || artifactId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId), "Run, attempt, job, and artifact identifiers must be positive.");
        RequireText(jobName, nameof(jobName), 200);
        RequireIdentifier(jobKey, nameof(jobKey), 100);
        RequireText(artifactName, nameof(artifactName), 200);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerArchiveDigest);
        // Artifact names are provider-controlled labels, not trusted job identities.
        // The trusted host binds the provider artifact ID/name to the numeric job ID and
        // configured workflow job key before constructing this descriptor.
        if (!providerArchiveDigest.StartsWith("sha256:", StringComparison.Ordinal) ||
            providerArchiveDigest.Length != 71 ||
            !providerArchiveDigest.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new ArgumentException("Provider archive digest must use sha256:<64 hexadecimal characters>.", nameof(providerArchiveDigest));
        ValidateDigest(payloadSha256, nameof(payloadSha256));
        if (archiveSizeBytes is <= 0 or > CiTestResultArtifactArchive.DefaultMaximumArchiveBytes)
            throw new ArgumentOutOfRangeException(nameof(archiveSizeBytes));
        if (payloadSizeBytes is <= 0 or > CiTestResultReport.DefaultMaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payloadSizeBytes));
        RequireText(testFramework, nameof(testFramework), 100);
        if (createdAt == default || expiresAt <= createdAt)
            throw new ArgumentException("Artifact creation and expiry timestamps are invalid.");

        Repository = repository;
        RunId = runId;
        RunAttempt = runAttempt;
        JobId = jobId;
        JobName = jobName;
        JobKey = jobKey;
        ArtifactId = artifactId;
        ArtifactName = artifactName;
        ProviderArchiveDigest = providerArchiveDigest.ToLowerInvariant();
        ArchiveSizeBytes = archiveSizeBytes;
        PayloadSha256 = payloadSha256.ToLowerInvariant();
        PayloadSizeBytes = payloadSizeBytes;
        TestFramework = testFramework;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Creates a source descriptor only from a host-observed provider artifact/job binding and
    /// an archive whose provider digest, bounded ZIP entries, TRX bytes, and embedded provenance
    /// have already been verified. The host must construct the binding from independent provider
    /// API observations and its explicit artifact-to-job association; report JSON is never a source.
    /// </summary>
    public static CiTestResultArtifactSource FromVerifiedProviderObservation(
        CiWorkflowEvidencePacket evidence,
        CiRequiredJobPolicy requiredJobPolicy,
        CiTestResultProviderArtifactBinding providerBinding,
        CiTestResultArtifactArchive archive,
        string testFramework)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(requiredJobPolicy);
        ArgumentNullException.ThrowIfNull(providerBinding);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(testFramework);

        if (!evidence.WorkflowDefinitionTrusted || !evidence.CheckoutVerified || !evidence.EvidenceComplete)
            throw new InvalidDataException("Provider artifact binding requires trusted, complete exact-run evidence.");
        if (providerBinding.IsExpired)
            throw new InvalidDataException("Provider reports that the source artifact is expired.");
        if (!string.Equals(providerBinding.Repository, evidence.Provenance.Repository, StringComparison.OrdinalIgnoreCase) ||
            providerBinding.RunId != evidence.Provenance.RunId ||
            providerBinding.RunAttempt != evidence.Provenance.RunAttempt)
            throw new InvalidDataException("Provider artifact binding does not identify the exact trusted run and attempt.");

        var matchingJobs = evidence.ObservedRun.Jobs.Where(job =>
            job.JobId == providerBinding.JobId &&
            job.RunId == providerBinding.RunId &&
            job.RunAttempt == providerBinding.RunAttempt &&
            string.Equals(job.Name, providerBinding.JobName, StringComparison.Ordinal)).ToArray();
        if (matchingJobs.Length != 1)
            throw new InvalidDataException("Provider artifact binding does not identify exactly one independently observed job.");

        var policyDefinitions = requiredJobPolicy.RequiredJobs.Where(definition =>
            string.Equals(definition.WorkflowPath, evidence.Provenance.WorkflowPath, StringComparison.Ordinal) &&
            string.Equals(definition.JobKey, providerBinding.JobKey, StringComparison.Ordinal) &&
            string.Equals(definition.JobName, providerBinding.JobName, StringComparison.Ordinal)).ToArray();
        if (policyDefinitions.Length != 1)
            throw new InvalidDataException("Provider artifact binding does not match exactly one configured required workflow job.");
        var requiredStatus = requiredJobPolicy.Evaluate(evidence, providerBinding.JobId, providerBinding.JobKey);
        if (requiredStatus is not (CiRequiredCheckStatus.Passed or CiRequiredCheckStatus.Failed))
            throw new InvalidDataException("Provider artifact binding does not identify a complete required check.");

        if (!string.Equals(archive.ProviderArchiveDigest, providerBinding.ProviderArchiveDigest, StringComparison.OrdinalIgnoreCase) ||
            archive.ArchiveSizeBytes != providerBinding.ArchiveSizeBytes)
            throw new InvalidDataException("Verified archive bytes do not match independently observed provider artifact metadata.");

        var embeddedProvenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            archive.ProvenanceBytes, providerBinding.Repository, providerBinding.RunId,
            providerBinding.RunAttempt, archive.ProvenanceSha256);
        if (!SameProvenance(embeddedProvenance, evidence.Provenance))
            throw new InvalidDataException("Verified archive provenance does not match the exact trusted workflow evidence.");

        return new CiTestResultArtifactSource(
            providerBinding.Repository, providerBinding.RunId, providerBinding.RunAttempt,
            providerBinding.JobId, providerBinding.JobName, providerBinding.JobKey,
            providerBinding.ArtifactId, providerBinding.ArtifactName,
            archive.ProviderArchiveDigest, archive.ArchiveSizeBytes,
            archive.TrxSha256, archive.TrxBytes.Length, testFramework,
            providerBinding.CreatedAt, providerBinding.ExpiresAt);
    }

    private static bool SameProvenance(
        CiWorkflowProvenanceRecord left,
        CiWorkflowProvenanceRecord right) =>
        string.Equals(left.Repository, right.Repository, StringComparison.OrdinalIgnoreCase) &&
        left.RunId == right.RunId &&
        left.RunAttempt == right.RunAttempt &&
        string.Equals(left.WorkflowPath, right.WorkflowPath, StringComparison.Ordinal) &&
        string.Equals(left.WorkflowRef, right.WorkflowRef, StringComparison.Ordinal) &&
        string.Equals(left.WorkflowSha, right.WorkflowSha, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.EventName, right.EventName, StringComparison.Ordinal) &&
        string.Equals(left.EventRef, right.EventRef, StringComparison.Ordinal) &&
        string.Equals(left.EventSha, right.EventSha, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.CheckoutRef, right.CheckoutRef, StringComparison.Ordinal) &&
        string.Equals(left.CheckoutSha, right.CheckoutSha, StringComparison.OrdinalIgnoreCase) &&
        SamePullRequest(left.PullRequest, right.PullRequest);

    private static bool SamePullRequest(
        CiWorkflowPullRequestProvenance? left,
        CiWorkflowPullRequestProvenance? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Number == right.Number &&
            string.Equals(left.HeadRef, right.HeadRef, StringComparison.Ordinal) &&
            string.Equals(left.HeadSha, right.HeadSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.BaseRef, right.BaseRef, StringComparison.Ordinal) &&
            string.Equals(left.BaseSha, right.BaseSha, StringComparison.OrdinalIgnoreCase);
    }

    public string Repository { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public long JobId { get; }
    public string JobName { get; }
    public string JobKey { get; }
    public long ArtifactId { get; }
    public string ArtifactName { get; }
    public string ProviderArchiveDigest { get; }
    public long ArchiveSizeBytes { get; }
    public string PayloadSha256 { get; }
    public long PayloadSizeBytes { get; }
    public string TestFramework { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    private static void RequireText(string value, string name, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maximumLength} characters without control characters.", name);
    }

    private static void RequireIdentifier(string value, string name, int maximumLength)
    {
        RequireText(value, name, maximumLength);
        if (!value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException("Identifier contains unsupported characters.", name);
    }

    private static void ValidateDigest(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A full SHA-256 hexadecimal digest is required.", name);
    }
}

/// <summary>
/// Normalized, provenance-bound test diagnostics from one exact workflow run attempt.
/// Parsing is local and bounded; it does not download artifacts or verify the ZIP archive.
/// </summary>
public sealed record CiTestResultReport
{
    public const int CurrentContractVersion = 1;
    public const string CurrentSchema = "ciai.test-results.v1";
    public const long DefaultMaximumPayloadBytes = 16 * 1024 * 1024;
    public const int MaximumTestResults = 10_000;
    private const int MaximumMessageLength = 4_000;
    private const int MaximumStackTraceLength = 8_000;

    private static readonly Regex SensitiveValuePattern = new(
        @"(?i)(gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|Bearer\s+[A-Za-z0-9._~+/=-]{16,}|(?:password|passwd|token|api[_-]?key|secret)\s*[:=]\s*[^\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private CiTestResultReport(
        CiWorkflowEvidencePacket evidence,
        CiWorkflowJobObservation job,
        CiRequiredCheckStatus requiredCheckStatus,
        CiTestResultVerdict verdict,
        IReadOnlyList<string> reasonCodes,
        string? testFramework,
        long? sourceArtifactId,
        string? sourceArtifactName,
        string? sourceJobKey,
        string? providerArchiveDigest,
        long? archiveSizeBytes,
        DateTimeOffset? artifactCreatedAt,
        DateTimeOffset? artifactExpiresAt,
        string? payloadSha256,
        long payloadSizeBytes,
        int total,
        int passed,
        int failed,
        int skipped,
        int other,
        IReadOnlyList<CiTestResultRecord> results,
        DateTimeOffset observedAt)
    {
        ContractVersion = CurrentContractVersion;
        Schema = CurrentSchema;
        Repository = evidence.Provenance.Repository;
        CandidateRef = evidence.CandidateRef;
        CandidateSha = evidence.CandidateSha;
        WorkflowPath = evidence.Provenance.WorkflowPath;
        WorkflowDefinitionRef = evidence.Provenance.WorkflowRef;
        PullRequestNumber = evidence.Provenance.PullRequest?.Number;
        PullRequestHeadRef = evidence.Provenance.PullRequest?.HeadRef;
        PullRequestHeadSha = evidence.Provenance.PullRequest?.HeadSha;
        PullRequestBaseRef = evidence.Provenance.PullRequest?.BaseRef;
        PullRequestBaseSha = evidence.Provenance.PullRequest?.BaseSha;
        RunEvidenceUrl = evidence.ObservedRun.RunEvidenceUrl;
        JobEvidenceUrl = job.EvidenceUrl;
        WorkflowDefinitionSha = evidence.Provenance.WorkflowSha;
        EventName = evidence.Provenance.EventName;
        EventRef = evidence.Provenance.EventRef;
        EventSha = evidence.Provenance.EventSha;
        RunId = evidence.Provenance.RunId;
        RunAttempt = evidence.Provenance.RunAttempt;
        JobId = job.JobId;
        JobName = job.Name;
        JobStatus = job.Status;
        JobConclusion = job.Conclusion;
        RequiredCheckStatus = requiredCheckStatus;
        Verdict = verdict;
        ReasonCodes = Array.AsReadOnly(reasonCodes.Distinct(StringComparer.Ordinal).ToArray());
        TestFramework = testFramework;
        Format = "trx";
        SourceArtifactId = sourceArtifactId;
        SourceArtifactName = sourceArtifactName;
        SourceJobKey = sourceJobKey;
        ProviderArchiveDigest = providerArchiveDigest;
        ArchiveSizeBytes = archiveSizeBytes;
        ArtifactCreatedAt = artifactCreatedAt;
        ArtifactExpiresAt = artifactExpiresAt;
        PayloadSha256 = payloadSha256;
        PayloadSizeBytes = payloadSizeBytes;
        Total = total;
        Passed = passed;
        Failed = failed;
        Skipped = skipped;
        Other = other;
        Results = Array.AsReadOnly(results.ToArray());
        ObservedAt = observedAt;
    }

    public int ContractVersion { get; }
    public string Schema { get; }
    public string Repository { get; }
    public string CandidateRef { get; }
    public string CandidateSha { get; }
    public string WorkflowPath { get; }
    public string WorkflowDefinitionRef { get; }
    public int? PullRequestNumber { get; }
    public string? PullRequestHeadRef { get; }
    public string? PullRequestHeadSha { get; }
    public string? PullRequestBaseRef { get; }
    public string? PullRequestBaseSha { get; }
    public Uri RunEvidenceUrl { get; }
    public Uri JobEvidenceUrl { get; }
    public string WorkflowDefinitionSha { get; }
    public string EventName { get; }
    public string EventRef { get; }
    public string EventSha { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public long JobId { get; }
    public string JobName { get; }
    public string JobStatus { get; }
    public string? JobConclusion { get; }
    public CiRequiredCheckStatus RequiredCheckStatus { get; }
    public CiTestResultVerdict Verdict { get; }
    public IReadOnlyList<string> ReasonCodes { get; }
    public string? TestFramework { get; }
    public string Format { get; }
    public long? SourceArtifactId { get; }
    public string? SourceArtifactName { get; }
    public string? SourceJobKey { get; }
    public string? ProviderArchiveDigest { get; }
    public long? ArchiveSizeBytes { get; }
    public DateTimeOffset? ArtifactCreatedAt { get; }
    public DateTimeOffset? ArtifactExpiresAt { get; }
    public string? PayloadSha256 { get; }
    public long PayloadSizeBytes { get; }
    public int Total { get; }
    public int Passed { get; }
    public int Failed { get; }
    public int Skipped { get; }
    public int Other { get; }
    public IReadOnlyList<CiTestResultRecord> Results { get; }
    public DateTimeOffset ObservedAt { get; }

    public static CiTestResultReport ParseAndValidate(
        ReadOnlyMemory<byte> trxBytes,
        CiWorkflowEvidencePacket evidence,
        CiTestResultArtifactSource source,
        CiRequiredJobPolicy requiredJobPolicy,
        DateTimeOffset observedAt,
        long maximumPayloadBytes = DefaultMaximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requiredJobPolicy);
        if (maximumPayloadBytes is <= 0 or > DefaultMaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        if (trxBytes.IsEmpty || trxBytes.Length > maximumPayloadBytes)
            throw new InvalidDataException("TRX payload is empty or exceeds the configured byte limit.");
        if (observedAt == default || observedAt < source.CreatedAt || observedAt > source.ExpiresAt ||
            observedAt < evidence.ObservedRun.ObservedAt)
            throw new InvalidDataException("TRX artifact is not valid at the supplied observation time.");

        ValidateEvidenceBinding(evidence, source);
        if (source.PayloadSizeBytes != trxBytes.Length)
            throw new InvalidDataException("Extracted TRX payload size does not match the trusted artifact manifest.");
        var payloadDigest = Convert.ToHexString(SHA256.HashData(trxBytes.Span)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(payloadDigest), Convert.FromHexString(source.PayloadSha256)))
            throw new InvalidDataException("Extracted TRX payload digest does not match the trusted artifact manifest.");

        var parsed = ParseTrx(trxBytes);
        var job = evidence.ObservedRun.Jobs.Single(item => item.JobId == source.JobId);
        var requiredCheckStatus = requiredJobPolicy.Evaluate(evidence, source.JobId, source.JobKey);
        var reasons = new List<string>();
        var verdict = EvaluateVerdict(job, requiredCheckStatus, parsed, reasons);
        return new CiTestResultReport(
            evidence, job, requiredCheckStatus, verdict, reasons, source.TestFramework,
            source.ArtifactId, source.ArtifactName, source.JobKey,
            source.ProviderArchiveDigest, source.ArchiveSizeBytes, source.CreatedAt, source.ExpiresAt,
            payloadDigest, trxBytes.Length,
            parsed.Total, parsed.Passed, parsed.Failed, parsed.Skipped, parsed.Other, parsed.Results, observedAt);
    }

    /// <summary>
    /// Verifies the complete provider archive, parses its embedded provenance, binds that provenance
    /// to the already trusted workflow evidence, and only then produces normalized test diagnostics.
    /// This is the preferred end-to-end entry point for provider-downloaded test-result artifacts.
    /// </summary>
    public static CiTestResultReport ParseAndValidate(
        CiTestResultArtifactArchive archive,
        CiWorkflowEvidencePacket evidence,
        CiTestResultArtifactSource source,
        CiRequiredJobPolicy requiredJobPolicy,
        DateTimeOffset observedAt,
        long maximumPayloadBytes = DefaultMaximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requiredJobPolicy);

        if (!string.Equals(archive.ProviderArchiveDigest, source.ProviderArchiveDigest, StringComparison.OrdinalIgnoreCase) ||
            archive.ArchiveSizeBytes != source.ArchiveSizeBytes)
            throw new InvalidDataException("Verified ZIP archive identity does not match the trusted provider artifact manifest.");
        var actualTrxDigest = SHA256.HashData(archive.TrxBytes.Span);
        var actualProvenanceDigest = SHA256.HashData(archive.ProvenanceBytes.Span);
        if (!CryptographicOperations.FixedTimeEquals(actualTrxDigest, Convert.FromHexString(archive.TrxSha256)) ||
            !CryptographicOperations.FixedTimeEquals(actualProvenanceDigest, Convert.FromHexString(archive.ProvenanceSha256)))
            throw new InvalidDataException("Verified archive payload bytes changed after extraction.");

        if (!string.Equals(archive.TrxSha256, source.PayloadSha256, StringComparison.OrdinalIgnoreCase) ||
            archive.TrxBytes.Length != source.PayloadSizeBytes)
            throw new InvalidDataException("Verified TRX payload identity does not match the trusted artifact manifest.");

        var embeddedProvenance = CiWorkflowProvenanceRecord.ParseAndValidate(
            archive.ProvenanceBytes, source.Repository, source.RunId, source.RunAttempt, archive.ProvenanceSha256);
        if (!HasSameProvenance(embeddedProvenance, evidence.Provenance))
            throw new InvalidDataException("Embedded artifact provenance does not match the trusted workflow evidence packet.");

        return ParseAndValidate(
            archive.TrxBytes, evidence, source, requiredJobPolicy, observedAt, maximumPayloadBytes);
    }

    private static bool HasSameProvenance(
        CiWorkflowProvenanceRecord left,
        CiWorkflowProvenanceRecord right)
    {
        return string.Equals(left.Repository, right.Repository, StringComparison.OrdinalIgnoreCase) &&
            left.RunId == right.RunId &&
            left.RunAttempt == right.RunAttempt &&
            string.Equals(left.WorkflowPath, right.WorkflowPath, StringComparison.Ordinal) &&
            string.Equals(left.WorkflowRef, right.WorkflowRef, StringComparison.Ordinal) &&
            string.Equals(left.WorkflowSha, right.WorkflowSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.EventName, right.EventName, StringComparison.Ordinal) &&
            string.Equals(left.EventRef, right.EventRef, StringComparison.Ordinal) &&
            string.Equals(left.EventSha, right.EventSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.CheckoutRef, right.CheckoutRef, StringComparison.Ordinal) &&
            string.Equals(left.CheckoutSha, right.CheckoutSha, StringComparison.OrdinalIgnoreCase) &&
            SamePullRequest(left.PullRequest, right.PullRequest);
    }

    private static bool SamePullRequest(
        CiWorkflowPullRequestProvenance? left,
        CiWorkflowPullRequestProvenance? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Number == right.Number &&
            string.Equals(left.HeadRef, right.HeadRef, StringComparison.Ordinal) &&
            string.Equals(left.HeadSha, right.HeadSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.BaseRef, right.BaseRef, StringComparison.Ordinal) &&
            string.Equals(left.BaseSha, right.BaseSha, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates explicit incomplete evidence when the exact required test artifact is absent.</summary>
    public static CiTestResultReport MissingArtifact(
        CiWorkflowEvidencePacket evidence,
        long jobId,
        string jobKey,
        CiRequiredJobPolicy requiredJobPolicy,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(requiredJobPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobKey);
        if (jobId <= 0 || observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(jobId));
        if (observedAt < evidence.ObservedRun.ObservedAt)
            throw new InvalidDataException("Missing-artifact observation predates the workflow evidence.");
        if (!evidence.WorkflowDefinitionTrusted || !evidence.CheckoutVerified || !evidence.EvidenceComplete)
            throw new InvalidDataException("Missing-artifact reports still require trusted exact-run provenance.");
        var matches = evidence.ObservedRun.Jobs.Where(item => item.JobId == jobId).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Missing-artifact report must identify exactly one observed job.");
        var requiredCheckStatus = requiredJobPolicy.Evaluate(evidence, jobId, jobKey);
        var reasonCode = requiredCheckStatus == CiRequiredCheckStatus.NotRequired
            ? "test-results.check-not-required"
            : "test-results.artifact-missing";
        return new CiTestResultReport(
            evidence, matches[0], requiredCheckStatus, CiTestResultVerdict.IncompleteOrUntrusted,
            new[] { reasonCode }, null, null, null, jobKey, null,
            null, null, null, null, 0, 0, 0, 0, 0, 0, Array.Empty<CiTestResultRecord>(), observedAt);
    }

    /// <summary>
    /// Maximum canonical report size accepted by the cross-repository CODE RED handoff.
    /// This is deliberately independent of the larger raw TRX/archive parsing limits.
    /// </summary>
    public const int DefaultMaximumReportBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions CanonicalJsonOptions = CreateCanonicalJsonOptions();

    /// <summary>
    /// SHA-256 identity of the exact canonical UTF-8 report bytes, prefixed with "sha256:".
    /// This property is metadata for consumers and is not included in the report JSON itself.
    /// </summary>
    [JsonIgnore]
    public string ReportIdentity => "sha256:" + Convert.ToHexString(SHA256.HashData(ToCanonicalUtf8Bytes())).ToLowerInvariant();

    /// <summary>Lowercase SHA-256 of the exact canonical UTF-8 report bytes.</summary>
    [JsonIgnore]
    public string ReportSha256 => ReportIdentity[7..];

    /// <summary>
    /// Returns the canonical wire representation: UTF-8 without BOM, compact JSON, camelCase
    /// properties, string enum values, declaration-order properties, and no trailing newline.
    /// The returned bytes are the bytes to hash, persist, deliver, and pass unchanged downstream.
    /// </summary>
    public byte[] ToCanonicalUtf8Bytes()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, CanonicalJsonOptions);
        if (bytes.Length > DefaultMaximumReportBytes)
            throw new InvalidDataException($"Canonical test-result report exceeds the {DefaultMaximumReportBytes}-byte handoff limit.");
        return bytes;
    }

    public string ToCanonicalJson() => Encoding.UTF8.GetString(ToCanonicalUtf8Bytes());

    public string ToJson(bool indented = true)
    {
        var options = new JsonSerializerOptions { WriteIndented = indented };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(this, options);
    }

    private static JsonSerializerOptions CreateCanonicalJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void ValidateEvidenceBinding(
        CiWorkflowEvidencePacket evidence,
        CiTestResultArtifactSource source)
    {
        if (!evidence.WorkflowDefinitionTrusted || !evidence.CheckoutVerified || !evidence.EvidenceComplete)
            throw new InvalidDataException("Test diagnostics require trusted workflow definition, verified checkout, and complete run evidence.");
        if (!string.Equals(evidence.Provenance.Repository, source.Repository, StringComparison.OrdinalIgnoreCase) ||
            evidence.Provenance.RunId != source.RunId ||
            evidence.Provenance.RunAttempt != source.RunAttempt)
            throw new InvalidDataException("Test artifact belongs to a different repository, workflow run, or attempt.");
        var jobs = evidence.ObservedRun.Jobs.Where(item =>
            item.JobId == source.JobId &&
            string.Equals(item.Name, source.JobName, StringComparison.Ordinal)).ToArray();
        if (jobs.Length != 1 || jobs[0].RunId != source.RunId || jobs[0].RunAttempt != source.RunAttempt)
            throw new InvalidDataException("Test artifact does not bind to exactly one observed job in this run attempt.");
    }

    private static CiTestResultVerdict EvaluateVerdict(
        CiWorkflowJobObservation job,
        CiRequiredCheckStatus requiredCheckStatus,
        ParsedTrx parsed,
        List<string> reasons)
    {
        if (!Enum.IsDefined(requiredCheckStatus))
            throw new ArgumentOutOfRangeException(nameof(requiredCheckStatus));
        if (requiredCheckStatus == CiRequiredCheckStatus.NotRequired)
        {
            reasons.Add("test-results.check-not-required");
            return CiTestResultVerdict.IncompleteOrUntrusted;
        }
        if (parsed.Other > 0)
        {
            reasons.Add("test-results.unknown-outcome");
            return CiTestResultVerdict.IncompleteOrUntrusted;
        }
        if (requiredCheckStatus == CiRequiredCheckStatus.Incomplete ||
            job.Status != "completed" ||
            parsed.Total == 0)
        {
            reasons.Add(parsed.Total == 0 ? "test-results.empty" : "test-results.required-check-incomplete");
            return CiTestResultVerdict.IncompleteOrUntrusted;
        }

        var jobPassed = string.Equals(job.Conclusion, "success", StringComparison.Ordinal);
        var jobFailed = string.Equals(job.Conclusion, "failure", StringComparison.Ordinal);
        if ((requiredCheckStatus == CiRequiredCheckStatus.Passed) != jobPassed ||
            (requiredCheckStatus == CiRequiredCheckStatus.Failed) != jobFailed)
        {
            reasons.Add("test-results.required-check-status-mismatch");
            return CiTestResultVerdict.IncompleteOrUntrusted;
        }

        if (parsed.Failed == 0 && jobPassed)
        {
            reasons.Add("test-results.complete-pass");
            return CiTestResultVerdict.CompletePass;
        }
        if (parsed.Failed == 0)
        {
            reasons.Add("test-results.check-failed-without-test-failure");
            return CiTestResultVerdict.NonActionableFailure;
        }
        if (jobPassed)
        {
            reasons.Add("test-results.failed-tests-in-green-job");
            return CiTestResultVerdict.IncompleteOrUntrusted;
        }

        var failedResults = parsed.Results.Where(result => result.Outcome == CiTestOutcome.Failed).ToArray();
        if (jobFailed && failedResults.Length == parsed.Failed &&
            failedResults.All(result => !string.IsNullOrWhiteSpace(result.Message) ||
                !string.IsNullOrWhiteSpace(result.StackTrace)))
        {
            reasons.Add("test-results.actionable-failure");
            return CiTestResultVerdict.ActionableFailure;
        }

        reasons.Add("test-results.failure-diagnostics-incomplete");
        return CiTestResultVerdict.NonActionableFailure;
    }

    private static ParsedTrx ParseTrx(ReadOnlyMemory<byte> bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = DefaultMaximumPayloadBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "TestRun")
            throw new InvalidDataException("TRX document root must be TestRun.");
        var counters = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "Counters")
            ?? throw new InvalidDataException("TRX result summary is missing counters.");
        var total = ReadCounter(counters, "total");
        var passed = ReadCounter(counters, "passed");
        var failed = ReadCounter(counters, "failed") + ReadCounter(counters, "error") +
            ReadCounter(counters, "timeout") + ReadCounter(counters, "aborted");
        var skipped = ReadCounter(counters, "notExecuted") + ReadCounter(counters, "inconclusive") +
            ReadCounter(counters, "notRunnable") + ReadCounter(counters, "disconnected") +
            ReadCounter(counters, "warning") + ReadCounter(counters, "pending") +
            ReadCounter(counters, "inProgress") + ReadCounter(counters, "passedButRunAborted");

        var resultElements = root.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
        if (resultElements.Length > MaximumTestResults || resultElements.Length != total ||
            passed + failed + skipped > total)
            throw new InvalidDataException("TRX result records do not reconcile with the summary counters or exceed the record limit.");

        var results = new List<CiTestResultRecord>(resultElements.Length);
        foreach (var element in resultElements)
        {
            var rawTestId = RequiredAttribute(element, "testId", 200);
            var (normalizedTestId, testIdTruncated, testIdRedacted) = NormalizeDiagnostic(rawTestId, 200);
            var testId = normalizedTestId ?? rawTestId;
            var rawTestName = RequiredAttribute(element, "testName", 512);
            var (normalizedTestName, testNameTruncated, testNameRedacted) = NormalizeDiagnostic(rawTestName, 512);
            var testName = normalizedTestName ?? rawTestName;
            var outcomeText = RequiredAttribute(element, "outcome", 100);
            var outcome = MapOutcome(outcomeText);
            var errorInfo = element.Descendants().FirstOrDefault(child => child.Name.LocalName == "ErrorInfo");
            var rawMessage = errorInfo?.Elements().FirstOrDefault(child => child.Name.LocalName == "Message")?.Value;
            var rawStack = errorInfo?.Elements().FirstOrDefault(child => child.Name.LocalName == "StackTrace")?.Value;
            var (message, messageTruncated, messageRedacted) = NormalizeDiagnostic(rawMessage, MaximumMessageLength);
            var (stackTrace, stackTruncated, stackRedacted) = NormalizeDiagnostic(rawStack, MaximumStackTraceLength);
            var redacted = testIdRedacted || testNameRedacted || messageRedacted || stackRedacted;
            var truncated = testIdTruncated || testNameTruncated || messageTruncated || stackTruncated;
            var digestInput = string.Join("\0", testId, testName, outcome.ToString(), outcomeText, message ?? "", stackTrace ?? "",
                redacted.ToString(CultureInfo.InvariantCulture), truncated.ToString(CultureInfo.InvariantCulture));
            var recordDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput))).ToLowerInvariant();
            results.Add(new CiTestResultRecord(testId, testName, outcome, outcomeText, message, stackTrace,
                redacted, truncated, recordDigest));
        }

        if (results.Select(item => item.TestId + "\0" + item.TestName).Distinct(StringComparer.Ordinal).Count() != results.Count)
            throw new InvalidDataException("TRX contains duplicate stable test identities.");
        var other = total - passed - failed - skipped;
        if (results.Count(item => item.Outcome == CiTestOutcome.Passed) != passed ||
            results.Count(item => item.Outcome == CiTestOutcome.Failed) != failed ||
            results.Count(item => item.Outcome == CiTestOutcome.Skipped) != skipped ||
            results.Count(item => item.Outcome == CiTestOutcome.Other) != other)
            throw new InvalidDataException("TRX test outcomes do not match the summary counters.");

        return new ParsedTrx(total, passed, failed, skipped, other, results);
    }

    private static CiTestOutcome MapOutcome(string value) => value switch
    {
        "Passed" => CiTestOutcome.Passed,
        "Failed" or "Error" or "Timeout" or "Aborted" => CiTestOutcome.Failed,
        "NotExecuted" or "Inconclusive" or "NotRunnable" or "Disconnected" or
            "Warning" or "Pending" or "InProgress" or "PassedButRunAborted" => CiTestOutcome.Skipped,
        _ => CiTestOutcome.Other
    };

    private static int ReadCounter(XElement counters, string name)
    {
        var raw = (string?)counters.Attribute(name);
        if (raw is null) return 0;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidDataException($"TRX counter '{name}' is invalid.");
        return value;
    }

    private static string RequiredAttribute(XElement element, string name, int maximumLength)
    {
        var value = (string?)element.Attribute(name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new InvalidDataException($"TRX result attribute '{name}' is missing or invalid.");
        return value;
    }

    private static (string? Value, bool Truncated, bool Redacted) NormalizeDiagnostic(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, false, false);
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var redacted = SensitiveValuePattern.IsMatch(normalized);
        if (redacted) normalized = SensitiveValuePattern.Replace(normalized, "[REDACTED]");
        var truncated = normalized.Length > maximumLength;
        if (truncated) normalized = normalized[..maximumLength] + " [TRUNCATED]";
        return (normalized, truncated, redacted);
    }

    private sealed record ParsedTrx(
        int Total,
        int Passed,
        int Failed,
        int Skipped,
        int Other,
        IReadOnlyList<CiTestResultRecord> Results);
}

public enum CiTestOutcome
{
    Passed,
    Failed,
    Skipped,
    Other
}

public sealed record CiTestResultRecord(
    string TestId,
    string TestName,
    CiTestOutcome Outcome,
    string RawOutcome,
    string? Message,
    string? StackTrace,
    bool SensitiveContentRedacted,
    bool DiagnosticTruncated,
    string RecordSha256);
