using TheSingularityWorkshop.CiAi;
using TheSingularityWorkshop.GrammarAi;

internal static class CiWorkflowGrammarContractTests
{
    public static int Run()
    {
        var passed=0;
        var plan=new CiWorkflowPlanDraft("TrentBest/TheSingularityWorkshop.CiAi","release-proof",new[]
        {
            Step("restore","Restore dependencies","restore","build-test",
                inputs:new[]{new CiWorkflowTypedValueDraft("repository","string"),new CiWorkflowTypedValueDraft("source_sha","commit_sha")}),
            Step("build","Build Release","build","build-test","restore",
                inputs:new[]{new CiWorkflowTypedValueDraft("source_sha","commit_sha")},
                outputs:new[]{new CiWorkflowTypedValueDraft("build_artifact","artifact")},
                capabilities:new[]{"read_source","execute_build"},sideEffects:new[]{"read_files","write_artifacts","spawn_process"},
                evidence:new[]{"build_log"},outcomes:new[]{"build_succeeded"}),
            Step("test","Run contract tests","test","build-test","build",
                inputs:new[]{new CiWorkflowTypedValueDraft("build_artifact","artifact")},
                outputs:new[]{new CiWorkflowTypedValueDraft("test_results","artifact")},
                capabilities:new[]{"execute_tests"},sideEffects:new[]{"spawn_process"},
                evidence:new[]{"test_results"},outcomes:new[]{"tests_passed"}),
            Step("package","Pack candidate","pack","package","test",
                inputs:new[]{new CiWorkflowTypedValueDraft("source_sha","commit_sha")},
                outputs:new[]{new CiWorkflowTypedValueDraft("package","artifact")},
                capabilities:new[]{"create_package"},sideEffects:new[]{"write_artifacts"},
                evidence:new[]{"package_metadata","provenance"},outcomes:new[]{"package_created","evidence_complete"})
        },new[]{"push","pull_request"});

        Check("workflow graph contains triggers, actions, typed values, capabilities, side effects, evidence and outcomes",()=>{
            Assert(CiWorkflowGrammarMap.GrammarAiRevision=="2582362ad233faeffe82c2de28d464efffb0cda4","GrammarAi pin mismatch");
            Assert(CiWorkflowGrammarMap.ProtocolAiRevision=="024ca3f2fac486d5aefa6f276c5203d5bdfe3bcf","ProtocolAi pin mismatch");
            var proposal=CiWorkflowGrammarMap.CreateProposal(plan);var graph=CiWorkflowGrammarMap.ValidateProposal(proposal);
            Assert(graph.Definition.Id==CiWorkflowGrammarMap.DefinitionId && graph.Definition.Version==2,"schema identity/version mismatch");
            Assert(proposal.WireVersion==GrammarStructureProposalCodec.CurrentWireVersion,"codec wire version mismatch");
            Assert(graph.Nodes.Count(n=>n.NodeDefinitionId==CiWorkflowGrammarMap.StepNodeId)==4,"step nodes missing");
            Assert(graph.Nodes.Count(n=>n.NodeDefinitionId==CiWorkflowGrammarMap.InputNodeId)==5,"input nodes missing");
            Assert(graph.Nodes.Count(n=>n.NodeDefinitionId==CiWorkflowGrammarMap.OutputNodeId)==3,"output nodes missing");
            Assert(graph.Relationships.Count(r=>r.SlotId==CiWorkflowGrammarMap.WorkflowTriggersSlotId)==2,"triggers missing");
            Assert(graph.Relationships.Count(r=>r.SlotId==CiWorkflowGrammarMap.StepCapabilitiesSlotId)==4,"capabilities missing");
            Assert(graph.Relationships.Count(r=>r.SlotId==CiWorkflowGrammarMap.StepSideEffectsSlotId)==5,"side effects missing");
            Assert(graph.Relationships.Count(r=>r.SlotId==CiWorkflowGrammarMap.StepEvidenceRolesSlotId)==4,"evidence roles missing");
            Assert(graph.Relationships.Count(r=>r.SlotId==CiWorkflowGrammarMap.StepOutcomeRolesSlotId)==4,"outcome roles missing");
        });
        Check("v2 encoding is deterministic and round-trips against the exact definition",()=>{
            var first=CiWorkflowGrammarMap.CreateProposal(plan);var second=CiWorkflowGrammarMap.CreateProposal(plan);
            Assert(first.Json==second.Json,"proposal encoding is nondeterministic");
            var graph=CiWorkflowGrammarMap.ValidateProposal(first.Json);
            Assert(GrammarStructureProposalCodec.Serialize(graph)==first.Json,"round trip differs");
            Assert(CiWorkflowGrammarMap.ValidateProposal(first).Relationships.Count==graph.Relationships.Count,"envelope mismatch");
        });
        Check("request exposure snapshots reject disabled members",()=>{
            var restricted=CiWorkflowVocabularyCatalog.CreateSnapshot(exposedMembers:new Dictionary<ulong,IReadOnlyCollection<ulong>>
            {[CiWorkflowVocabularyCatalog.TriggerCategoryId]=Array.Empty<ulong>()});
            var json=CiWorkflowGrammarMap.CreateProposal(plan).Json;
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.ValidateProposal(json,restricted));
            Assert(restricted.Digest!=CiWorkflowVocabularyCatalog.CreateSnapshot().Digest,"snapshot digest ignored exposure");
        });
        Check("GrammarAi rejects an external reference from the wrong category",()=>{
            var json=CiWorkflowGrammarMap.CreateProposal(plan).Json;
            var wrong=json.Replace($"[1,{CiWorkflowGrammarMap.WorkflowTriggersSlotId},1,{CiWorkflowVocabularyCatalog.TriggerCategoryId},1]",
                $"[1,{CiWorkflowGrammarMap.WorkflowTriggersSlotId},1,{CiWorkflowVocabularyCatalog.ActionCategoryId},1]",StringComparison.Ordinal);
            Assert(wrong!=json,"test failed to locate trigger reference");
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.ValidateProposal(wrong));
        });
        Check("stale schema, unknown wire version, malformed and oversized proposals fail closed",()=>{
            var json=CiWorkflowGrammarMap.CreateProposal(plan).Json;
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.ValidateProposal(json.Replace("\"gv\":2","\"gv\":3",StringComparison.Ordinal)));
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.ValidateProposal(json.Replace("\"v\":2","\"v\":99",StringComparison.Ordinal)));
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.ValidateProposal("{not-json"));
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.ValidateProposal(new string('x',GrammarLimits.MaxProposalCharacters+1)));
        });
        Check("plans without triggers or typed step actions cannot be mapped",()=>{
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.CreateProposal(plan with{Triggers=Array.Empty<string>()}));
            Throws<ArgumentException>(()=>CiWorkflowGrammarMap.CreateProposal(plan with{Steps=plan.Steps.Select(s=>s with{ActionId=null}).ToArray()}));
        });
        return passed;
        void Check(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
        static CiWorkflowPlanStepDraft Step(string id,string name,string action,string domain,string dependency="",
            IReadOnlyList<CiWorkflowTypedValueDraft>? inputs=null,IReadOnlyList<CiWorkflowTypedValueDraft>? outputs=null,
            IReadOnlyList<string>? capabilities=null,IReadOnlyList<string>? sideEffects=null,
            IReadOnlyList<string>? evidence=null,IReadOnlyList<string>? outcomes=null)=>
            new(id,name,domain,Array.AsReadOnly(string.IsNullOrEmpty(dependency)?Array.Empty<string>():new[]{dependency}),
                CiWorkflowStepCondition.AllDependenciesSucceeded,action,inputs,outputs,capabilities,sideEffects,evidence,outcomes);
        static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    }
}
