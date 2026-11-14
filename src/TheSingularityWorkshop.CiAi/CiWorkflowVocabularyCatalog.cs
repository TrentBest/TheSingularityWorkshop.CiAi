using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.GrammarAi;
using TheSingularityWorkshop.ProtocolAi;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Immutable per-request exposure snapshot for CiAi's full ProtocolAi vocabulary registry.</summary>
public sealed class CiWorkflowVocabularySnapshot : IGrammarExternalReferenceResolver
{
    private readonly IReadOnlyDictionary<ulong, ProtocolDefinition> _definitions;
    private readonly IReadOnlyDictionary<ulong, IReadOnlySet<ulong>> _exposed;

    internal CiWorkflowVocabularySnapshot(IReadOnlyDictionary<ulong, ProtocolDefinition> definitions,
        IReadOnlyDictionary<ulong, IReadOnlySet<ulong>> exposed)
    {
        _definitions = definitions; _exposed = exposed;
        var canonicalLines = new List<string> { "protocol-source:" + CiWorkflowGrammarMap.ProtocolAiRevision };
        foreach (var pair in exposed.OrderBy(p => p.Key))
        {
            var definition = definitions[pair.Key];
            canonicalLines.Add($"category:{definition.Id}:{definition.Name}");
            foreach (var symbol in definition.Symbols.Where(s => pair.Value.Contains(s.Id)).OrderBy(s => s.Id))
                canonicalLines.Add($"member:{symbol.Id}:{symbol.Name}:{symbol.Value}");
        }
        Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonicalLines)))).ToLowerInvariant();
    }

    public string Digest { get; }

    /// <summary>Returns only categories and members exposed in this immutable request snapshot.</summary>
    public string Describe()
    {
        var lines = new List<string> { "snapshot:" + Digest };
        foreach (var pair in _exposed.OrderBy(p => p.Key))
        {
            var definition = _definitions[pair.Key];
            lines.Add($"[{definition.Id}] {definition.Name}");
            foreach (var symbol in definition.Symbols.Where(s => pair.Value.Contains(s.Id)).OrderBy(s => s.Id))
                lines.Add($"  [{symbol.Id}] {symbol.Name} = \"{symbol.Value}\"");
        }
        return string.Join(Environment.NewLine, lines);
    }

    public GrammarExternalReferenceStatus Resolve(GrammarProtocolReference reference)
    {
        if (!_definitions.TryGetValue(reference.ProtocolId, out var definition)) return GrammarExternalReferenceStatus.UnknownProtocol;
        if (!_exposed.TryGetValue(reference.ProtocolId, out var members)) return GrammarExternalReferenceStatus.NotExposed;
        if (!definition.Symbols.Any(s => s.Id == reference.SymbolId)) return GrammarExternalReferenceStatus.UnknownSymbol;
        return members.Contains(reference.SymbolId) ? GrammarExternalReferenceStatus.ResolvedAndExposed : GrammarExternalReferenceStatus.NotExposed;
    }
}

/// <summary>CiAi-owned stable CI vocabularies built with ProtocolAi identity primitives.</summary>
public static class CiWorkflowVocabularyCatalog
{
    public const ulong TriggerCategoryId = 0x4349414900000001UL;
    public const ulong ActionCategoryId = 0x4349414900000002UL;
    public const ulong InputNameCategoryId = 0x4349414900000003UL;
    public const ulong DataTypeCategoryId = 0x4349414900000004UL;
    public const ulong OutputNameCategoryId = 0x4349414900000005UL;
    public const ulong CapabilityCategoryId = 0x4349414900000006UL;
    public const ulong SideEffectCategoryId = 0x4349414900000007UL;
    public const ulong EvidenceRoleCategoryId = 0x4349414900000008UL;
    public const ulong OutcomeRoleCategoryId = 0x4349414900000009UL;
    public const ulong StepConditionCategoryId = 0x434941490000000AUL;

