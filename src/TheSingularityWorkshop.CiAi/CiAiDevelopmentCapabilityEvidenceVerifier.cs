namespace TheSingularityWorkshop.CiAi;

/// <summary>A TRX archive and report already validated against exact GitHub run, job, artifact, and provenance metadata.</summary>
public sealed record CiAiDevelopmentCapabilityVerifiedTestArtifact
{
    private CiAiDevelopmentCapabilityVerifiedTestArtifact(
        string kind,
        CiTestResultArtifactArchive archive,
        CiTestResultReport report)
    {
        Kind = kind;
        Archive = archive;
        Report = report;
    }

    public string Kind { get; }
    public CiTestResultArtifactArchive Archive { get; }
    public CiTestResultReport Report { get; }

    public static CiAiDevelopmentCapabilityVerifiedTestArtifact FromVerifiedReport(
        string kind,
        CiTestResultArtifactArchive archive,
        CiTestResultReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(report);
        Ensure(report.SourceArtifactId is > 0, "devcap.test-artifact-provider-id-missing");
        Ensure(!string.IsNullOrWhiteSpace(report.SourceArtifactName), "devcap.test-artifact-provider-name-missing");
        Ensure(string.Equals(report.ProviderArchiveDigest, archive.ProviderArchiveDigest, StringComparison.OrdinalIgnoreCase),
            "devcap.test-artifact-archive-digest-mismatch");
        Ensure(report.ArchiveSizeBytes == archive.ArchiveSizeBytes, "devcap.test-artifact-archive-size-mismatch");
        Ensure(string.Equals(report.PayloadSha256, archive.TrxSha256, StringComparison.OrdinalIgnoreCase),
            "devcap.test-artifact-trx-digest-mismatch");
        Ensure(report.PayloadSizeBytes == archive.TrxBytes.Length, "devcap.test-artifact-trx-size-mismatch");
        return new CiAiDevelopmentCapabilityVerifiedTestArtifact(kind, archive, report);
    }

    private static void Ensure(bool condition, string errorCode)
    {
        if (!condition) throw new InvalidDataException(errorCode);
    }
}

/// <summary>
/// Completes development-capability verification only after each declared TRX/provenance archive has been
/// independently validated by the existing CiTestResultReport pipeline.
/// </summary>
public static class CiAiDevelopmentCapabilityEvidenceVerifier
{
    public static CiAiDevelopmentCapabilityEvidenceValidation VerifyTestResults(
        CiAiDevelopmentCapabilityArtifactBinding binding,
        CiAiDevelopmentCapabilityEvidenceValidation envelopeValidation,
        IReadOnlyDictionary<string, CiAiDevelopmentCapabilityVerifiedTestArtifact> testArtifacts)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(envelopeValidation);
        ArgumentNullException.ThrowIfNull(testArtifacts);

        var evidence = envelopeValidation.Evidence;
        if (!envelopeValidation.IsEnvelopeValid || evidence is null)
            return envelopeValidation;

