using TheSingularityWorkshop.CiAi;

internal static class FileDependencyReleaseCandidateStoreContractTests
{
    public static int Run()
    {
        var passed = 0;
        var root = Path.Combine(Path.GetTempPath(), "ciai-candidate-store-tests-" + Guid.NewGuid().ToString("N"));
        var candidate = new DependencyReleaseCandidate(
            "TrentBest/TheSingularityWorkshop.FSM_Serialization", new string('a', 40),
            "TheSingularityWorkshop.FSM_Serialization", "1.0.1", "serialization-1.0.1-candidate",
            new[]
            {
                new RequiredPublishedDependency("TrentBest/TheSingularityWorkshop.FSM_API", new string('b', 40),
                    "TheSingularityWorkshop.FSM_API", "2.0.0")
            });

        try
        {
            var store = new FileDependencyReleaseCandidateStore(root);

            Check("persists and reloads exact candidate identity and dependencies", () =>
            {
                Assert(store.Save(candidate) == DependencyCandidateStoreOutcome.Saved, "first save was not recorded");
                var loaded = store.Load(candidate.CandidateId);
                Assert(loaded is not null && loaded.Repository == candidate.Repository &&
                    loaded.CommitSha == candidate.CommitSha && loaded.PackageId == candidate.PackageId &&
                    loaded.PackageVersion == candidate.PackageVersion &&
                    loaded.Dependencies.Count == 1 &&
                    loaded.Dependencies[0].PackageVersion == "2.0.0",
                    "reloaded candidate differs from the original snapshot");
            });

            Check("identical retry is idempotent", () =>
            {
                Assert(store.Save(candidate) == DependencyCandidateStoreOutcome.AlreadyExists,
                    "identical candidate retry was not idempotent");
            });

            Check("rejects candidate ID reuse for another commit", () =>
            {
                var changed = new DependencyReleaseCandidate(candidate.Repository, new string('c', 40),
                    candidate.PackageId, candidate.PackageVersion, candidate.CandidateId, candidate.Dependencies);
                Assert(store.Save(changed) == DependencyCandidateStoreOutcome.Conflict,
                    "candidate identity was overwritten by a different commit");
                Assert(store.Load(candidate.CandidateId)!.CommitSha == candidate.CommitSha,
                    "conflicting save changed the stored commit");
            });

            Check("missing candidate returns null", () =>
            {
                Assert(store.Load("not-saved") is null, "missing candidate unexpectedly loaded");
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS durable candidate store {passed}: {name}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
