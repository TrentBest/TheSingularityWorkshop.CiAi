using System.Security.Cryptography;
using System.Text;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Renderer-neutral, deterministic projection of the effective repository composition.
/// It describes declarations only; it does not represent an expanded execution plan or observed evidence.
/// </summary>
public sealed record CiAiRepositoryGraphSnapshot
{
    public const string CurrentSchema = "ciai.repository-configuration-graph.v1";

    private CiAiRepositoryGraphSnapshot(
        string repository,
        IReadOnlyList<CiAiRepositoryGraphNode> nodes,
        IReadOnlyList<CiAiRepositoryGraphEdge> edges)
    {
        Repository = repository;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
        Validate();
    }

    public string SchemaVersion => CurrentSchema;
    public string Repository { get; }
    public IReadOnlyList<CiAiRepositoryGraphNode> Nodes { get; }
    public IReadOnlyList<CiAiRepositoryGraphEdge> Edges { get; }

    /// <summary>Projects every effective fluent declaration in one immutable repository definition.</summary>
    public static CiAiRepositoryGraphSnapshot FromDefinition(CiAiRepositoryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var nodes = new Dictionary<string, CiAiRepositoryGraphNode>(StringComparer.Ordinal);
        var edges = new List<CiAiRepositoryGraphEdge>();
        var rootId = RepositoryNodeId(definition.Repository);

        AddRepository(definition.Repository, isRoot: true);

        foreach (var badge in definition.Badges)
        {
            var id = StableId("badge", badge.Id);
            AddNode(new(id, "badge", badge.Label, $"Workflow: {badge.WorkflowFile}", true,
                badge.TargetUrl(definition.Repository)));
            AddEdge("configuration", rootId, id, "badge", "enables badge");
        }

        for (var index = 0; index < definition.Dependencies.Count; index++)
        {
            var dependency = definition.Dependencies[index];
            switch (dependency)
            {
                case CiAiRepositoryDependency direct:
                {
                    var repositoryId = AddRepository(direct.Repository);
                    var detail = direct.Capability is null ? "Required repository dependency" :
                        $"Required repository dependency · capability: {direct.Capability}";
                    AddEdge("dependency", rootId, repositoryId, "dependency", detail, index);
                    break;
                }
                case CiAiOrderedDependencyFallback fallback:
                {
                    var groupId = StableId("fallback", index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "|" + string.Join("|", fallback.Repositories));
                    AddNode(new(groupId, "ordered-fallback", "Ordered dependency fallback",
                        "Candidates are evaluated in declared order; order is significant.", true, null));
                    AddEdge("configuration", rootId, groupId, "fallback", "declares fallback", index);
                    for (var candidateIndex = 0; candidateIndex < fallback.Repositories.Count; candidateIndex++)
                    {
                        var repositoryId = AddRepository(fallback.Repositories[candidateIndex]);
                        AddEdge("fallback-candidate", groupId, repositoryId, "fallback-candidate",
                            $"candidate {candidateIndex + 1}", candidateIndex);
                    }
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported dependency expression type '{dependency.GetType().FullName}'.");
            }
        }

        foreach (var extension in definition.Extensions)
        {
            var id = StableId("extension", extension.Id);
            AddNode(new(id, "extension", extension.Id, $"Version: {extension.Version}", true, null));
            AddEdge("configuration", rootId, id, "extension", "registers extension");
        }

        var pipelines = definition.NamedDevelopmentPipelines.Count == 0
            ? new[] { CiAiRepositoryDefinition.DefaultDevelopmentPipeline }
            : definition.NamedDevelopmentPipelines.ToArray();
        foreach (var pipeline in pipelines)
        {
            var isExplicit = definition.NamedDevelopmentPipelines.Count > 0;
            var id = StableId("pipeline", pipeline);
            AddNode(new(id, "development-pipeline", pipeline,
                isExplicit ? "Explicit development pipeline membership" : "Implicit default pipeline",
                isExplicit, null));
            AddEdge("configuration", rootId, id, "pipeline",
                isExplicit ? "includes pipeline" : "uses default pipeline");
        }

        foreach (var relationship in definition.Relationships)
        {
            var sourceId = AddRepository(relationship.SourceRepository);
            var targetId = AddRepository(relationship.TargetRepository);
            var authorityId = AddRepository(relationship.AuthorityRepository);
            var relationshipId = StableId("relationship", relationship.RelationshipId);
            var detail = $"Contract v{relationship.ContractVersion} · artifact: {relationship.ArtifactKind} · " +
                $"authority: {relationship.AuthorityRepository}";
            AddNode(new(relationshipId, "adp-relationship", relationship.RelationshipId, detail, true, null));
            AddEdge("configuration", rootId, relationshipId, "relationship", "declares ADP relationship");
            AddEdge("relationship-source", sourceId, relationshipId, "relationship-source", "source");
            AddEdge("relationship-target", relationshipId, targetId, "relationship-target",
                $"{relationship.Kind} · {relationship.ArtifactKind}");
            AddEdge("relationship-authority", relationshipId, authorityId, "relationship-authority", "authority");
        }

        if (definition.AdpSettings is { } adp)
        {
            var id = StableId("adp", "settings");
            AddNode(new(id, "adp-settings", "Autonomous Development",
                $"Maximum repair attempts: {adp.MaximumRepairAttempts} · " +
                $"Human approval for writes: {(adp.RequireHumanApprovalForWrites ? "required" : "not required")}",
                true, null));
            AddEdge("configuration", rootId, id, "adp", "enables ADP settings");
        }

        if (definition.LlmAllocation is { } allocation)
        {
            var id = StableId("llm-allocation", allocation.Provider + "|" + allocation.Model);
            AddNode(new(id, "llm-allocation", $"{allocation.Provider} · {allocation.Model}",
                $"Concurrency: {allocation.MaximumConcurrentCalls} · Calls/window: {allocation.MaximumCallsPerWindow} · " +
                $"Tokens/window: {allocation.MaximumTokensPerWindow} · Window ticks: {allocation.Window.Ticks} · " +
                $"Priority: {allocation.Priority}",
                true, null));
            AddEdge("configuration", rootId, id, "llm-allocation", "requests LLM allocation");
        }

        var orderedNodes = nodes.Values
            .OrderBy(node => node.Kind, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var orderedEdges = edges
            .OrderBy(edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Order)
            .ThenBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        return new CiAiRepositoryGraphSnapshot(definition.Repository, orderedNodes, orderedEdges);

        string AddRepository(string repository, bool isRoot = false)
        {
            var id = RepositoryNodeId(repository);
            AddNode(new(id, isRoot ? "repository-root" : "repository", repository,
                isRoot ? "Repository whose fluent composition is being projected." : "Repository endpoint",
                true, $"https://github.com/{repository}"));
            return id;
        }

        void AddNode(CiAiRepositoryGraphNode node)
        {
            if (nodes.TryGetValue(node.Id, out var existing))
            {
                if (existing.Kind is "repository" or "repository-root" &&
                    node.Kind is "repository" or "repository-root")
                {
                    if (existing.Kind == "repository-root" || node.Kind == "repository-root")
                        nodes[node.Id] = existing with
                        {
                            Kind = "repository-root",
                            Details = "Repository whose fluent composition is being projected."
                        };
                    return;
                }
                if (existing != node)
                    throw new InvalidOperationException($"Graph node identity collision: '{node.Id}'.");
                return;
            }
            nodes.Add(node.Id, node);
        }

        void AddEdge(string prefix, string source, string target, string kind, string label, int order = 0)
        {
            var id = StableId("edge", $"{prefix}|{source}|{target}|{kind}|{label}|{order}");
            edges.Add(new CiAiRepositoryGraphEdge(id, source, target, kind, label, order));
        }
    }

    private void Validate()
    {
        if (!RepositoryIdentity.IsValid(Repository))
            throw new ArgumentException("Graph repository must use canonical owner/repository identity.");

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || !nodeIds.Add(node.Id))
                throw new ArgumentException("Graph node IDs must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(node.Kind) || string.IsNullOrWhiteSpace(node.Label))
                throw new ArgumentException($"Graph node '{node.Id}' requires a kind and label.");
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Id) || !edgeIds.Add(edge.Id))
                throw new ArgumentException("Graph edge IDs must be non-empty and unique.");
            if (!nodeIds.Contains(edge.SourceNodeId) || !nodeIds.Contains(edge.TargetNodeId))
                throw new ArgumentException($"Graph edge '{edge.Id}' references an unknown node.");
            if (string.IsNullOrWhiteSpace(edge.Kind) || string.IsNullOrWhiteSpace(edge.Label) || edge.Order < 0)
                throw new ArgumentException($"Graph edge '{edge.Id}' has invalid metadata.");
        }
    }

    private static string RepositoryNodeId(string repository) =>
        StableId("repository", repository.ToUpperInvariant());

    private static string StableId(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return prefix + "-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }
}

/// <summary>A single declared repository-composition feature or repository endpoint.</summary>
public sealed record CiAiRepositoryGraphNode(
    string Id,
    string Kind,
    string Label,
    string Details,
    bool IsExplicit,
    string? Url);

/// <summary>A directed, labelled relationship in the repository-composition projection.</summary>
public sealed record CiAiRepositoryGraphEdge(
    string Id,
    string SourceNodeId,
    string TargetNodeId,
    string Kind,
    string Label,
    int Order);