        try
        {
            Ensure(testArtifacts.Count == binding.ExpectedTestResultArtifacts.Count,
                "devcap.test-artifact-count-mismatch");

            foreach (var expected in binding.ExpectedTestResultArtifacts)
            {
                if (!testArtifacts.TryGetValue(expected.Key, out var verified))
                    throw new InvalidDataException("devcap.test-artifact-not-verified:" + expected.Key);
                Ensure(string.Equals(verified.Kind, expected.Key, StringComparison.Ordinal),
                    "devcap.test-artifact-kind-mismatch:" + expected.Key);

                var declared = evidence.TestArtifacts.SingleOrDefault(item =>
                    string.Equals(item.Kind, expected.Key, StringComparison.Ordinal));
                if (declared is null)
                    throw new InvalidDataException("devcap.test-artifact-not-declared:" + expected.Key);
                Ensure(string.Equals(declared.ArtifactName, expected.Value, StringComparison.Ordinal),
                    "devcap.test-artifact-name-mismatch:" + expected.Key);

                var report = verified.Report;
                var archive = verified.Archive;
                Ensure(string.Equals(report.Repository, binding.ProviderRepository, StringComparison.Ordinal),
                    "devcap.test-report-repository-mismatch:" + expected.Key);
                Ensure(report.RunId == binding.WorkflowRunId && report.RunAttempt == binding.WorkflowAttempt,
                    "devcap.test-report-run-identity-mismatch:" + expected.Key);
                Ensure(string.Equals(report.WorkflowPath, binding.WorkflowPath, StringComparison.Ordinal) &&
                    string.Equals(report.WorkflowDefinitionRef, binding.WorkflowRef, StringComparison.Ordinal) &&
                    string.Equals(report.WorkflowDefinitionSha, binding.WorkflowSha, StringComparison.OrdinalIgnoreCase),
                    "devcap.test-report-workflow-identity-mismatch:" + expected.Key);
                Ensure(string.Equals(report.EventName, binding.EventName, StringComparison.Ordinal) &&
                    string.Equals(report.EventRef, binding.EventRef, StringComparison.Ordinal) &&
                    string.Equals(report.EventSha, binding.EventSha, StringComparison.OrdinalIgnoreCase),
                    "devcap.test-report-event-identity-mismatch:" + expected.Key);
                Ensure(string.Equals(report.CandidateRef, binding.CheckoutRef, StringComparison.Ordinal) &&
                    string.Equals(report.CandidateSha, binding.CheckoutSha, StringComparison.OrdinalIgnoreCase),
                    "devcap.test-report-checkout-identity-mismatch:" + expected.Key);
                Ensure(report.JobId == binding.ProviderJobId &&
                    string.Equals(report.JobName, binding.ProviderJobName, StringComparison.Ordinal) &&
                    string.Equals(report.SourceJobKey, binding.ProviderJobKey, StringComparison.Ordinal) &&
                    string.Equals(report.JobConclusion, binding.ProviderJobConclusion, StringComparison.Ordinal),
                    "devcap.test-report-job-identity-mismatch:" + expected.Key);
                Ensure(report.RequiredCheckStatus == CiRequiredCheckStatus.Passed &&
                    report.Verdict == CiTestResultVerdict.CompletePass &&
                    report.Total > 0 && report.Failed == 0 && report.Other == 0 &&
                    report.Passed + report.Skipped == report.Total,
                    "devcap.required-test-results-not-pass:" + expected.Key);
                Ensure(string.Equals(report.SourceArtifactName, expected.Value, StringComparison.Ordinal) &&
                    report.SourceArtifactId is > 0,
                    "devcap.test-report-artifact-identity-mismatch:" + expected.Key);
                Ensure(string.Equals(report.ProviderArchiveDigest, archive.ProviderArchiveDigest, StringComparison.OrdinalIgnoreCase) &&
                    report.ArchiveSizeBytes == archive.ArchiveSizeBytes,
                    "devcap.test-report-archive-binding-mismatch:" + expected.Key);
                Ensure(string.Equals(report.PayloadSha256, declared.TrxSha256, StringComparison.OrdinalIgnoreCase) &&
                    report.PayloadSizeBytes == declared.TrxSizeBytes &&
                    string.Equals(archive.TrxSha256, declared.TrxSha256, StringComparison.OrdinalIgnoreCase) &&
                    archive.TrxBytes.Length == declared.TrxSizeBytes,
                    "devcap.test-report-trx-binding-mismatch:" + expected.Key);
                Ensure(string.Equals(archive.ProvenanceSha256, declared.ProvenanceSha256, StringComparison.OrdinalIgnoreCase),
                    "devcap.test-report-provenance-binding-mismatch:" + expected.Key);
                Ensure(report.ArtifactCreatedAt.HasValue && report.ArtifactExpiresAt.HasValue &&
                    report.ArtifactCreatedAt.Value <= report.ObservedAt &&
                    report.ArtifactExpiresAt.Value > report.ObservedAt,
                    "devcap.test-report-artifact-expired-or-invalid:" + expected.Key);
            }

            return new CiAiDevelopmentCapabilityEvidenceValidation(true, true, null, evidence);
        }
        catch (InvalidDataException exception)
        {
            return new CiAiDevelopmentCapabilityEvidenceValidation(true, false, exception.Message, evidence);
        }
    }

    private static void Ensure(bool condition, string errorCode)
    {
        if (!condition) throw new InvalidDataException(errorCode);
    }
}
