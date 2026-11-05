using TheSingularityWorkshop.CiAi;
using TheSingularityWorkshop.CiAi.RepositoryCommands;
using TheSingularityWorkshop.CommandAi;

var proposal = AiCi.ForRepository(
        "Workshop/ConsumerSmoke",
        "feature/package-consumer-smoke",
        new string('a', 40))
    .RequestBuild()
    .Build();

if (proposal.ProtocolVersion != CommandProposal.CurrentProtocolVersion)
    throw new InvalidOperationException("The transitive CommandAi contract was not resolved.");

var protocol = CiWorkflowProtocolCatalog.Create();
var grammar = CiWorkflowGrammarCatalog.Create(protocol);
if (CiWorkflowGrammarCatalog.FindUnresolvedProtocolReferences(grammar, protocol).Count != 0)
    throw new InvalidOperationException("The packaged CiAi semantic grammar has unresolved protocol references.");

Console.WriteLine($"Restored and compiled CiAi package with {proposal.ProtocolVersion} and {protocol.Definitions.Sum(category => category.Symbols.Count)} CI vocabulary symbols.");
