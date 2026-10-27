using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Result of persisting an immutable dependency release candidate.</summary>
public enum DependencyCandidateStoreOutcome
{
    Saved,
    AlreadyExists,
    Conflict
}

/// <summary>
/// Durable local candidate store. Candidate IDs are mapped to hashed filenames; an existing ID can
/// never be silently overwritten with a different repository, commit, package, or dependency set.
/// Evidence is deliberately re-observed and re-evaluated when a host resumes the candidate.
/// </summary>
public sealed class FileDependencyReleaseCandidateStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public FileDependencyReleaseCandidateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    /// <summary>Saves a candidate once, treats an identical retry as idempotent, and rejects identity reuse.</summary>
    public DependencyCandidateStoreOutcome Save(DependencyReleaseCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Directory.CreateDirectory(_directory);
        var path = GetPath(candidate.CandidateId);
        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);

        if (File.Exists(path))
        {
            var existing = ReadCandidate(path);
            return AreEquivalent(existing, candidate)
                ? DependencyCandidateStoreOutcome.AlreadyExists
                : DependencyCandidateStoreOutcome.Conflict;
        }

        var snapshot = StoredCandidate.From(candidate);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, snapshot, JsonOptions);
                output.Flush(flushToDisk: true);
            }

            // Same-directory move keeps the completed snapshot from being observed partially written.
            File.Move(temporaryPath, path);
            return DependencyCandidateStoreOutcome.Saved;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Loads the exact candidate snapshot, or returns null when the ID has not been saved.</summary>
    public DependencyReleaseCandidate? Load(string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        var path = GetPath(candidateId);
        if (!File.Exists(path))
            return null;

        using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        return ReadCandidate(path);
    }

    private string GetPath(string candidateId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidateId)));
        return Path.Combine(_directory, hash + ".json");
    }

    private static DependencyReleaseCandidate ReadCandidate(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stored = JsonSerializer.Deserialize<StoredCandidate>(input)
            ?? throw new InvalidDataException("Candidate store snapshot is empty.");
        if (stored.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Candidate store schema version is unsupported.");

        try
        {
            return new DependencyReleaseCandidate(stored.Repository, stored.CommitSha, stored.PackageId,
                stored.PackageVersion, stored.CandidateId,
                stored.Dependencies.Select(dependency => new RequiredPublishedDependency(
                    dependency.UpstreamRepository, dependency.UpstreamCommitSha,
                    dependency.PackageId, dependency.PackageVersion)).ToArray());
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Candidate store snapshot violates the candidate contract.", exception);
        }
    }

    private static bool AreEquivalent(DependencyReleaseCandidate left, DependencyReleaseCandidate right) =>
        string.Equals(left.CandidateId, right.CandidateId, StringComparison.Ordinal) &&
        string.Equals(left.Repository, right.Repository, StringComparison.Ordinal) &&
        string.Equals(left.CommitSha, right.CommitSha, StringComparison.Ordinal) &&
        string.Equals(left.PackageId, right.PackageId, StringComparison.Ordinal) &&
        string.Equals(left.PackageVersion, right.PackageVersion, StringComparison.Ordinal) &&
        left.Dependencies.SequenceEqual(right.Dependencies);

    private sealed record StoredCandidate(
        int SchemaVersion,
        string CandidateId,
        string Repository,
        string CommitSha,
        string PackageId,
        string PackageVersion,
        StoredDependency[] Dependencies)
    {
        public static StoredCandidate From(DependencyReleaseCandidate candidate) => new(
            CurrentSchemaVersion, candidate.CandidateId, candidate.Repository, candidate.CommitSha,
            candidate.PackageId, candidate.PackageVersion,
            candidate.Dependencies.Select(dependency => new StoredDependency(
                dependency.UpstreamRepository, dependency.UpstreamCommitSha,
                dependency.PackageId, dependency.PackageVersion)).ToArray());
    }

    private sealed record StoredDependency(
        string UpstreamRepository,
        string UpstreamCommitSha,
        string PackageId,
        string PackageVersion);
}
