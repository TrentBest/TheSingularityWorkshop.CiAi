using TheSingularityWorkshop.ProtocolAi;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Explicit evidence inputs required before a GitHub Actions observation can be represented
/// as a ProtocolAi source event. Values in this record must come from independently verified
/// workflow/check-out/evidence inspection; the run and job API responses do not prove them.
/// </summary>
public sealed record CiProtocolEvidenceBinding(
    string CandidateRef,
    string EventSha,
    string TestedCheckoutSha,
    string WorkflowDefinitionRef,
    string WorkflowDefinitionSha,
    string EvidenceReference,
    string EvidenceSha256);

/// <summary>
/// Maps one exact GitHub Actions run attempt and job to a ProtocolAi v1 source event.
/// CiAi remains the authority for evidence verification; this mapper rejects incomplete
/// bindings and never infers tested-checkout or workflow-definition identity from head_sha.
/// </summary>
public static class GitHubActionsProtocolSourceEventMapper
{
    public static ProtocolEventEnvelope CreateEnvelope(
        CiWorkflowRunObservation run,
        CiWorkflowJobObservation job,
        CiProtocolEvidenceBinding evidence,
        string messageId,
        string candidateId,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);

        var snapshot = run.Run;
        if (!string.Equals(job.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                snapshot.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            job.RunAttempt != run.RunAttempt)
            throw new InvalidDataException("Job identity does not belong to the selected workflow run attempt.");

        if (!string.Equals(evidence.EventSha, snapshot.HeadSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Event SHA does not match the provider-observed candidate head SHA.");
        if (!string.Equals(evidence.TestedCheckoutSha, snapshot.HeadSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Tested checkout SHA must be independently verified and exactly match the candidate head SHA.");

        RequireCommitSha(evidence.EventSha, nameof(evidence.EventSha));
        RequireCommitSha(evidence.TestedCheckoutSha, nameof(evidence.TestedCheckoutSha));
        RequireCommitSha(evidence.WorkflowDefinitionSha, nameof(evidence.WorkflowDefinitionSha));
        if (string.IsNullOrWhiteSpace(evidence.CandidateRef) ||
            string.IsNullOrWhiteSpace(evidence.WorkflowDefinitionRef) ||
            string.IsNullOrWhiteSpace(evidence.EvidenceReference))
            throw new InvalidDataException("Candidate ref, workflow definition ref, and evidence reference are required.");
        if (evidence.EvidenceSha256.Length != 64 || !evidence.EvidenceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Evidence SHA-256 must be exactly 64 hexadecimal characters.");

        if (job.RunId != snapshot.RunId || job.RunAttempt != run.RunAttempt ||
            !string.Equals(job.EvidenceUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(job.EvidenceUrl.AbsolutePath,
                $"/{snapshot.Repository}/actions/runs/{snapshot.RunId}/job/{job.JobId}",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Observed job evidence identity does not match the selected run.");

        var outcome = MapOutcome(snapshot.Status, snapshot.Conclusion, job.Status, job.Conclusion);
        var sourceEvent = new ProtocolSourceEvent(
            "github-actions",
            "workflow.job.observed",
            $"run:{snapshot.RunId}/attempt:{run.RunAttempt}/job:{job.JobId}",
            outcome,
            $"GitHub Actions run '{snapshot.WorkflowName}' job '{job.Name}' reported status '{job.Status}' and conclusion '{job.Conclusion ?? "none"}'.");

        var provenance = new ProtocolCandidateProvenance(
            snapshot.Repository,
            evidence.CandidateRef,
            snapshot.HeadSha,
            evidence.EventSha,
            evidence.TestedCheckoutSha,
            evidence.WorkflowDefinitionRef,
            evidence.WorkflowDefinitionSha,
            snapshot.RunId,
            run.RunAttempt,
            $"{job.JobId}:{job.Name}",
            evidence.EvidenceReference,
            evidence.EvidenceSha256);

        return ProtocolTypedEventEnvelope.Create(
            messageId, candidateId, "ciai", sourceEvent, createdAt, provenance);
    }

    private static ProtocolSourceEventOutcome MapOutcome(
        CiRunStatus runStatus,
        CiRunConclusion runConclusion,
        string jobStatus,
        string? jobConclusion)
    {
        if (string.Equals(jobConclusion, "cancelled", StringComparison.OrdinalIgnoreCase) ||
            (runStatus == CiRunStatus.Completed && runConclusion == CiRunConclusion.Cancelled))
            return ProtocolSourceEventOutcome.Cancelled;

        if (runStatus == CiRunStatus.Completed &&
            runConclusion is CiRunConclusion.Failure or CiRunConclusion.TimedOut or CiRunConclusion.ActionRequired)
            return ProtocolSourceEventOutcome.Failed;

        if (!string.Equals(jobStatus, "completed", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(jobConclusion, "success", StringComparison.OrdinalIgnoreCase) ||
            runStatus != CiRunStatus.Completed || runConclusion != CiRunConclusion.Success)
            return ProtocolSourceEventOutcome.Unknown;

        return ProtocolSourceEventOutcome.Succeeded;
    }

    private static void RequireCommitSha(string value, string parameterName)
    {
        if (value is not { Length: 40 } || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{parameterName} must be a full 40-character Git commit SHA.");
    }
}
