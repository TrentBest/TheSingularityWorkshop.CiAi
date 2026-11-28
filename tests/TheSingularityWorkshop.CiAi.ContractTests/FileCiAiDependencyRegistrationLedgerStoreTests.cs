using TheSingularityWorkshop.CiAi;

internal static class FileCiAiDependencyRegistrationLedgerStoreTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-dependency-ledger-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileCiAiDependencyRegistrationLedgerStore(root);
            var first = CreateSnapshot("0123456789abcdef0123456789abcdef01234567", "TrentBest/Provider");
            var applied = store.ApplyVerifiedSnapshot(first, 2);
            Assert(applied.Status == CiAiDependencyRegistrationApplyStatus.Applied, "initial snapshot was not applied");
            Assert(store.ApplyVerifiedSnapshot(first, 2).Status == CiAiDependencyRegistrationApplyStatus.Duplicate,
                "identical replay was not idempotent");

            var reopened = new FileCiAiDependencyRegistrationLedgerStore(root);
            var loaded = reopened.Load("TrentBest/Consumer");
            Assert(loaded is not null && loaded.SourceRevision == 2 &&
                loaded.Registrations.Single().TargetRepository == "TrentBest/Provider",
                "ledger did not survive store reconstruction");

            var stale = CreateSnapshot("1123456789abcdef0123456789abcdef01234567", "TrentBest/Stale");
            Assert(reopened.ApplyVerifiedSnapshot(stale, 1).Status == CiAiDependencyRegistrationApplyStatus.Stale,
                "older positive revision was not rejected");
            AssertThrows<ArgumentOutOfRangeException>(() => reopened.ApplyVerifiedSnapshot(stale, 0));

            var conflict = CreateSnapshot("2123456789abcdef0123456789abcdef01234567", "TrentBest/Conflict");
            Assert(reopened.ApplyVerifiedSnapshot(conflict, 2).Status == CiAiDependencyRegistrationApplyStatus.Conflict,
                "same-revision changed snapshot was not rejected");

            var replacement = CreateSnapshot("3123456789abcdef0123456789abcdef01234567");
            Assert(reopened.ApplyVerifiedSnapshot(replacement, 3).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "newer complete empty snapshot was not applied");
            Assert(reopened.Load("TrentBest/Consumer")!.Registrations.Count == 0,
                "newer empty snapshot did not withdraw prior registrations");

            var other = CreateSnapshot("4123456789abcdef0123456789abcdef01234567", source: "TrentBest/Other");
            Assert(reopened.ApplyVerifiedSnapshot(other, 1).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "independent source ledger was not stored");
            Assert(reopened.ReadAll().Count == 2, "source ledgers were not enumerated independently");

            var corrupt = Directory.GetFiles(root, "*.json").OrderBy(path => path, StringComparer.Ordinal).First();
            File.AppendAllText(corrupt, " ");
            AssertThrows<InvalidDataException>(() => reopened.ReadAll());

            Console.WriteLine("PASS: 9 durable dependency ledger store contract checks.");
            return 9;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiAiDependencyRegistrationSnapshot CreateSnapshot(
        string sha, string? target = null, string source = "TrentBest/Consumer")
    {
        var builder = CiAi.ForRepository(source);
        if (target is not null) builder = builder.WithDependency(target);
        return CiAiDependencyRegistrationSnapshot.FromDefinition(builder.Build(), sha);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }
}
