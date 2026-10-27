namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Coordinates durable candidate registration and re-evaluation after a host resumes work.
/// Evidence is supplied by the host for each evaluation; it is never inferred from candidate state.
/// </summary>
public sealed class DependencyReleaseGateCoordinator
{
    private readonly FileDependencyReleaseCandidateStore _store;

    public DependencyReleaseGateCoordinator(FileDependencyReleaseCandidateStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Registers a candidate without replacing a different candidate that reused its ID.</summary>
    public DependencyCandidateStoreOutcome Register(DependencyReleaseCandidate candidate) =>
        _store.Save(candidate);

    /// <summary>
    /// Re-evaluates the originally stored candidate with newly observed evidence.
    /// Throws when the candidate was never registered; it never accepts a replacement candidate object.
    /// </summary>
    public DependencyReleaseGateEvaluation Resume(
        string candidateId,
        IEnumerable<PublishedDependencyEvidence> currentEvidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(currentEvidence);

        var candidate = _store.Load(candidateId)
            ?? throw new KeyNotFoundException("No persisted dependency release candidate exists for the supplied ID.");
        return DependencyReleaseGateEvaluator.Evaluate(candidate, currentEvidence);
    }
}
