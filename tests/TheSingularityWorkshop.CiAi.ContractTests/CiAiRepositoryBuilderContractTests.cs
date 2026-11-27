using TheSingularityWorkshop.CiAi;

internal static class CiAiRepositoryBuilderContractTests
{
    public static int Run()
    {
        var passed = 0;
        Check("one builder composes badge, dependency, extension, and Gauntlet opt-in", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/A")
                .WithBadge(Badges.SithPolicy())
                .WithDependency("TrentBest/B")
                .WithGauntlet()
                .Build();
            Assert(definition.Badges.Single().ImageUrl(definition.Repository).EndsWith(
                "/actions/workflows/sith-policy.yml/badge.svg", StringComparison.Ordinal), "live Sith badge URL changed");
            Assert(definition.DependencyRepositories.SequenceEqual(new[] { "TrentBest/B" }), "dependency identity changed");
            Assert(definition.Extensions.Single().Id == "Gauntlet.Registrar", "Gauntlet opt-in was not preserved");
        });
        Check("unbound and repository-bound fluent entry points compose the same model", () =>
        {
            var fromBound = CiAi.ForRepository("TrentBest/A").WithDependency("TrentBest/B").Build();
            var fromUnbound = CiAi.WithDependency(CiAi.Resolve("B")).Build("TrentBest/A");
            var ownerRelative = CiAi.ForRepository("TrentBest/A")
                .WithDependency(CiAi.Resolve("OtherRepo", "runtime")).Build();
            Assert(ownerRelative.Dependencies.Single() is CiAiRepositoryDependency resolved &&
                resolved.Repository == "TrentBest/OtherRepo" && resolved.Capability == "runtime",
                "short repository name or capability was not resolved against the configured owner");
            Assert(fromBound.DependencyRepositories.SequenceEqual(fromUnbound.DependencyRepositories),
                "the two entry paths produced different dependency declarations");
        });
        Check("ordered fallback retains declaration order", () =>
        {
            var definition = CiAi.ForRepository("TrentBest/C")
                .WithDependency(CiAi.Logic.Or("TrentBest/D", "TrentBest/E", "TrentBest/F")).Build();
            Assert(definition.DependencyRepositories.SequenceEqual(
                new[] { "TrentBest/D", "TrentBest/E", "TrentBest/F" }), "fallback order changed");
        });
        Check("unnamed and named development pipeline membership is deterministic", () =>
        {
            var baseline = CiAi.ForRepository("TrentBest/A").Build();
            var named = CiAi.ForRepository("TrentBest/A").WithNamedDevelopmentPipeline("Core")
                .WithNamedDevelopmentPipeline("Apps").WithNamedDevelopmentPipeline("Core").Build();
            Assert(baseline.DevelopmentPipelines.SequenceEqual(new[] { "default" }), "default pipeline missing");
            Assert(named.DevelopmentPipelines.SequenceEqual(new[] { "Core", "Apps" }), "named pipeline membership changed");
        });
        Check("ADP remains optional and separate from ordinary CI composition", () =>
        {
            var ordinary = CiAi.ForRepository("TrentBest/A").Build();
            var autonomous = CiAi.ForRepository("TrentBest/A")
                .WithADP(new CiAiAdpSettings(2)).Build();
            Assert(!ordinary.AutonomousDevelopmentEnabled && ordinary.AdpSettings is null,
                "ordinary CI unexpectedly enabled ADP");
            Assert(autonomous.AutonomousDevelopmentEnabled &&
                autonomous.AdpSettings!.MaximumRepairAttempts == 2 &&
                autonomous.AdpSettings.RequireHumanApprovalForWrites, "ADP policy was not preserved");
        });
        Check("LLM allocation retains bounded provider/model budgets", () =>
        {
            var allocation = new CiAiLlmAllocation("OpenAI", "model-x", 2, 30, 50_000,
                TimeSpan.FromMinutes(15), CiAiLlmPriority.High);
            var result = CiAi.ForRepository("TrentBest/A").WithLLMAllocation(allocation).Build().LlmAllocation!;
            Assert(result.Provider == "OpenAI" && result.Model == "model-x" &&
                result.MaximumConcurrentCalls == 2 && result.MaximumCallsPerWindow == 30 &&
                result.MaximumTokensPerWindow == 50_000 && result.Window == TimeSpan.FromMinutes(15) &&
                result.Priority == CiAiLlmPriority.High, "allocation limits changed");
        });
        Check("self-dependencies and duplicate declarations fail closed", () =>
        {
            Throws<ArgumentException>(() => CiAi.ForRepository("TrentBest/A").WithDependency("TrentBest/A"));
            Throws<InvalidOperationException>(() => CiAi.ForRepository("TrentBest/A")
                .WithDependency("TrentBest/B").WithDependency("TrentBest/B"));
            Throws<ArgumentException>(() => CiAi.Logic.Or("TrentBest/B", "TrentBest/B"));
        });
        Check("built definitions are immutable snapshots", () =>
        {
            var firstBuilder = CiAi.ForRepository("TrentBest/A").WithDependency("TrentBest/B");
            var first = firstBuilder.Build();
            var second = firstBuilder.WithDependency("TrentBest/C").Build();
            Assert(first.DependencyRepositories.SequenceEqual(new[] { "TrentBest/B" }), "earlier snapshot mutated");
            Assert(second.DependencyRepositories.SequenceEqual(new[] { "TrentBest/B", "TrentBest/C" }),
                "new immutable builder snapshot lost a declaration");
        });
        Check("invalid allocation and ADP budgets are rejected", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new CiAiAdpSettings(11));
            Throws<ArgumentOutOfRangeException>(() => new CiAiLlmAllocation("OpenAI", "x", maximumConcurrentCalls: 0));
            Throws<ArgumentOutOfRangeException>(() => new CiAiLlmAllocation("OpenAI", "x", priority: (CiAiLlmPriority)99));
        });
        Check("repository composition retains explicit cross-repository ADP relationships without enabling autonomy", () =>
        {
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
            var definition = CiAi.ForRepository("TrentBest/TheSingularityWorkshop.CiAi")
                .WithRelationship(contribution).WithRelationship(consumption).WithRelationship(experience).Build();
            Assert(definition.Relationships.Count == 3 &&
                definition.Relationships[0] == contribution &&
                definition.Relationships[1] == consumption &&
                definition.Relationships[2] == experience,
                "relationship graph declarations were not preserved in order");
            Assert(!definition.AutonomousDevelopmentEnabled,
                "declaring relationship metadata implicitly enabled autonomous execution");
            Throws<InvalidOperationException>(() => CiAi.ForRepository("TrentBest/A")
                .WithRelationship(contribution).WithRelationship(contribution));
        });
        Check("repository graph visibly projects every effective fluent modifier and preserves fallback order", () =>
        {
            var relationship = new CiRepositoryRelationship(
                "graph-content", 1, CiRepositoryRelationshipKind.ContentContribution,
                "TrentBest/Producer", "TrentBest/Consumer", "content-proposal", "TrentBest/Consumer");
            var definition = CiAi.ForRepository("TrentBest/Graph")
                .WithBadge(Badges.SithPolicy())
                .WithDependency("TrentBest/Direct")
                .WithDependency(CiAi.Logic.Or("TrentBest/FallbackA", "TrentBest/FallbackB"))
                .ExtendWith(new CiAiExtensionDefinition("Example.Extension", "2"))
                .WithNamedDevelopmentPipeline("Core")
                .WithRelationship(relationship)
                .WithADP(new CiAiAdpSettings(2))
                .WithLLMAllocation(new CiAiLlmAllocation("OpenAI", "model-x", 2, 30, 50_000,
                    TimeSpan.FromMinutes(15), CiAiLlmPriority.High))
                .Build();

            var graph = CiAiRepositoryGraphSnapshot.FromDefinition(definition);
            var kinds = graph.Nodes.Select(node => node.Kind).ToHashSet(StringComparer.Ordinal);
            foreach (var expected in new[]
            {
                "repository-root", "repository", "badge", "ordered-fallback", "extension",
                "development-pipeline", "adp-relationship", "adp-settings", "llm-allocation"
            })
                Assert(kinds.Contains(expected), $"graph omitted fluent modifier category '{expected}'");

            var fallback = graph.Nodes.Single(node => node.Kind == "ordered-fallback");
            var candidates = graph.Edges.Where(edge => edge.SourceNodeId == fallback.Id)
                .OrderBy(edge => edge.Order).ToArray();
            Assert(candidates.Length == 2 && candidates[0].Label == "candidate 1" &&
                candidates[1].Label == "candidate 2", "fallback candidate order was not represented");
            Assert(graph.Nodes.Any(node => node.Kind == "development-pipeline" &&
                node.Label == "Core" && node.IsExplicit), "explicit pipeline membership was omitted");
            Assert(graph.Nodes.Any(node => node.Kind == "adp-relationship" &&
                node.Label == "graph-content" && node.Details.Contains("authority: TrentBest/Consumer", StringComparison.Ordinal)),
                "relationship contract or authority was omitted");
            Assert(graph.Nodes.Any(node => node.Kind == "llm-allocation" &&
                node.Details.Contains("Tokens/window: 50000", StringComparison.Ordinal)),
                "LLM allocation bounds were omitted");
            Assert(graph.Edges.All(edge =>
                graph.Nodes.Any(node => node.Id == edge.SourceNodeId) &&
                graph.Nodes.Any(node => node.Id == edge.TargetNodeId)),
                "graph contains a dangling edge");

            var repeated = CiAiRepositoryGraphSnapshot.FromDefinition(definition);
            Assert(graph.Nodes.SequenceEqual(repeated.Nodes) && graph.Edges.SequenceEqual(repeated.Edges),
                "identical definitions did not produce an identical graph projection");

            var defaultGraph = CiAiRepositoryGraphSnapshot.FromDefinition(CiAi.ForRepository("TrentBest/Default").Build());
            Assert(defaultGraph.Nodes.Single(node => node.Kind == "development-pipeline").IsExplicit == false,
                "implicit default pipeline was presented as an explicit fluent modifier");
        });
        Check("dependency registration snapshot is deterministic, exact-SHA-bound, and preserves dependency direction", () =>
        {
            const string sha = "0123456789abcdef0123456789abcdef01234567";
            var definition = CiAi.ForRepository("TrentBest/Consumer")
                .WithDependency(CiAi.Resolve("TrentBest/Provider", "runtime"))
                .WithDependency(CiAi.Logic.Or("TrentBest/Primary", "TrentBest/Fallback"))
                .Build();

            var snapshot = CiAiDependencyRegistrationSnapshot.FromDefinition(definition, sha);
            Assert(snapshot.SchemaVersion == CiAiDependencyRegistrationSnapshot.CurrentSchema,
                "registration schema changed");
            Assert(snapshot.SourceRepository == "TrentBest/Consumer" &&
                snapshot.SourceCommitSha == sha, "source identity or exact SHA was not retained");
            Assert(snapshot.Registrations.Count == 3, "direct and fallback registrations were not all emitted");
            Assert(snapshot.Registrations[0].TargetRepository == "TrentBest/Provider" &&
                snapshot.Registrations[0].Capability == "runtime" &&
                snapshot.Registrations[0].DependencyKind == "required",
                "direct dependency direction or capability was lost");
            Assert(snapshot.Registrations[1].TargetRepository == "TrentBest/Primary" &&
                snapshot.Registrations[1].DependencyKind == "fallback-candidate" &&
                snapshot.Registrations[1].CandidateIndex == 0 &&
                snapshot.Registrations[2].TargetRepository == "TrentBest/Fallback" &&
                snapshot.Registrations[2].CandidateIndex == 1,
                "fallback order was not retained in registration payload");

            var json = snapshot.ToJson();
            Assert(json == snapshot.ToJson() &&
                json.Contains(CiAiDependencyRegistrationSnapshot.CurrentSchema, StringComparison.Ordinal) &&
                json.Contains("sourceCommitSha", StringComparison.Ordinal) &&
                json.Contains(sha, StringComparison.Ordinal),
                "registration JSON was not deterministic or did not retain exact source provenance");

            var repeated = CiAiDependencyRegistrationSnapshot.FromDefinition(definition, sha);
            Assert(snapshot.Registrations.SequenceEqual(repeated.Registrations),
                "identical source declaration did not produce stable registration identities");
            var nextRevision = CiAiDependencyRegistrationSnapshot.FromDefinition(definition,
                "fedcba9876543210fedcba9876543210fedcba98");
            Assert(snapshot.Registrations.SequenceEqual(nextRevision.Registrations),
                "stable declaration identity incorrectly changed with source commit revision");

            Throws<ArgumentException>(() =>
                CiAiDependencyRegistrationSnapshot.FromDefinition(definition, "short-sha"));
            Throws<ArgumentException>(() =>
                CiAiDependencyRegistrationSnapshot.FromDefinition(
                    CiAi.ForRepository("TrentBest/Self").WithDependency("TrentBest/Self").Build(), sha));
            var empty = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/Independent").Build(), sha);
            Assert(empty.Registrations.Count == 0,
                "an authoritative empty dependency snapshot must remain representable");
        });
        Check("dependency registration receiver reconciliation is idempotent, monotonic, and withdraws removed declarations", () =>
        {
            const string sha1 = "0123456789abcdef0123456789abcdef01234567";
            const string sha2 = "fedcba9876543210fedcba9876543210fedcba98";
            var first = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/Consumer").WithDependency("TrentBest/Provider").Build(), sha1);
            var accepted = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(null, first, 1);
            Assert(accepted.Status == CiAiDependencyRegistrationApplyStatus.Applied &&
                accepted.Ledger!.Registrations.Count == 1, "first complete snapshot was not accepted");

            var replay = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(accepted.Ledger, first, 1);
            Assert(replay.Status == CiAiDependencyRegistrationApplyStatus.Duplicate &&
                ReferenceEquals(replay.Ledger, accepted.Ledger), "identical replay was not idempotent");

            var stale = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(accepted.Ledger, first, 0);
            Assert(stale.Status == CiAiDependencyRegistrationApplyStatus.Stale,
                "older source revision was not rejected");

            var conflicting = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/Consumer").WithDependency("TrentBest/Other").Build(), sha2);
            var conflict = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(accepted.Ledger, conflicting, 1);
            Assert(conflict.Status == CiAiDependencyRegistrationApplyStatus.Conflict &&
                conflict.Ledger!.Registrations.Single().TargetRepository == "TrentBest/Provider",
                "same-revision conflicting snapshot replaced accepted state");

            var empty = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/Consumer").Build(), sha2);
            var withdrawn = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(accepted.Ledger, empty, 2);
            Assert(withdrawn.Status == CiAiDependencyRegistrationApplyStatus.Applied &&
                withdrawn.Ledger!.Registrations.Count == 0,
                "newer complete empty snapshot did not withdraw removed registrations");

            var wrongSource = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/AnotherSource").Build(), sha2);
            var mismatch = CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(accepted.Ledger, wrongSource, 2);
            Assert(mismatch.Status == CiAiDependencyRegistrationApplyStatus.SourceMismatch,
                "ledger accepted a snapshot from another source");

            Throws<ArgumentOutOfRangeException>(() =>
                CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(null, first, 0));
        });
        Check("ecosystem graph deduplicates cycles and diamonds and keeps ADP membership explicit", () =>
        {
            const string sha = "0123456789abcdef0123456789abcdef01234567";
            var a = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/A").WithDependency("TrentBest/B").WithDependency("TrentBest/C").Build(), sha);
            var b = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/B").WithDependency("TrentBest/D").Build(), sha);
            var c = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/C").WithDependency("TrentBest/D").Build(), sha);
            var d = CiAiDependencyRegistrationSnapshot.FromDefinition(
                CiAi.ForRepository("TrentBest/D").WithDependency("TrentBest/A").Build(), sha);
            var ledgers = new[] { a, b, c, d }.Select(x =>
                CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(null, x, 1).Ledger!).ToArray();
            var cluster = new CiAiAdpClusterDefinition("adp-core", "Autonomous Development Pipeline",
                new[] { "TrentBest/A", "TrentBest/D" });

            var graph = CiAiEcosystemGraphSnapshot.FromLedgers("TrentBest/A", ledgers, new[] { cluster });
            Assert(graph.Coverage == CiAiEcosystemGraphCoverage.Partial,
                "a supplied ledger set was incorrectly presented as complete ecosystem coverage");
            Assert(graph.Nodes.Count(x => x.Repository == "TrentBest/D") == 1,
                "shared dependency or cycle duplicated a repository node");
            Assert(graph.Edges.Count(x => x.Kind == "required") == 5,
                "directed dependency declarations were lost");
            Assert(graph.Edges.Count(x => x.Kind == "adp-membership") == 2,
                "explicit ADP membership edges were not projected");
            Assert(graph.Nodes.Single(x => x.Repository == "TrentBest/A").Kind == "repository-focal",
                "focal repository was not distinguished");
            Assert(graph.Nodes.Single(x => x.Kind == "adp-cluster").Label == "Autonomous Development Pipeline",
                "explicit ADP cluster title was not retained");
            var repeated = CiAiEcosystemGraphSnapshot.FromLedgers("TrentBest/A", ledgers, new[] { cluster });
            Assert(graph.Nodes.SequenceEqual(repeated.Nodes) && graph.Edges.SequenceEqual(repeated.Edges),
                "ecosystem graph projection was not deterministic");
            Throws<ArgumentException>(() => new CiAiAdpClusterDefinition("empty", "Empty", Array.Empty<string>()));
        });
        return passed;

        void Check(string name, Action action)
        {
            try { action(); passed++; }
            catch (Exception exception) { throw new InvalidOperationException($"CiAi repository composition contract failed: {name}", exception); }
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
