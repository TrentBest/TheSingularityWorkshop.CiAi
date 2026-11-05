using TheSingularityWorkshop.CiAi;
using TheSingularityWorkshop.GrammarAi;
using TheSingularityWorkshop.ProtocolAi;

internal static class CiWorkflowSemanticStackContractTests
{
    public static int Run()
    {
        var passed = 0;
        var protocol = CiWorkflowProtocolCatalog.Create();
        var grammar = CiWorkflowGrammarCatalog.Create(protocol);

        Check("CiAi ProtocolAi vocabulary has stable unique identities and values", () =>
        {
            Assert(protocol.Id == CiWorkflowProtocolCatalog.ProtocolId, "protocol identity changed");
            Assert(protocol.Symbols.Count >= 50, "expected CI workflow, domain, action, and outcome vocabulary is incomplete");
            Assert(protocol.Symbols.Select(x => x.Id).Distinct().Count() == protocol.Symbols.Count,
                "protocol symbol IDs are not unique");
            Assert(protocol.Symbols.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == protocol.Symbols.Count,
                "protocol symbol names are not unique");
            Assert(protocol.Symbols.Select(x => x.Value).Distinct(StringComparer.Ordinal).Count() == protocol.Symbols.Count,
                "protocol symbol values are not unique");
            Assert(protocol.Reference("Domain_build-test").SymbolId == CiWorkflowProtocolCatalog.DomainBuildTest &&
                protocol.Reference("Domain_smoke").SymbolId == CiWorkflowProtocolCatalog.DomainSmoke,
                "domain symbol identities changed with catalog ordering");
        });

        Check("ProtocolAi distinguishes known CI vocabulary from application literals", () =>
        {
            var known = protocol.Encode("workflow");
            var literal = protocol.Encode("TrentBest/TheSingularityWorkshop.CiAi");
            Assert(known.IsReference && protocol.Resolve(known) == "workflow",
                "known CI vocabulary did not encode as a symbol reference");
            Assert(literal.IsLiteral && protocol.Resolve(literal) == "TrentBest/TheSingularityWorkshop.CiAi",
                "repository identity did not remain an application literal");
        });

        Check("CiAi GrammarAi recipe grammar links every protocol terminal through the host resolver", () =>
        {
            Assert(grammar.Id == CiWorkflowGrammarCatalog.GrammarId, "grammar identity changed");
            Assert(grammar.Rules.Count == 43, "grammar production inventory changed");
            Assert(CiWorkflowGrammarCatalog.FindUnresolvedProtocolReferences(grammar, protocol).Count == 0,
                "valid CI grammar contains unresolved protocol references");
        });

        Check("GrammarAi reports protocol-qualified references missing from the host vocabulary", () =>
        {
            var invalid = new GrammarBuilder(9002, "Invalid CiAi Fixture", 1)
                .Rule(1, 1, GrammarSymbol.Terminal(new GrammarProtocolReference(protocol.Id, ulong.MaxValue)))
                .Build();
            var unresolved = CiWorkflowGrammarCatalog.FindUnresolvedProtocolReferences(invalid, protocol);
            Assert(unresolved.Count == 1 && unresolved[0].SymbolId == ulong.MaxValue,
                "unknown protocol symbol was not reported");
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
