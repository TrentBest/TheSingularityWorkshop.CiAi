using TheSingularityWorkshop.CiAi;

internal static class NuGetPackageArtifactVerifierContractTests
{
    public static int Run()
    {
        var passed = 0;
        Check("NuGet artifact inventory requires both package and symbol package", () =>
        {
            var report = NuGetPackageArtifactVerifier.Verify(
                "TheSingularityWorkshop.FSM_Serialization", "1.0.1",
                new[]
                {
                    "TheSingularityWorkshop.FSM_Serialization.1.0.1.nupkg",
                    "TheSingularityWorkshop.FSM_Serialization.1.0.1.snupkg"
                });
            Assert(report.IsComplete, "both expected package outputs should satisfy the inventory");
            Assert(report.ExpectedPaths.Count == 2, "inventory should require package and symbols");
        });

        Check("NuGet artifact inventory explicitly reports a missing symbol package", () =>
        {
            var report = NuGetPackageArtifactVerifier.Verify(
                "TheSingularityWorkshop.FSM_Serialization", "1.0.1",
                new[] { "TheSingularityWorkshop.FSM_Serialization.1.0.1.nupkg" });
            Assert(!report.IsComplete, "missing symbol package was accepted");
            Assert(report.MissingPaths.SequenceEqual(
                new[] { "TheSingularityWorkshop.FSM_Serialization.1.0.1.snupkg" }),
                "missing symbol package diagnostic was incorrect");
            Assert(report.ToReport().Contains("MISSING: TheSingularityWorkshop.FSM_Serialization.1.0.1.snupkg",
                StringComparison.Ordinal), "human-readable report omitted the missing symbol package");
        });

        Check("NuGet artifact inventory requires the exact candidate version", () =>
        {
            var report = NuGetPackageArtifactVerifier.Verify(
                "Example.Package", "1.2.3",
                new[] { "Example.Package.1.2.2.nupkg", "Example.Package.1.2.2.snupkg" });
            Assert(!report.IsComplete && report.MissingPaths.Count == 2,
                "outputs from a different version were incorrectly accepted");
        });

        Check("NuGet artifact inventory recognizes outputs nested in retained artifact folders", () =>
        {
            var report = NuGetPackageArtifactVerifier.Verify(
                "Example.Package", "1.2.3",
                new[] { "artifacts/release/Example.Package.1.2.3.nupkg",
                    "symbols/Example.Package.1.2.3.snupkg" });
            Assert(report.IsComplete, "nested package outputs should be found by their exact filenames");
        });

        Check("NuGet artifact inventory compares portable paths case-insensitively", () =>
        {
            var report = NuGetPackageArtifactVerifier.Verify(
                "Example.Package", "1.2.3",
                new[] { "example.package.1.2.3.NUPKG", "EXAMPLE.PACKAGE.1.2.3.SNUPKG" });
            Assert(report.IsComplete, "case-only path differences should not make an inventory platform-dependent");
        });

        Check("NuGet artifact inventory rejects rooted and traversal paths", () =>
        {
            Throws<ArgumentException>(() => NuGetPackageArtifactVerifier.Verify(
                "Example.Package", "1.2.3", new[] { "../Example.Package.1.2.3.nupkg" }));
            Throws<ArgumentException>(() => NuGetPackageArtifactVerifier.Verify(
                "Example.Package", "1.2.3", new[] { "C:/temp/Example.Package.1.2.3.nupkg" }));
        });

        Check("NuGet artifact inventory rejects unsafe package ID and version segments", () =>
        {
            Throws<ArgumentException>(() => NuGetPackageArtifactVerifier.Verify("../Example", "1.2.3", Array.Empty<string>()));
            Throws<ArgumentException>(() => NuGetPackageArtifactVerifier.Verify("Example.Package", "../1.2.3", Array.Empty<string>()));
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS NuGet artifact inventory: {name}");
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
