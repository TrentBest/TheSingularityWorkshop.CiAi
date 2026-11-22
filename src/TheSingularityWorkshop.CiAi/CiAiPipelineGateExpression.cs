namespace TheSingularityWorkshop.CiAi;

/// <summary>Three-valued result of evaluating a composite prerequisite expression.</summary>
public enum CiAiPipelineGateState
{
    Waiting = 0,
    Satisfied = 1,
    Blocked = 2
}

/// <summary>A bounded, immutable Boolean prerequisite tree for one named development pipeline.</summary>
public sealed record CiAiPipelineGateExpression
{
    public const int MaximumDepth = 8;
    public const int MaximumNodes = 32;

    private CiAiPipelineGateExpression(string pipeline, CiAiGateExpression root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeline);
        ArgumentNullException.ThrowIfNull(root);
        Pipeline = pipeline.Trim();
        if (Pipeline.Length > 100 || Pipeline.Any(char.IsControl))
            throw new ArgumentException("Pipeline names must be at most 100 printable characters.", nameof(pipeline));

        var leaves = new List<CiAiRepositoryGateReference>();
        var nodeCount = Validate(root, 1, leaves);
        if (nodeCount > MaximumNodes)
            throw new ArgumentException($"Gate expressions may contain at most {MaximumNodes} nodes.", nameof(root));
        if (leaves.Select(x => x.GateId).Distinct(StringComparer.Ordinal).Count() != leaves.Count)
            throw new ArgumentException("A gate expression cannot contain duplicate leaf gate identities.", nameof(root));

        Root = root;
        Leaves = Array.AsReadOnly(leaves.ToArray());
    }

    public string Pipeline { get; }
    public CiAiGateExpression Root { get; }
    public IReadOnlyList<CiAiRepositoryGateReference> Leaves { get; }

    /// <summary>Returns whether a verified leaf-gate event should trigger reevaluation of this pipeline.</summary>
    public bool DependsOnGate(string gateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        return Leaves.Any(leaf => string.Equals(leaf.GateId, gateId, StringComparison.Ordinal));
    }

    public static CiAiPipelineGateExpression ForPipeline(string pipeline, CiAiGateExpression root) =>
        new(pipeline, root);

    private static int Validate(CiAiGateExpression expression, int depth, ICollection<CiAiRepositoryGateReference> leaves)
    {
        if (depth > MaximumDepth)
            throw new ArgumentException($"Gate expressions may not exceed depth {MaximumDepth}.");

        switch (expression)
        {
            case CiAiRepositoryGateReference leaf:
                leaves.Add(leaf);
                return 1;
            case CiAiAllOf all:
                return 1 + all.Children.Sum(child => Validate(child, depth + 1, leaves));
            case CiAiAnyOf any:
                return 1 + any.Children.Sum(child => Validate(child, depth + 1, leaves));
            default:
                throw new ArgumentException("Unsupported gate expression node.", nameof(expression));
        }
    }
}

/// <summary>Base type for immutable AND/OR prerequisite nodes.</summary>
public abstract record CiAiGateExpression;

/// <summary>A leaf tied to one repository, candidate ID, and exact source commit.</summary>
public sealed record CiAiRepositoryGateReference : CiAiGateExpression
{
    public CiAiRepositoryGateReference(string gateId, string repository, string candidateId, string commitSha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        if (!RepositoryIdentity.IsValid(repository))
            throw new ArgumentException("Repository must use canonical owner/repository identity.", nameof(repository));
        if (commitSha is null || commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A full 40-character source commit SHA is required.", nameof(commitSha));

        var normalizedGateId = gateId.Trim();
        var normalizedCandidateId = candidateId.Trim();
        if (normalizedGateId.Length > 128 || normalizedGateId.Any(char.IsControl))
            throw new ArgumentException("Gate IDs must be at most 128 printable characters.", nameof(gateId));
        if (normalizedCandidateId.Length > 256 || normalizedCandidateId.Any(char.IsControl))
            throw new ArgumentException("Candidate IDs must be at most 256 printable characters.", nameof(candidateId));

        GateId = normalizedGateId;
        Repository = repository;
        CandidateId = normalizedCandidateId;
        CommitSha = commitSha.ToLowerInvariant();
    }

    public string GateId { get; }
    public string Repository { get; }
    public string CandidateId { get; }
    public string CommitSha { get; }
}

/// <summary>All child gates must be satisfied.</summary>
public sealed record CiAiAllOf : CiAiGateExpression
{
    public CiAiAllOf(IEnumerable<CiAiGateExpression> children) =>
        Children = CopyChildren(children, nameof(children));

    public IReadOnlyList<CiAiGateExpression> Children { get; }