    private static readonly IReadOnlyDictionary<ulong, ProtocolDefinition> DefinitionsValue = new Dictionary<ulong, ProtocolDefinition>
    {
        [TriggerCategoryId] = Build(TriggerCategoryId, "CiAi.Trigger", (1,"push"),(2,"pull_request"),(3,"workflow_dispatch"),(4,"schedule"),(5,"release")),
        [ActionCategoryId] = Build(ActionCategoryId, "CiAi.StepAction", (1,"restore"),(2,"build"),(3,"test"),(4,"pack"),(5,"benchmark"),(6,"observe")),
        [InputNameCategoryId] = Build(InputNameCategoryId, "CiAi.InputName", (1,"repository"),(2,"source_sha"),(3,"configuration"),(4,"build_artifact")),
        [DataTypeCategoryId] = Build(DataTypeCategoryId, "CiAi.DataType", (1,"string"),(2,"commit_sha"),(3,"boolean"),(4,"artifact")),
        [OutputNameCategoryId] = Build(OutputNameCategoryId, "CiAi.OutputName", (1,"build_artifact"),(2,"test_results"),(3,"package"),(4,"evidence_report")),
        [CapabilityCategoryId] = Build(CapabilityCategoryId, "CiAi.Capability", (1,"read_source"),(2,"execute_build"),(3,"execute_tests"),(4,"create_package"),(5,"observe_provider")),
        [SideEffectCategoryId] = Build(SideEffectCategoryId, "CiAi.SideEffect", (1,"read_files"),(2,"write_artifacts"),(3,"spawn_process"),(4,"network_read")),
        [EvidenceRoleCategoryId] = Build(EvidenceRoleCategoryId, "CiAi.EvidenceRole", (1,"build_log"),(2,"test_results"),(3,"package_metadata"),(4,"provenance")),
        [OutcomeRoleCategoryId] = Build(OutcomeRoleCategoryId, "CiAi.OutcomeRole", (1,"build_succeeded"),(2,"tests_passed"),(3,"package_created"),(4,"evidence_complete")),
        [StepConditionCategoryId] = Build(StepConditionCategoryId, "CiAi.StepCondition", (1,"all_dependencies_succeeded"),(2,"always"),(3,"any_dependency_failed"),(4,"all_dependencies_completed"))
    };

    public static IReadOnlyCollection<ProtocolDefinition> Definitions => DefinitionsValue.Values.ToArray();

    public static CiWorkflowVocabularySnapshot CreateSnapshot(IEnumerable<ulong>? exposedCategories = null,
        IReadOnlyDictionary<ulong, IReadOnlyCollection<ulong>>? exposedMembers = null)
    {
        var categories = exposedCategories?.Distinct().ToHashSet() ?? DefinitionsValue.Keys.ToHashSet();
        if (categories.Any(id => !DefinitionsValue.ContainsKey(id)))
            throw new ArgumentException("Exposure snapshot names an unknown ProtocolAi category.", nameof(exposedCategories));
        var exposed = new Dictionary<ulong, IReadOnlySet<ulong>>();
        foreach (var id in categories)
        {
            var definition = DefinitionsValue[id];
            var members = exposedMembers is not null && exposedMembers.TryGetValue(id, out var selected)
                ? selected.ToHashSet() : definition.Symbols.Select(s => s.Id).ToHashSet();
            if (members.Any(member => definition.Symbols.All(s => s.Id != member)))
                throw new ArgumentException("Exposure snapshot names an unknown ProtocolAi member.", nameof(exposedMembers));
            exposed.Add(id, members);
        }
        if (exposedMembers is not null && exposedMembers.Keys.Any(id => !categories.Contains(id)))
            throw new ArgumentException("Member exposure cannot target an excluded category.", nameof(exposedMembers));
        return new CiWorkflowVocabularySnapshot(DefinitionsValue, exposed);
    }

    internal static GrammarProtocolReference Reference(CiWorkflowVocabularySnapshot snapshot, ulong categoryId, string value)
    {
        if (!DefinitionsValue.TryGetValue(categoryId, out var definition)) throw new ArgumentOutOfRangeException(nameof(categoryId));
        var reference = definition.ReferenceByValue(value);
        var result = new GrammarProtocolReference(reference.ProtocolId, reference.SymbolId);
        var status = snapshot.Resolve(result);
        if (status != GrammarExternalReferenceStatus.ResolvedAndExposed)
            throw new ArgumentException($"CI vocabulary value '{value}' is not exposed in the active snapshot ({status}).");
        return result;
    }

    private static ProtocolDefinition Build(ulong id, string name, params (ulong Id, string Value)[] symbols)
    {
        var builder = new ProtocolBuilder(id, name);
        foreach (var symbol in symbols) builder.Define(symbol.Id, symbol.Value, symbol.Value);
        return builder.Build();
    }
}
