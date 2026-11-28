namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A source snapshot bound to independently collected, successful GitHub Actions evidence and a
/// host-issued protected-workflow trust decision. Instances can only be created by Verify(...).
/// </summary>
public sealed class CiAiDependencyRegistrationSourceVerification
{
    private CiAiDependencyRegistrationSourceVerification(
        CiAiDependencyRegistrationSnapshot snapshot,
        CiWorkflowEvidencePacketV2 evidence,
        CiWorkflowDefinitionTrustDecision trustDecision)
    {
        Snapshot = snapshot;
        SourceRevision = evidence.RunId;
        SourceRunAttempt = evidence.RunAttempt;
        EvidencePayloadSha256 = evidence.PayloadSha256;
        WorkflowPath = evidence.WorkflowPath;
        SourceRef = evidence.CandidateRef;
        ObservedAt = evidence.ObservedAt;
        TrustDecision = trustDecision;
    }

    public CiAiDependencyRegistrationSnapshot Snapshot { get; }
    public string SourceRepository => Snapshot.SourceRepository;
    public string SourceCommitSha => Snapshot.SourceCommitSha;

    /// <summary>GitHub run ID, monotonically assigned within the source repository; never derived from a SHA.</summary>
    public long SourceRevision { get; }

    public int SourceRunAttempt { get; }
    public string EvidencePayloadSha256 { get; }
    public string WorkflowPath { get; }
    public string SourceRef { get; }
    public DateTimeOffset ObservedAt { get; }
    public CiWorkflowDefinitionTrustDecision TrustDecision { get; }

    /// <summary>
    /// Creates a verification only when the exact source SHA was built successfully by the
    /// approved push workflow on an explicitly allowed source ref, with complete required checks.
    /// </summary>
    public static CiAiDependencyRegistrationSourceVerification Verify(
        CiAiDependencyRegistrationSnapshot snapshot,
        CiWorkflowEvidencePacketV2 evidence,
        CiWorkflowDefinitionTrustDecision trustDecision,
        IEnumerable<string> allowedSourceRefs)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(trustDecision);
        ArgumentNullException.ThrowIfNull(allowedSourceRefs);

        var allowed = allowedSourceRefs.ToArray();
        if (allowed.Length == 0 || allowed.Any(value =>
            string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("refs/heads/", StringComparison.Ordinal) ||
            value.Length > 300 || value.Any(char.IsControl)) ||
            allowed.Distinct(StringComparer.Ordinal).Count() != allowed.Length)
            throw new ArgumentException("Allowed source refs must be a non-empty unique set of canonical branch refs.",
                nameof(allowedSourceRefs));

        var provenance = evidence.WorkflowEvidence.Provenance;
        if (!string.Equals(evidence.Repository, snapshot.SourceRepository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source evidence belongs to a different repository.");
        if (!string.Equals(evidence.CandidateSha, snapshot.SourceCommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source evidence checkout SHA does not match the dependency snapshot SHA.");
        if (!string.Equals(evidence.WorkflowEvidence.ObservedRun.HeadSha, snapshot.SourceCommitSha,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Provider-observed workflow head SHA does not match the dependency snapshot.");
        if (!allowed.Contains(evidence.CandidateRef, StringComparer.Ordinal) ||
            !string.Equals(provenance.EventRef, evidence.CandidateRef, StringComparison.Ordinal) ||
            !string.Equals(provenance.CheckoutRef, evidence.CandidateRef, StringComparison.Ordinal))
            throw new InvalidDataException("Source event and checkout refs are not the same explicitly allowed branch.");
        if (!string.Equals(provenance.EventName, "push", StringComparison.Ordinal))
            throw new InvalidDataException("Dependency declarations may be verified only from a push event.");
        if (!trustDecision.IsTrusted || !trustDecision.IsFor(provenance) ||
            !string.Equals(evidence.WorkflowPath, trustDecision.WorkflowPath, StringComparison.Ordinal))
            throw new InvalidDataException("Source workflow does not have a matching host-issued protected-definition trust decision.");
        if (!evidence.EvidenceComplete || !evidence.IsSuccessful ||
            !evidence.WorkflowEvidence.WorkflowDefinitionTrusted ||
            !evidence.WorkflowEvidence.CheckoutVerified)
            throw new InvalidDataException("Source workflow evidence is incomplete, untrusted, or unsuccessful.");
        if (evidence.RunId <= 0 || evidence.RunAttempt <= 0)
            throw new InvalidDataException("Source workflow run identity is invalid.");

        return new CiAiDependencyRegistrationSourceVerification(snapshot, evidence, trustDecision);
    }
}