    internal static IReadOnlyList<CiAiGateExpression> CopyChildren(
        IEnumerable<CiAiGateExpression> children, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(children);
        var copy = children.ToArray();
        if (copy.Length < 2)
            throw new ArgumentException("AND/OR expressions require at least two children.", parameterName);
        if (copy.Any(child => child is null))
            throw new ArgumentException("Gate expressions cannot contain null children.", parameterName);
        return Array.AsReadOnly(copy);
    }
}

/// <summary>At least one child gate must be satisfied.</summary>
public sealed record CiAiAnyOf : CiAiGateExpression
{
    public CiAiAnyOf(IEnumerable<CiAiGateExpression> children) =>
        Children = CiAiAllOf.CopyChildren(children, nameof(children));

    public IReadOnlyList<CiAiGateExpression> Children { get; }
}

/// <summary>Evaluates composite gates using only previously verified leaf evaluations.</summary>
public static class CiAiPipelineGateEvaluator
{
    public static CiAiPipelineGateState Evaluate(
        CiAiPipelineGateExpression expression,
        IReadOnlyDictionary<string, DependencyReleaseGateEvaluation> leafEvaluations)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(leafEvaluations);
        return EvaluateNode(expression.Root, leafEvaluations);
    }

    private static CiAiPipelineGateState EvaluateNode(
        CiAiGateExpression node,
        IReadOnlyDictionary<string, DependencyReleaseGateEvaluation> evaluations)
    {
        switch (node)
        {
            case CiAiRepositoryGateReference leaf:
                if (!evaluations.TryGetValue(leaf.GateId, out var evaluation))
                    return CiAiPipelineGateState.Waiting;
                if (!string.Equals(evaluation.Repository, leaf.Repository, StringComparison.Ordinal) ||
                    !string.Equals(evaluation.CandidateId, leaf.CandidateId, StringComparison.Ordinal) ||
                    !string.Equals(evaluation.CommitSha, leaf.CommitSha, StringComparison.OrdinalIgnoreCase))
                    return CiAiPipelineGateState.Blocked;
                return evaluation.CanVerifyConsumer
                    ? CiAiPipelineGateState.Satisfied
                    : evaluation.State == DependencyReleaseGateState.WaitingForPublishedDependencies
                        ? CiAiPipelineGateState.Waiting
                        : CiAiPipelineGateState.Blocked;

            case CiAiAllOf all:
            {
                var states = all.Children.Select(child => EvaluateNode(child, evaluations)).ToArray();
                if (states.Contains(CiAiPipelineGateState.Blocked)) return CiAiPipelineGateState.Blocked;
                return states.All(state => state == CiAiPipelineGateState.Satisfied)
                    ? CiAiPipelineGateState.Satisfied
                    : CiAiPipelineGateState.Waiting;
            }

            case CiAiAnyOf any:
            {
                var states = any.Children.Select(child => EvaluateNode(child, evaluations)).ToArray();
                if (states.Contains(CiAiPipelineGateState.Satisfied)) return CiAiPipelineGateState.Satisfied;
                return states.All(state => state == CiAiPipelineGateState.Blocked)
                    ? CiAiPipelineGateState.Blocked
                    : CiAiPipelineGateState.Waiting;
            }

            default:
                return CiAiPipelineGateState.Blocked;
        }
    }
}


/// <summary>LLM allocation disposition derived from a composite prerequisite result.</summary>
public enum CiAiPipelineGateAdmissionState
{
    DormantWaiting = 0,
    DeniedBlocked = 1,
    Eligible = 2
}

/// <summary>A deterministic admission decision; it does not itself dispatch or schedule an LLM call.</summary>
public sealed record CiAiPipelineGateAdmission(CiAiPipelineGateAdmissionState State, string Reason)
{
    public bool MayAllocateLlm => State == CiAiPipelineGateAdmissionState.Eligible;
}

/// <summary>Prevents a repository with unresolved prerequisites from consuming LLM allocation.</summary>
public static class CiAiPipelineGateAdmissionPolicy
{
    public static CiAiPipelineGateAdmission Evaluate(CiAiPipelineGateState gateState) => gateState switch
    {
        CiAiPipelineGateState.Satisfied =>
            new(CiAiPipelineGateAdmissionState.Eligible, "pipeline.gates-satisfied"),
        CiAiPipelineGateState.Waiting =>
            new(CiAiPipelineGateAdmissionState.DormantWaiting, "pipeline.gates-waiting"),
        CiAiPipelineGateState.Blocked =>
            new(CiAiPipelineGateAdmissionState.DeniedBlocked, "pipeline.gates-blocked"),
        _ => new(CiAiPipelineGateAdmissionState.DeniedBlocked, "pipeline.gates-state-unknown")
    };
}
