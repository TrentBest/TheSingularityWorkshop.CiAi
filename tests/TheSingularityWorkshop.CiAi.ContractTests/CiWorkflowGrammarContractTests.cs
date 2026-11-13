using TheSingularityWorkshop.CiAi;
using TheSingularityWorkshop.GrammarAi;

internal static class CiWorkflowGrammarContractTests
{
    public static int Run()
    {
        var passed = 0;
        var plan = new CiWorkflowPlanDraft("TrentBest/TheSingularityWorkshop.CiAi", "release-proof",
            new[]
            {
                Step("restore", "Restore dependencies", "build-test"),
                Step("build", "Build Release", "build-test", "restore"),
                Step("test", "Run contract tests", "build-test", "build"),
                Step("package", "Pack candidate", "package", "test")
            });

        Check("CiAi maps a populated workflow into the pinned GrammarAi structural graph", () =>
        {
            Assert(CiWorkflowGrammarMap.GrammarAiRevision == "2582362ad233faeffe82c2de28d464efffb0cda4",
                "GrammarAi source revision differs from the immutable integration pin");
            var proposal = CiWorkflowGrammarMap.CreateProposal(plan);
            var graph = CiWorkflowGrammarMap.ValidateProposal(proposal);
            Assert(graph.Definition.Id == CiWorkflowGrammarMap.DefinitionId &&
                graph.Definition.Version == CiWorkflowGrammarMap.SchemaVersion,
                "graph schema identity/version differs");
            Assert(proposal.WireVersion == GrammarStructureProposalCodec.CurrentWireVersion,
                "schema version and codec wire version were not kept distinct");
            Assert(graph.Nodes.Count == 5 && graph.Relationships.Count == 7,
                "populated workflow graph did not preserve all nodes and relationships");
            Assert(proposal.StepInstanceIds.Count == 4, "step correlation map is incomplete");
        });

        Check("GrammarAi proposal encoding is deterministic and round-trips", () =>
        {
            var first = CiWorkflowGrammarMap.CreateProposal(plan);
            var second = CiWorkflowGrammarMap.CreateProposal(plan);
            Assert(first.Json == second.Json, "identical plans produced different integer-first proposals");
            var decoded = CiWorkflowGrammarMap.ValidateProposal(first.Json);
            Assert(GrammarStructureProposalCodec.Serialize(decoded) == first.Json,
                "GrammarAi encode/decode round trip was not deterministic");
            Assert(CiWorkflowGrammarMap.ValidateProposal(first).Relationships.Count == 7,
                "proposal envelope did not reconcile with its graph");
        });

        Check("required workflow steps, repeated step membership, optional dependencies, and ordering are enforced", () =>
        {
            var single = CiWorkflowGrammarMap.CreateProposal(new CiWorkflowPlanDraft(
                "TrentBest/TheSingularityWorkshop.CiAi", "single-step",
                new[] { Step("build", "Build", "build-test") }));
            var singleGraph = CiWorkflowGrammarMap.ValidateProposal(single);
            Assert(singleGraph.Relationships.Count == 1,
                "optional dependency slot should allow zero dependencies");
            var repeated = CiWorkflowGrammarMap.CreateProposal(plan);
            var graph = CiWorkflowGrammarMap.ValidateProposal(repeated);
            var orderedSteps = graph.GetRelationships(repeated.WorkflowInstanceId,
                CiWorkflowGrammarMap.WorkflowStepsSlotId).Select(x => x.ChildInstanceId).ToArray();
            Assert(orderedSteps.SequenceEqual(plan.Steps.Select(x => repeated.StepInstanceIds[x.Id])),
                "repeated workflow step relationships lost declaration order");
            Throws<ArgumentException>(() => new GrammarStructureInstance(
                CiWorkflowGrammarMap.Definition,
                new[] { new GrammarNodeInstance(1, CiWorkflowGrammarMap.WorkflowNodeId) },
                Array.Empty<GrammarRelationship>()));
        });

        Check("GrammarAi rejects unknown node and slot identities", () =>
        {
            var json = CiWorkflowGrammarMap.CreateProposal(plan).Json;
            var invalidNode = json.Replace("[2,2]", "[2,999]", StringComparison.Ordinal);
            var invalidSlot = json.Replace("[[1,101,0,2]", "[[1,999,0,2]", StringComparison.Ordinal);
            Throws<ArgumentException>(() => CiWorkflowGrammarMap.ValidateProposal(invalidNode));
            Throws<ArgumentException>(() => CiWorkflowGrammarMap.ValidateProposal(invalidSlot));
        });

        Check("GrammarAi rejects stale schema versions and unknown codec wire versions", () =>
        {
            var json = CiWorkflowGrammarMap.CreateProposal(plan).Json;
            Throws<ArgumentException>(() => CiWorkflowGrammarMap.ValidateProposal(
                json.Replace("\"gv\":1", "\"gv\":2", StringComparison.Ordinal)));
            Throws<ArgumentException>(() => CiWorkflowGrammarMap.ValidateProposal(
                json.Replace("\"v\":2", "\"v\":99", StringComparison.Ordinal)));
        });

        Check("GrammarAi rejects malformed and oversized proposals", () =>
        {
            Throws<ArgumentException>(() => CiWorkflowGrammarMap.ValidateProposal("{not-json"));
            Throws<ArgumentException>(() => CiWorkflowGrammarMap.ValidateProposal(
                new string('x', GrammarLimits.MaxProposalCharacters + 1)));
        });

        Check("CiAi refuses to map a plan that fails semantic validation", () =>
        {
            var invalid = new CiWorkflowPlanDraft(plan.Repository, "invalid-plan",
                new[] { Step("build", "Build", "build-test", "missing") });
            Throws<ArgumentException>(() => CiWorkflowGrammarMap.CreateProposal(invalid));
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        static CiWorkflowPlanStepDraft Step(string id, string name, string domain, params string[] dependencies) =>
            new(id, name, domain, Array.AsReadOnly(dependencies), CiWorkflowStepCondition.AllDependenciesSucceeded);
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
