using TheSingularityWorkshop.CiAi.RepositoryCommands;
using TheSingularityWorkshop.CommandAi;

internal static class AiCiBuilderContractTests
{
    private static int _passed;

    public static int Run()
    {
        _passed = 0;
        Check("builder emits all six repository operation identities as enriched CommandAi/1 commands", () =>
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
            Assert(proposal.Commands.All(c => c.HasAtomicContract), "a command omitted enriched atomic metadata");
            Assert(proposal.Commands.Select(c => c.CommandId).Distinct().Count() == proposal.Commands.Count,
                "command identities are not unique");
            Assert(proposal.Commands.Select(c => c.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() == proposal.Commands.Count,
                "idempotency identities are not unique");
        });

        Check("equivalent builder calls derive deterministic command and idempotency identities", () =>
        {
            var first = NewBuilder().CreateProject("Game").AddScript("Game/Main.cs", "class Main {}").Build();
            var second = NewBuilder().CreateProject("Game").AddScript("Game/Main.cs", "class Main {}").Build();
            var encoded = CommandAiTextCodec.Encode(first);
            Assert(encoded == CommandAiTextCodec.Encode(second), "equivalent proposals were not deterministic");
            Assert(first.Commands.Select(c => c.CommandId).SequenceEqual(second.Commands.Select(c => c.CommandId)),
                "command identities changed for equivalent proposals");
            Assert(first.Commands.Select(c => c.IdempotencyKey).SequenceEqual(second.Commands.Select(c => c.IdempotencyKey)),
                "idempotency identities changed for equivalent proposals");
            var decoded = CommandAiTextCodec.Decode(encoded);
            Assert(CommandAiTextCodec.Encode(decoded) == encoded, "CommandAi round-trip changed the proposal");
            Assert(encoded.Contains("AtomicCommand=", StringComparison.Ordinal), "builder did not emit enriched atomic entries");
        });

        Check("repository identity and isolated branch/base SHA are represented in target and scope", () =>
        {
            var proposal = NewBuilder().CreateProject("Game").RequestBuild().Build();
            foreach (var command in proposal.Commands)
            {
                Assert(command.Target?.Text == "TrentBest/Minesweeper", "repository target missing");
                Assert(command.Scope?.Text == "branch:feature/repair-board;base:" + new string('a', 40),
                    "feature branch or exact base SHA missing from scope");
                Assert(!command.Arguments.Any(argument => argument.ParameterId is
                    AiCiCommandCatalog.Repository or AiCiCommandCatalog.FeatureBranch or AiCiCommandCatalog.BaseSha),
                    "repository context was duplicated as generic arguments");
            }
        });

        Check("operation-specific path, payload, digest, and version remain typed arguments", () =>
        {
            var proposal = NewBuilder()
                .AddScript("Game/Main.cs", "class Main {}")
                .ModifyScript("Game/Board.cs", new string('b', 64), "patch")
                .AddDependency("FSM_API", "1.0.13")
                .Build();

            Assert(Text(proposal.Commands[0], AiCiCommandCatalog.NameOrPath) == "Game/Main.cs", "add-file path missing");
            Assert(Text(proposal.Commands[0], AiCiCommandCatalog.ContentOrExpectedSha) == "class Main {}", "add-file payload missing");
            Assert(Text(proposal.Commands[1], AiCiCommandCatalog.NameOrPath) == "Game/Board.cs", "modify-file path missing");
            Assert(Text(proposal.Commands[1], AiCiCommandCatalog.ContentOrExpectedSha) == new string('b', 64), "expected digest missing");
            Assert(Text(proposal.Commands[1], AiCiCommandCatalog.PatchOrVersion) == "patch", "patch missing");
            Assert(Text(proposal.Commands[2], AiCiCommandCatalog.NameOrPath) == "FSM_API", "package identity missing");
            Assert(Text(proposal.Commands[2], AiCiCommandCatalog.ContentOrExpectedSha) == "1.0.13", "package version missing");
        });

        Check("every command declares the same bounded expected repository-operation outcome shape", () =>
        {
            var proposal = NewBuilder().CreateProject("Game").RequestBuild().Build();
            foreach (var command in proposal.Commands)
            {
                Assert(command.OutcomeContract?.OutcomeId == AiCiCommandCatalog.RepositoryOperationOutcome,
                    "expected outcome identity missing");
                Assert(command.OutcomeContract!.Fields.Select(field => field.FieldId).SequenceEqual(new ulong[] {
                    AiCiCommandCatalog.OutcomeStatus,
                    AiCiCommandCatalog.OutcomeDetail,
                    AiCiCommandCatalog.ResultCommitSha }),
                    "expected outcome fields changed");
                Assert(command.OutcomeContract.Fields[0].Required, "outcome status must be required");
                Assert(command.OutcomeContract.Fields.Skip(1).All(field => !field.Required),
                    "outcome detail and result SHA must remain optional");
                Assert(command.Preconditions.Count == 0,
                    "builder must not invent a grammar-versioned precondition without a registered grammar");
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

        Check("Build returns an immutable proposal snapshot when the builder continues", () =>
        {
            var builder = NewBuilder().CreateProject("Initial");
            var firstSnapshot = builder.Build();
            builder.RequestBuild();
            var secondSnapshot = builder.Build();

            Assert(firstSnapshot.Commands.Count == 1,
                "a later builder operation mutated an earlier proposal snapshot");
            Assert(secondSnapshot.Commands.Count == 2,
                "the later proposal did not include the newly appended operation");
            Assert(firstSnapshot.Commands[0].ActionId == AiCiCommandCatalog.CreateProject &&
                secondSnapshot.Commands[1].ActionId == AiCiCommandCatalog.RequestBuild,
                "proposal snapshots did not preserve their respective command order");
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
