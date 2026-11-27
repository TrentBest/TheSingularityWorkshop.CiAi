using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Renderer-neutral projection of the dependency declarations in a supplied set of accepted
/// source ledgers. This is intentionally a partial-discovery model, not a claim that every
/// repository in an ecosystem has been observed.
/// </summary>
public sealed record CiAiEcosystemGraphSnapshot
{
    public const string CurrentSchema = "ciai.ecosystem-dependency-graph.v1";

    private CiAiEcosystemGraphSnapshot(
        string focalRepository,
        IReadOnlyList<CiAiEcosystemGraphNode> nodes,
        IReadOnlyList<CiAiEcosystemGraphEdge> edges,
        IReadOnlyList<string> observedSourceRepositories)
    {
        FocalRepository = focalRepository;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
        ObservedSourceRepositories = Array.AsReadOnly(observedSourceRepositories.ToArray());
        Coverage = CiAiEcosystemGraphCoverage.Partial;
        Validate();
    }

    public string SchemaVersion => CurrentSchema;
    public string FocalRepository { get; }
    public IReadOnlyList<CiAiEcosystemGraphNode> Nodes { get; }
    public IReadOnlyList<CiAiEcosystemGraphEdge> Edges { get; }
    public IReadOnlyList<string> ObservedSourceRepositories { get; }

    /// <summary>
    /// Supplied ledgers describe only their own sources. Without an authoritative repository
    /// inventory and verified snapshots for every inventoried source, coverage is always partial.
    /// </summary>
    public CiAiEcosystemGraphCoverage Coverage { get; }

    /// <summary>
    /// Builds a deterministic directed graph from the latest accepted ledger for each source.
    /// Cycles, diamonds, and shared dependencies are represented by one node per repository identity.
    /// ADP clusters are included only when explicitly supplied; membership is never inferred.
    /// </summary>
    public static CiAiEcosystemGraphSnapshot FromLedgers(
        string focalRepository,
        IEnumerable<CiAiDependencyRegistrationLedger> ledgers,
        IEnumerable<CiAiAdpClusterDefinition>? adpClusters = null)
    {
        if (!RepositoryIdentity.IsValid(focalRepository))
            throw new ArgumentException("Focal repository must use canonical owner/repository identity.", nameof(focalRepository));
        ArgumentNullException.ThrowIfNull(ledgers);

        var ledgerBySource = new Dictionary<string, CiAiDependencyRegistrationLedger>(StringComparer.OrdinalIgnoreCase);
        foreach (var ledger in ledgers)
        {
            ArgumentNullException.ThrowIfNull(ledger);
            if (!RepositoryIdentity.IsValid(ledger.SourceRepository) || ledger.SourceRevision < 1 ||
                ledger.SourceCommitSha is not { Length: 40 } || !ledger.SourceCommitSha.All(Uri.IsHexDigit))
                throw new ArgumentException("Every ledger must have a canonical source, positive revision, and exact full commit SHA.", nameof(ledgers));

            if (!ledgerBySource.TryAdd(ledger.SourceRepository, ledger))
                throw new ArgumentException($"More than one accepted ledger was supplied for '{ledger.SourceRepository}'.", nameof(ledgers));

            foreach (var registration in ledger.Registrations)
            {
                if (!string.Equals(registration.SourceRepository, ledger.SourceRepository, StringComparison.OrdinalIgnoreCase) ||
                    !RepositoryIdentity.IsValid(registration.TargetRepository) ||
                    string.Equals(registration.SourceRepository, registration.TargetRepository, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Ledger contains a registration inconsistent with its source or target identity.", nameof(ledgers));
            }
        }

        var nodes = new Dictionary<string, CiAiEcosystemGraphNode>(StringComparer.Ordinal);
        var edges = new Dictionary<string, CiAiEcosystemGraphEdge>(StringComparer.Ordinal);

        AddRepository(focalRepository, isFocal: true);

        foreach (var ledger in ledgerBySource.Values.OrderBy(x => x.SourceRepository, StringComparer.OrdinalIgnoreCase))
        {
            var sourceId = AddRepository(ledger.SourceRepository,
                string.Equals(ledger.SourceRepository, focalRepository, StringComparison.OrdinalIgnoreCase));
            foreach (var registration in ledger.Registrations
                         .OrderBy(x => x.DeclarationIndex)
                         .ThenBy(x => x.CandidateIndex)
                         .ThenBy(x => x.RegistrationId, StringComparer.Ordinal))
            {
                var targetId = AddRepository(registration.TargetRepository,
                    string.Equals(registration.TargetRepository, focalRepository, StringComparison.OrdinalIgnoreCase));
                var label = registration.DependencyKind == "fallback-candidate"
                    ? $"Fallback candidate {registration.CandidateIndex + 1}"
                    : registration.Capability is null
                        ? "Required dependency"
                        : $"Required dependency · {registration.Capability}";
                var details = $"Registration: {registration.RegistrationId} · source revision: {ledger.SourceRevision} · " +
                              $"source SHA: {ledger.SourceCommitSha} · declaration: {registration.DeclarationIndex} · " +
                              $"candidate: {registration.CandidateIndex}";
                AddEdge(new(
                    StableId("dependency-edge", registration.RegistrationId),
                    sourceId,
                    targetId,
                    registration.DependencyKind,
                    label,
                    details,
                    registration.DeclarationIndex,
                    registration.CandidateIndex));
            }
        }

        var clusters = (adpClusters ?? Array.Empty<CiAiAdpClusterDefinition>()).ToArray();
        if (clusters.Any(x => x is null))
            throw new ArgumentException("ADP cluster collection cannot contain null entries.", nameof(adpClusters));
        var clusterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cluster in clusters.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(cluster);
            if (!clusterIds.Add(cluster.Id))
                throw new ArgumentException($"Duplicate ADP cluster '{cluster.Id}'.", nameof(adpClusters));

            var clusterNodeId = StableId("adp-cluster", cluster.Id);
            nodes.Add(clusterNodeId, new(clusterNodeId, "adp-cluster", cluster.Title,
                $"Explicit ADP membership · cluster: {cluster.Id}", cluster.Id, null));
            foreach (var member in cluster.MemberRepositories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var memberId = AddRepository(member,
                    string.Equals(member, focalRepository, StringComparison.OrdinalIgnoreCase));
                AddEdge(new(
                    StableId("adp-membership", cluster.Id.ToUpperInvariant() + "|" + member.ToUpperInvariant()),
                    clusterNodeId,
                    memberId,
                    "adp-membership",
                    "explicit ADP member",
                    $"Membership declared by cluster '{cluster.Id}'",
                    0,
                    0));
            }
        }

        return new CiAiEcosystemGraphSnapshot(
            focalRepository,
            nodes.Values.OrderBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(),
            edges.Values.OrderBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.SourceNodeId, StringComparer.Ordinal)
                .ThenBy(x => x.DeclarationOrder)
                .ThenBy(x => x.CandidateOrder)
                .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(),
            ledgerBySource.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());

        string AddRepository(string repository, bool isFocal)
        {
            if (!RepositoryIdentity.IsValid(repository))
                throw new ArgumentException($"Repository identity '{repository}' is not canonical.");
            var id = RepositoryNodeId(repository);
            var node = new CiAiEcosystemGraphNode(id, isFocal ? "repository-focal" : "repository",
                repository, isFocal ? "Focal repository" : "Repository endpoint", repository, 
                $"https://github.com/{repository}");
            if (nodes.TryGetValue(id, out var existing))
            {
                if (isFocal && existing.Kind != "repository-focal")
                    nodes[id] = node;
                return id;
            }
            nodes.Add(id, node);
            return id;
        }

        void AddEdge(CiAiEcosystemGraphEdge edge)
        {
            if (!edges.TryAdd(edge.Id, edge))
                throw new ArgumentException($"Duplicate graph edge identity '{edge.Id}'.");
        }
    }

