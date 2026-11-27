namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// A receiver-side view of the latest complete dependency snapshot accepted for one source.
/// The revision is assigned by a trusted integration; it is not inferred from a Git SHA.
/// </summary>
public sealed record CiAiDependencyRegistrationLedger
{
    private CiAiDependencyRegistrationLedger(
        string sourceRepository,
        long sourceRevision,
        string sourceCommitSha,
        IReadOnlyList<CiAiDependencyRegistration> registrations)
    {
        SourceRepository = sourceRepository;
        SourceRevision = sourceRevision;
        SourceCommitSha = sourceCommitSha;
        Registrations = Array.AsReadOnly(registrations.ToArray());
    }

    public string SourceRepository { get; }
    public long SourceRevision { get; }
    public string SourceCommitSha { get; }

    /// <summary>
    /// The complete active registration set at SourceRevision.
    /// Absence from this set means withdrawn only because the accepted input was a complete snapshot.
    /// </summary>
    public IReadOnlyList<CiAiDependencyRegistration> Registrations { get; }

    /// <summary>
    /// Applies a complete snapshot after the caller has authenticated its source and verified that
    /// the snapshot's source SHA is the exact revision being accepted. SourceRevision must be a
    /// trusted, monotonically increasing sequence for that source; Git SHAs are not sortable.
    /// This method performs no authentication, network I/O, or persistence.
    /// </summary>
    public static CiAiDependencyRegistrationApplyResult ApplyVerifiedSnapshot(
        CiAiDependencyRegistrationLedger? current,
        CiAiDependencyRegistrationSnapshot snapshot,
        long sourceRevision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (sourceRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision),
                "A verified source revision must be a positive, monotonically increasing value.");

        if (current is not null &&
            !string.Equals(current.SourceRepository, snapshot.SourceRepository, StringComparison.OrdinalIgnoreCase))
            return new(CiAiDependencyRegistrationApplyStatus.SourceMismatch, current);

        if (current is not null)
        {
            if (sourceRevision < current.SourceRevision)
                return new(CiAiDependencyRegistrationApplyStatus.Stale, current);

            if (sourceRevision == current.SourceRevision)
            {
                var identical = string.Equals(current.SourceCommitSha, snapshot.SourceCommitSha,
                                   StringComparison.OrdinalIgnoreCase) &&
                               current.Registrations.SequenceEqual(snapshot.Registrations);
                return identical
                    ? new(CiAiDependencyRegistrationApplyStatus.Duplicate, current)
                    : new(CiAiDependencyRegistrationApplyStatus.Conflict, current);
            }
        }

        var next = new CiAiDependencyRegistrationLedger(
            snapshot.SourceRepository,
            sourceRevision,
            snapshot.SourceCommitSha,
            snapshot.Registrations);
        return new(CiAiDependencyRegistrationApplyStatus.Applied, next);
    }
}

/// <summary>Outcome of applying a complete, externally verified source snapshot.</summary>
public sealed record CiAiDependencyRegistrationApplyResult(
    CiAiDependencyRegistrationApplyStatus Status,
    CiAiDependencyRegistrationLedger? Ledger);

/// <summary>Fail-closed receiver reconciliation outcomes.</summary>
public enum CiAiDependencyRegistrationApplyStatus
{
    Applied,
    Duplicate,
    Stale,
    Conflict,
    SourceMismatch
}
