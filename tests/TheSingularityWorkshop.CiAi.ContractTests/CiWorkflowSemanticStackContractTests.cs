using TheSingularityWorkshop.CiAi;
using TheSingularityWorkshop.GrammarAi;
using TheSingularityWorkshop.ProtocolAi;

internal static class CiWorkflowSemanticStackContractTests
{
    public static int Run()
    {
        var passed = 0;
        var protocols = CiWorkflowProtocolCatalog.Create();
        var grammar = CiWorkflowGrammarCatalog.Create(protocols);

        Check("CiAi owns separate ProtocolAi semantic categories with stable definition identities", () =>
        {
            Assert(protocols.Definitions.Count == 9, "semantic category inventory changed");
            Assert(protocols.Definitions.Select(x => x.Id).Distinct().Count() == protocols.Definitions.Count,
                "semantic category identities are not unique");
            Assert(protocols.Definitions.Sum(x => x.Symbols.Count) >= 70,
                "expected CI vocabulary members are incomplete");
            foreach (var definition in protocols.Definitions)
                Assert(definition.Symbols.Select(x => x.Id).Distinct().Count() == definition.Symbols.Count,
                    $"symbol IDs are not unique within category '{definition.Name}'");
            Assert(protocols.Domains.Reference("Domain_build-test").SymbolId == CiWorkflowProtocolCatalog.DomainBuildTest &&
                protocols.Domains.Reference("Domain_smoke").SymbolId == CiWorkflowProtocolCatalog.DomainSmoke,
                "domain symbol identities changed with catalog ordering");
        });

        Check("ProtocolAi category identity qualifies local member IDs", () =>
        {
            var trigger = protocols.Triggers.Reference("Push");
            var domain = protocols.Domains.Reference("Domain_build-test");
            Assert(trigger.SymbolId == domain.SymbolId,
                "fixture must demonstrate colliding local member IDs across categories");
            Assert(trigger.ProtocolId != domain.ProtocolId,
                "different semantic categories share one definition identity");
            Assert(protocols.Triggers.Decode(trigger) == "push" &&
                protocols.Domains.Decode(domain) == "build-test",
                "qualified references did not resolve in their owning categories");
            Throws<ArgumentException>(() => protocols.Triggers.Decode(domain));
        });

        Check("ProtocolAi keeps known vocabulary references distinct from unregistered repository literals", () =>
        {
            var known = protocols.WorkflowFields.Encode("workflow");
            var repository = protocols.Repositories.Encode("TrentBest/TheSingularityWorkshop.CiAi");
            Assert(known.IsReference && protocols.WorkflowFields.Resolve(known) == "workflow",
                "known CI field did not encode as a category member reference");
            Assert(protocols.Repositories.Symbols.Count == 0 && repository.IsLiteral &&
                protocols.Repositories.Resolve(repository) == "TrentBest/TheSingularityWorkshop.CiAi",
                "unregistered repository identity was silently promoted to a category member");
        });

        Check("CiAi GrammarAi production grammar resolves terminals across exact semantic categories", () =>
        {
            Assert(grammar.Id == CiWorkflowGrammarCatalog.GrammarId, "grammar identity changed");
            Assert(grammar.Rules.Count == 59, "grammar production inventory changed");
            Assert(CiWorkflowGrammarCatalog.FindUnresolvedProtocolReferences(grammar, protocols).Count == 0,
                "valid CI grammar contains unresolved protocol/category references");
        });

        Check("GrammarAi reports unknown members in an otherwise known ProtocolAi category", () =>
        {
            var invalid = new GrammarBuilder(9002, "Invalid CiAi Fixture", 1)
                .Rule(1, 1, GrammarSymbol.Terminal(
                    new GrammarProtocolReference(protocols.Triggers.Id, ulong.MaxValue)))
                .Build();
            var unresolved = CiWorkflowGrammarCatalog.FindUnresolvedProtocolReferences(invalid, protocols);
            Assert(unresolved.Count == 1 && unresolved[0].ProtocolId == protocols.Triggers.Id &&
                unresolved[0].SymbolId == ulong.MaxValue,
                "unknown member was not reported against its exact category");
        });

        Check("CiAi resolver rejects foreign categories and unknown members", () =>
        {
            Assert(!CiWorkflowProtocolCatalog.Resolves(protocols, protocols.Triggers.Id + 1,
                CiWorkflowProtocolCatalog.TriggerPush), "foreign category identity was accepted");
            Assert(!CiWorkflowProtocolCatalog.Resolves(protocols, protocols.Triggers.Id, ulong.MaxValue),
                "unknown category member was accepted");
            Assert(CiWorkflowProtocolCatalog.Resolves(protocols, protocols.Triggers.Id,
                CiWorkflowProtocolCatalog.TriggerPush), "known category member was not resolved");
            Throws<ArgumentNullException>(() => CiWorkflowProtocolCatalog.Resolves(null!,
                protocols.Triggers.Id, CiWorkflowProtocolCatalog.TriggerPush));
        });

        Check("GrammarAi rejects internally invalid CI grammar fixtures", () =>
        {
            Throws<ArgumentException>(() => new GrammarBuilder(9003, "Missing Production", 1)
                .Rule(1, 1, GrammarSymbol.NonTerminal(2))
                .Build());
            Throws<ArgumentException>(() => new GrammarBuilder(9004, "Duplicate Rule", 1)
                .Rule(1, 1)
                .Rule(1, 1)
                .Build());
        });

        Check("CiAi grammar resolver validates required inputs", () =>
        {
            Throws<ArgumentNullException>(() => CiWorkflowGrammarCatalog.FindUnresolvedProtocolReferences(null!, protocols));
            Throws<ArgumentNullException>(() => CiWorkflowGrammarCatalog.FindUnresolvedProtocolReferences(grammar, null!));
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS {passed}: {name}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