    private void Validate()
    {
        if (!RepositoryIdentity.IsValid(FocalRepository))
            throw new ArgumentException("Focal repository identity is invalid.");
        var nodeIds = Nodes.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (nodeIds.Count != Nodes.Count)
            throw new ArgumentException("Ecosystem graph node IDs must be unique.");
        if (Nodes.Count(x => x.Kind == "repository-focal") != 1 ||
            !Nodes.Any(x => x.Repository == FocalRepository && x.Kind == "repository-focal"))
            throw new ArgumentException("Ecosystem graph must contain exactly one focal repository node.");
        var edgeIds = Edges.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (edgeIds.Count != Edges.Count ||
            Edges.Any(x => !nodeIds.Contains(x.SourceNodeId) || !nodeIds.Contains(x.TargetNodeId)))
            throw new ArgumentException("Ecosystem graph edges must be unique and reference existing nodes.");
    }

    private static string RepositoryNodeId(string repository) =>
        StableId("repository", repository.ToUpperInvariant());

    private static string StableId(string prefix, string value) =>
        prefix + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))
            .AsSpan(0, 12)).ToLowerInvariant();
}

/// <summary>Discovery coverage for an ecosystem projection.</summary>
public enum CiAiEcosystemGraphCoverage
{
    Partial
}

/// <summary>A first-class, explicit ADP cluster; membership is not inferred from graph topology.</summary>
public sealed record CiAiAdpClusterDefinition
{
    public CiAiAdpClusterDefinition(string id, string title, IEnumerable<string> memberRepositories)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("ADP cluster ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("ADP cluster title is required.", nameof(title));
        ArgumentNullException.ThrowIfNull(memberRepositories);

        var members = memberRepositories.ToArray();
        if (members.Length == 0 || members.Any(x => !RepositoryIdentity.IsValid(x)))
            throw new ArgumentException("An ADP cluster requires one or more canonical repository members.", nameof(memberRepositories));
        if (members.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Length)
            throw new ArgumentException("ADP cluster members must be unique.", nameof(memberRepositories));

        Id = id.Trim();
        Title = title.Trim();
        MemberRepositories = Array.AsReadOnly(members.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<string> MemberRepositories { get; }
}

/// <summary>A repository or explicit cluster in the ecosystem dependency graph.</summary>
public sealed record CiAiEcosystemGraphNode(
    string Id,
    string Kind,
    string Label,
    string Details,
    string? Repository,
    string? Url);

/// <summary>A directed dependency or explicit cluster-membership relationship.</summary>
public sealed record CiAiEcosystemGraphEdge(
    string Id,
    string SourceNodeId,
    string TargetNodeId,
    string Kind,
    string Label,
    string Details,
    int DeclarationOrder,
    int CandidateOrder);
