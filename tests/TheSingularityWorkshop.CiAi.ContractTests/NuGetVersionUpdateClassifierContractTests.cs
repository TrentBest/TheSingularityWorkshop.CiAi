using TheSingularityWorkshop.CiAi;

internal static class NuGetVersionUpdateClassifierContractTests
{
    public static int Run()
    {
        var passed = 0;
        Check("NuGet version classifier derives patch minor and major lanes", () =>
        {
            Assert(NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3", "1.2.4").Scope == DependencyUpdateScope.Patch,
                "patch lane not derived");
            Assert(NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3", "1.3.0").Scope == DependencyUpdateScope.Minor,
                "minor lane not derived");
            Assert(NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3", "2.0.0").Scope == DependencyUpdateScope.Major,
                "major lane not derived");
        });
        Check("NuGet version classifier compares numeric versions rather than strings", () =>
        {
            Assert(NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.9.0", "1.10.0").IsNewerVersion,
                "1.10.0 should be newer than 1.9.0");
            Assert(!NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "2.0.0", "1.99.0").IsNewerVersion,
                "older candidate incorrectly treated as newer");
        });
        Check("NuGet version classifier applies prerelease precedence and policy facts", () =>
        {
            var candidate = NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3-beta.2", "1.2.3-beta.10");
            Assert(candidate.IsNewerVersion && candidate.IsPrerelease,
                "numeric prerelease identifiers were not ordered correctly");
            Assert(candidate.Scope == DependencyUpdateScope.Patch,
                "prerelease advancement did not remain in the patch lane");
            Assert(!NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3-rc.1", "1.2.3").IsPrerelease,
                "stable candidate was classified as prerelease");
        });
        Check("NuGet version classifier ignores build metadata for precedence", () =>
        {
            var candidate = NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3+build.1", "1.2.3+build.2");
            Assert(!candidate.IsNewerVersion && candidate.Scope == DependencyUpdateScope.Unknown,
                "build metadata was treated as version precedence");
        });
        Check("NuGet version classifier follows NuGet normalization semantics", () =>
        {
            var equivalent = NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2", "1.2.0");
            Assert(!equivalent.IsNewerVersion && equivalent.Scope == DependencyUpdateScope.Unknown,
                "omitted zero components were incorrectly treated as an update");

            var revision = NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3.1", "1.2.3.2");
            Assert(revision.IsNewerVersion && revision.Scope == DependencyUpdateScope.Unknown,
                "revision-only changes should be ordered but not guessed into a policy lane");
        });
        Check("NuGet version classifier fails closed on malformed versions", () =>
        {
            Throws<ArgumentException>(() => NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1..3", "1.2.4"));
            Throws<ArgumentException>(() => NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3", " 1.2.4"));
            Throws<ArgumentException>(() => NuGetVersionUpdateClassifier.CreateCandidate("Owner.Lib", "1.2.3", "1.2.4-"));
            Throws<ArgumentException>(() => NuGetVersionUpdateClassifier.CreateCandidate(
                "Owner.Lib", "1.2.3", "999999999999999999999999999999.0.0"));
            Throws<ArgumentException>(() => NuGetVersionUpdateClassifier.CreateCandidate(" ", "1.2.3", "1.2.4"));
        });
        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS NuGet version classifier {passed}: {name}");
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
