namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Identifies the exact GitHub Actions job steps that prove a trusted workflow resolved
/// and checked out one isolated candidate commit.
/// </summary>
public sealed record CiWorkflowCandidateCheckoutEvidence
{
    public CiWorkflowCandidateCheckoutEvidence(
        string candidateRef,
        string candidateSha,
        string jobName,
        string resolutionStepName,
        string verificationStepName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionStepName);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationStepName);

        if (!IsFeatureBranchRef(candidateRef))
            throw new ArgumentException("Candidate ref must identify an isolated feature branch.", nameof(candidateRef));
        if (candidateSha.Length != 40 || !candidateSha.All(Uri.IsHexDigit))
            throw new ArgumentException("Candidate SHA must be a full 40-character hexadecimal commit identifier.", nameof(candidateSha));

        ValidateStepIdentity(jobName, nameof(jobName));
        ValidateStepIdentity(resolutionStepName, nameof(resolutionStepName));
        ValidateStepIdentity(verificationStepName, nameof(verificationStepName));
        if (string.Equals(resolutionStepName, verificationStepName, StringComparison.Ordinal))
            throw new ArgumentException("Resolution and checkout verification steps must be distinct.");

        CandidateRef = candidateRef;
        CandidateSha = candidateSha.ToLowerInvariant();
        JobName = jobName;
        ResolutionStepName = resolutionStepName;
        VerificationStepName = verificationStepName;
    }

    public string CandidateRef { get; }
    public string CandidateSha { get; }
    public string JobName { get; }
    public string ResolutionStepName { get; }
    public string VerificationStepName { get; }

    private static bool IsFeatureBranchRef(string headRef)
    {
        const string prefix = "refs/heads/feature/";
        if (string.IsNullOrWhiteSpace(headRef) ||
            !headRef.StartsWith(prefix, StringComparison.Ordinal) ||
            headRef.Any(char.IsControl) || headRef.Any(char.IsWhiteSpace) ||
            headRef.Contains("..", StringComparison.Ordinal) ||
            headRef.Contains("@{", StringComparison.Ordinal) ||
            headRef.Contains('\\') || headRef.EndsWith("/", StringComparison.Ordinal) ||
            headRef.EndsWith(".", StringComparison.Ordinal))
            return false;

        var branch = headRef["refs/heads/".Length..];
        var invalid = new[] { '~', '^', ':', '?', '*', '[', ']' };
        return branch.Length > 0 && branch != "@" && branch.IndexOfAny(invalid) < 0 &&
            branch.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != ".." &&
                !segment.StartsWith(".", StringComparison.Ordinal) &&
                !segment.EndsWith(".lock", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateStepIdentity(string value, string parameterName)
    {
        if (value.Length > 200 || value.Any(char.IsControl))
            throw new ArgumentException("Workflow job and step identities must be bounded and control-free.", parameterName);
    }
}
