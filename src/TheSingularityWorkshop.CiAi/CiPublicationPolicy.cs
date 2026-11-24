namespace TheSingularityWorkshop.CiAi;

/// <summary>How a configured publication destination may be updated.</summary>
public enum CiPublicationMode
{
    Disabled = 0,
    Manual = 1,
    Automatic = 2
}

/// <summary>Whether this destination requires a human approval decision.</summary>
public enum CiPublicationApproval
{
    None = 0,
    Required = 1
}

/// <summary>Fail-closed decision produced by evaluating destination policy and exact build evidence.</summary>
public enum CiPublicationDecision
{
    InvalidPolicy = 0,
    Disabled = 1,
    Blocked = 2,
    AwaitingApproval = 3,
    AwaitingManualAction = 4,
    PublishAutomatically = 5
}

/// <summary>
/// Destination-specific publication policy. Approval is deliberately not a universal deployment gate:
/// an automatic Pages destination can explicitly use <see cref="CiPublicationApproval.None"/>.
/// This model evaluates eligibility only; it does not publish or authorize provider credentials.
/// </summary>
public sealed record CiPublicationPolicy(
    string DestinationId,
    CiPublicationMode Mode,
    CiPublicationApproval Approval)
{
    /// <summary>Returns stable validation codes for malformed or contradictory destination policy.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DestinationId) ||
            DestinationId.Length > 128 ||
            !DestinationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            errors.Add("publication.destination-id-invalid");
        if (!Enum.IsDefined(Mode)) errors.Add("publication.mode-invalid");
        if (!Enum.IsDefined(Approval)) errors.Add("publication.approval-invalid");
        if (Mode == CiPublicationMode.Disabled && Approval != CiPublicationApproval.None)
            errors.Add("publication.disabled-cannot-require-approval");
        return Array.AsReadOnly(errors.ToArray());
    }

    /// <summary>
    /// Evaluates only the supplied exact-attempt evidence. A stale, failed, cancelled, incomplete,
    /// or ambiguous attempt is never eligible for automatic publication.
    /// </summary>
    public CiPublicationDecision Evaluate(CiPublicationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (Validate().Count != 0) return CiPublicationDecision.InvalidPolicy;
        if (Mode == CiPublicationMode.Disabled) return CiPublicationDecision.Disabled;
        if (!evidence.BuildSucceeded || !evidence.EvidenceComplete ||
            !evidence.IsCurrentAttempt || !evidence.IdentityIsUnambiguous)
            return CiPublicationDecision.Blocked;
        if (Approval == CiPublicationApproval.Required) return CiPublicationDecision.AwaitingApproval;
        return Mode switch
        {
            CiPublicationMode.Automatic => CiPublicationDecision.PublishAutomatically,
            CiPublicationMode.Manual => CiPublicationDecision.AwaitingManualAction,
            _ => CiPublicationDecision.InvalidPolicy
        };
    }
}

/// <summary>Evidence facts for one repository/ref/full-SHA/workflow-run attempt.</summary>
public sealed record CiPublicationEvidence(
    string Repository,
    string SourceRef,
    string SourceCommitSha,
    long RunId,
    int RunAttempt,
    bool BuildSucceeded,
    bool EvidenceComplete,
    bool IsCurrentAttempt,
    bool IdentityIsUnambiguous);
