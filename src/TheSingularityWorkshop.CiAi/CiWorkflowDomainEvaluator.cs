namespace TheSingularityWorkshop.CiAi;

/// <summary>Result of evaluating a declared evidence policy for one workflow family.</summary>
public sealed record CiWorkflowDomainEvaluation(
    string DomainId,
    string DomainName,
    CiEvidenceEvaluation Evidence)
{
    public bool IsPassing => Evidence.IsPassing;
}

/// <summary>
/// Applies the existing fail-closed evidence evaluator through the vocabulary of a named workflow domain.
/// This evaluates supplied evidence only; it does not collect evidence or authorize consequential actions.
/// </summary>
public static class CiWorkflowDomainEvaluator
{
    public static CiWorkflowDomainEvaluation Evaluate(
        string domainId,
        string repository,
        string commitSha,
        IEnumerable<CiRequiredEvidence> requirements,
        IEnumerable<CiEvidenceFact> facts)
    {
        var domain = CiWorkflowDomainCatalog.GetRequired(domainId);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(facts);

        var materializedRequirements = requirements.ToArray();
        var supportedKinds = domain.EvidenceKinds.ToHashSet(StringComparer.Ordinal);
        var unsupported = materializedRequirements
            .Where(requirement => requirement is null || !supportedKinds.Contains(requirement.Id))
            .Select(requirement => requirement?.Id ?? "<null>")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length > 0)
            throw new ArgumentException(
                $"Evidence requirement(s) are not declared by domain '{domain.Id}': {string.Join(", ", unsupported)}.",
                nameof(requirements));

        var evaluation = CiRequiredEvidenceEvaluator.Evaluate(
            repository, commitSha, materializedRequirements, facts);
        return new CiWorkflowDomainEvaluation(domain.Id, domain.Name, evaluation);
    }
}
