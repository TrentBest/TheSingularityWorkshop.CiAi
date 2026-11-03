using TheSingularityWorkshop.CiAi.RepositoryCommands;
using TheSingularityWorkshop.CommandAi;

internal static class AiCiBuilderContractTests
{
    private static int _passed;

    public static int Run()
    {
        _passed = 0;
        Check("builder emits all six repository operation identities using CommandAi/1", () =>
        {
            var proposal = NewBuilder()
                .CreateProject("Minesweeper")
                .AddScript("Game/MinesweeperGame.cs", "public sealed class MinesweeperGame {}")
                .ModifyScript("Game/Board.cs", new string('a', 64), "replacement")
                .AddDependency("FSM_API", "1.0.13")
                .RequestBuild()
                .RequestTests()
                .Build();
            Assert(proposal.ProtocolVersion == CommandProposal.CurrentProtocolVersion, "wrong CommandAi protocol");
            Assert(proposal.ProtocolVersion == "CommandAi/1", "proposal did not use CommandAi/1");
            Assert(proposal.Commands.Select(c => c.ActionId).SequenceEqual(new ulong[] {
                AiCiCommandCatalog.CreateProject, AiCiCommandCatalog.AddProjectFile,
                AiCiCommandCatalog.ModifyProjectFile, AiCiCommandCatalog.AddDependency,
                AiCiCommandCatalog.RequestBuild, AiCiCommandCatalog.RequestTests }),
                "command order or identities changed");
            Assert(proposal.Commands.All(c => c.Arguments.Count >= 3), "repository context arguments missing");
        });

        Check("equivalent builder calls encode deterministically and round-trip through CommandAi", () =>
        {
            var first = NewBuilder().CreateProject("Game").AddScript("Game/Main.cs", "class Main {}").Build();
            var second = NewBuilder().CreateProject("Game").AddScript("Game/Main.cs", "class Main {}").Build();
            var encoded = CommandAiTextCodec.Encode(first);
            Assert(encoded == CommandAiTextCodec.Encode(second), "equivalent proposals were not deterministic");
            var decoded = CommandAiTextCodec.Decode(encoded);
            Assert(CommandAiTextCodec.Encode(decoded) == encoded, "CommandAi round-trip changed the proposal");
        });

        Check("every command carries repository feature branch and exact base SHA as typed arguments", () =>
        {
            var proposal = NewBuilder().CreateProject("Game").RequestBuild().Build();
            foreach (var command in proposal.Commands)
            {
                Assert(Text(command, AiCiCommandCatalog.Repository) == "TrentBest/Minesweeper", "repository argument missing");
                Assert(Text(command, AiCiCommandCatalog.FeatureBranch) == "feature/repair-board", "feature branch argument missing");
                Assert(Text(command, AiCiCommandCatalog.BaseSha) == new string('a', 40), "base SHA argument missing");
            }
        });

        Check("builder rejects canonical and malformed Git branch refs", () =>
        {
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "development", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "master", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "feature/x..y", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "feature/.hidden", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "feature/x.", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "feature/x.lock", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "feature//x", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/Game", "feature/x", "abc"));
            Throws<ArgumentException>(() => AiCi.ForRepository("../Game", "feature/x", new string('a', 40)));
            Throws<ArgumentException>(() => AiCi.ForRepository("TrentBest/.", "feature/x", new string('a', 40)));
        });

        Check("builder rejects path traversal, Git metadata, and rooted paths", () =>
        {
            Throws<ArgumentException>(() => NewBuilder().AddProjectFile("../outside.cs", "class X {}"));
            Throws<ArgumentException>(() => NewBuilder().AddProjectFile("/rooted.cs", "class X {}"));
            Throws<ArgumentException>(() => NewBuilder().AddProjectFile("Game\\escape.cs", "class X {}"));
            Throws<ArgumentException>(() => NewBuilder().AddProjectFile(".git/config", "unsafe"));
            Throws<ArgumentException>(() => NewBuilder().AddProjectFile("Game/.GIT/config", "unsafe"));
        });

        Check("modify operation requires an expected source digest", () =>
        {
            Throws<ArgumentException>(() => NewBuilder().ModifyScript("Game/Board.cs", "", "patch"));
            Throws<ArgumentException>(() => NewBuilder().ModifyScript("Game/Board.cs", "stale", "patch"));
        });

        Check("builder enforces CommandAi literal payload limits", () =>
        {
            Throws<ArgumentException>(() => NewBuilder().AddScript("Game/Large.cs", new string('x', CommandLimits.MaxLiteralCharacters + 1)));
            Throws<ArgumentException>(() => NewBuilder().CreateProject(" "));
        });

        Check("builder inspection and encoding are proposal-only and do not execute", () =>
        {
            var proposal = NewBuilder().CreateProject("NoSideEffects").Build();
            var first = CommandAiTextCodec.Encode(proposal);
            var second = CommandAiTextCodec.Encode(proposal);
            Assert(first == second && proposal.Commands.Count == 1, "inspection mutated or executed the proposal");
        });

        return _passed;
    }

    private static AiCiRepositoryBuilder NewBuilder()
        => AiCi.ForRepository("TrentBest/Minesweeper", "feature/repair-board", new string('a', 40));

    private static string Text(CommandInstruction command, ulong parameterId)
        => command.Arguments.Single(argument => argument.ParameterId == parameterId).Value.Text!;

    private static void Check(string name, Action action)
    {
        try { action(); _passed++; }
        catch (Exception ex) { throw new InvalidOperationException($"AiCi contract failed: {name}", ex); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
