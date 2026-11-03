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

Console.WriteLine($"Restored and compiled CiAi package with {proposal.ProtocolVersion}.");
