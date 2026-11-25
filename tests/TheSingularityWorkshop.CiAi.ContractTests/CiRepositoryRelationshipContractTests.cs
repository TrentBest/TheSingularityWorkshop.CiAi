using TheSingularityWorkshop.CiAi;

internal static class CiRepositoryRelationshipContractTests
{
    public static int Run()
    {
        var passed = 0;
        var contribution = new CiRepositoryRelationship(
            "rawwar-content-contribution", 1, CiRepositoryRelationshipKind.ContentContribution,
            "TrentBest/raWWar", "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem",
            "surps-content-proposal", "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem");
        var consumption = new CiRepositoryRelationship(
            "surps-canonical-content-consumption", 1, CiRepositoryRelationshipKind.CanonicalContentConsumption,
            "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem", "TrentBest/raWWar",
            "surps-canonical-content", "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem");
        var experience = new CiRepositoryRelationship(
            "rawwar-experience-validation", 1, CiRepositoryRelationshipKind.ExperienceValidation,
            "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem", "TrentBest/raWWar",
            "rawwar-experience-candidate", "TrentBest/raWWar");

        Check("producer and consumer roles are separate directed contracts", () =>
        {
            Assert(contribution.SourceRepository == "TrentBest/raWWar" &&
                contribution.TargetRepository == "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem" &&
                contribution.AuthorityRepository == "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem",
                "contribution direction or SURPS authority was lost");
            Assert(consumption.SourceRepository == "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem" &&
                consumption.TargetRepository == "TrentBest/raWWar",
                "canonical content consumption direction was lost");
            Assert(experience.SourceRepository == "TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem" &&
                experience.TargetRepository == "TrentBest/raWWar" &&
                experience.AuthorityRepository == "TrentBest/raWWar",
                "Experience ownership was not kept with raWWar");
        });

        Check("relationship rejects invalid identity, endpoints, version, and kind", () =>
        {
            Throws<ArgumentException>(() => new CiRepositoryRelationship(
                "Bad ID", 1, CiRepositoryRelationshipKind.ContentContribution,
                "TrentBest/raWWar", "TrentBest/SURPS", "content", "TrentBest/SURPS"));
            Throws<ArgumentException>(() => new CiRepositoryRelationship(
                "self-edge", 1, CiRepositoryRelationshipKind.BuildDependency,
                "TrentBest/raWWar", "TrentBest/raWWar", "build", "TrentBest/raWWar"));
            Throws<ArgumentOutOfRangeException>(() => new CiRepositoryRelationship(
                "bad-version", 0, CiRepositoryRelationshipKind.BuildDependency,
                "TrentBest/raWWar", "TrentBest/SURPS", "build", "TrentBest/raWWar"));
            Throws<ArgumentOutOfRangeException>(() => new CiRepositoryRelationship(
                "bad-kind", 1, (CiRepositoryRelationshipKind)99,
                "TrentBest/raWWar", "TrentBest/SURPS", "build", "TrentBest/raWWar"));
        });

        Check("change notice binds exact source revision and artifact digest", () =>
        {
            var notice = Notice(contribution, "event-1", new string('a', 40), new string('b', 64));
            Assert(notice.SourceCommitSha == new string('a', 40) &&
                notice.ArtifactSha256 == new string('b', 64) &&
                notice.Relationship == contribution,
                "notice did not preserve exact provenance");
        });

        Check("change notice rejects abbreviated revisions and malformed artifact digests", () =>
        {
            Throws<ArgumentException>(() => Notice(contribution, "event-2", "abcdef", new string('b', 64)));
            Throws<ArgumentException>(() => Notice(contribution, "event-3", new string('a', 40), "not-a-digest"));
        });

        Check("delivery retries deduplicate while changed source or artifact creates new work", () =>
        {
            var first = Notice(contribution, "event-4", new string('a', 40), new string('b', 64), "root-event");
            var retry = Notice(contribution, "event-5", new string('a', 40), new string('b', 64), "root-event");
            var changedSource = Notice(contribution, "event-6", new string('c', 40), new string('b', 64), "root-event");
            var changedArtifact = Notice(contribution, "event-7", new string('a', 40), new string('d', 64), "root-event");
            Assert(first.IdempotencyKey == retry.IdempotencyKey,
                "retry identity depended on transport event ID");
            Assert(first.IdempotencyKey != changedSource.IdempotencyKey &&
                first.IdempotencyKey != changedArtifact.IdempotencyKey,
                "changed source/artifact was incorrectly suppressed as a duplicate");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
        }
    }

    private static CiRepositoryChangeNotice Notice(
        CiRepositoryRelationship relationship, string eventId, string sourceSha, string digest, string? causationId = null) =>
        new(eventId, relationship, sourceSha, "surps-content/catalog.json", digest, causationId);

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
