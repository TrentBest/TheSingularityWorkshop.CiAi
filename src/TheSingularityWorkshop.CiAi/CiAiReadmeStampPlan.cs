namespace TheSingularityWorkshop.CiAi;

/// <summary>Immutable snapshot of a complete README stamping preview.</summary>
public sealed class CiAiReadmeStampPlan
{
    internal sealed record Change(string RelativePath, string OriginalContent, string StampedContent);
    internal CiAiReadmeStampPlan(string repositoryRoot, IReadOnlyList<Change> changes)
    {
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        Entries = Array.AsReadOnly(changes.ToArray());
        Changes = Array.AsReadOnly(changes.Select(change => new CiAiReadmeStampPlanEntry(change.RelativePath)).ToArray());
    }
    internal string RepositoryRoot { get; }
    internal IReadOnlyList<Change> Entries { get; }
    public IReadOnlyList<CiAiReadmeStampPlanEntry> Changes { get; }
}
public sealed record CiAiReadmeStampPlanEntry(string RelativePath);
