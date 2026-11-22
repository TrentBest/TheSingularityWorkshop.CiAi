using TheSingularityWorkshop.CiAi;

internal static class CiAiPipelineGateExpressionContractTests
{
    public static int Run()
    {
        var passed = 0;
        var b = Leaf("gate-b", "TrentBest/B", "candidate-b", new string('b', 40));
        var c = Leaf("gate-c", "TrentBest/C", "candidate-c", new string('c', 40));
        var d = Leaf("gate-d", "TrentBest/D", "candidate-d", new string('d', 40));
        var expression = CiAiPipelineGateExpression.ForPipeline(
            "PipelineA",
            new CiAiAnyOf(new CiAiGateExpression[]
            {
                new CiAiAllOf(new CiAiGateExpression[] { b, c }),
                d
            }));

        Check("B and C together satisfy the composite alternative", () =>
        {
            var result = CiAiPipelineGateEvaluator.Evaluate(expression, new Dictionary<string, DependencyReleaseGateEvaluation>
            {
                ["gate-b"] = Evaluation(b, DependencyReleaseGateState.ReadyForConsumerVerification),
                ["gate-c"] = Evaluation(c, DependencyReleaseGateState.ReadyForConsumerVerification)
            });
            Assert(result == CiAiPipelineGateState.Satisfied, "B+C did not satisfy AllOf");
        });

        Check("one passing child cannot release an AllOf hold", () =>
        {
            var result = CiAiPipelineGateEvaluator.Evaluate(expression, new Dictionary<string, DependencyReleaseGateEvaluation>
            {
                ["gate-b"] = Evaluation(b, DependencyReleaseGateState.ReadyForConsumerVerification)
            });
            Assert(result == CiAiPipelineGateState.Waiting, "B alone released the B+C alternative");
        });

        Check("D independently satisfies the OR alternative", () =>
        {
            var result = CiAiPipelineGateEvaluator.Evaluate(expression, new Dictionary<string, DependencyReleaseGateEvaluation>
            {
                ["gate-d"] = Evaluation(d, DependencyReleaseGateState.ReadyForConsumerVerification)
            });
            Assert(result == CiAiPipelineGateState.Satisfied, "D did not satisfy the complete alternative");
        });

        Check("gate events identify only pipelines containing the changed leaf", () =>
        {
            Assert(expression.DependsOnGate("gate-b") &&
                   expression.DependsOnGate("gate-c") &&
                   expression.DependsOnGate("gate-d"),
                "declared leaf event was not associated with the pipeline");
            Assert(!expression.DependsOnGate("gate-unrelated"),
                "unrelated gate event would wake this pipeline");
        });

        Check("blocked alternatives remain blocked until a complete alternative passes", () =>
        {
            var result = CiAiPipelineGateEvaluator.Evaluate(expression, new Dictionary<string, DependencyReleaseGateEvaluation>
            {
                ["gate-b"] = Evaluation(b, DependencyReleaseGateState.Blocked),
                ["gate-c"] = Evaluation(c, DependencyReleaseGateState.ReadyForConsumerVerification),
                ["gate-d"] = Evaluation(d, DependencyReleaseGateState.Blocked)
            });
            Assert(result == CiAiPipelineGateState.Blocked, "all terminally blocked alternatives did not remain blocked");
        });

        Check("stale candidate SHA cannot satisfy a leaf gate", () =>
        {
            var stale = Evaluation(b, DependencyReleaseGateState.ReadyForConsumerVerification) with
            {
                CommitSha = new string('e', 40)
            };
            var result = CiAiPipelineGateEvaluator.Evaluate(
                CiAiPipelineGateExpression.ForPipeline("PipelineA", b),
                new Dictionary<string, DependencyReleaseGateEvaluation> { ["gate-b"] = stale });
            Assert(result == CiAiPipelineGateState.Blocked, "stale SHA satisfied a gate");
        });

        Check("missing leaf evidence is waiting, not success", () =>
        {
            var result = CiAiPipelineGateEvaluator.Evaluate(
                CiAiPipelineGateExpression.ForPipeline("PipelineA", b),
                new Dictionary<string, DependencyReleaseGateEvaluation>());
            Assert(result == CiAiPipelineGateState.Waiting, "missing evidence was not held");
        });

        Check("duplicate leaf identities are rejected", () =>
        {
            Throws<ArgumentException>(() => CiAiPipelineGateExpression.ForPipeline(
                "PipelineA", new CiAiAllOf(new CiAiGateExpression[] { b, b })));
        });

        Check("expression depth is bounded", () =>
        {
            CiAiGateExpression nested = new CiAiAllOf(new CiAiGateExpression[] { b, c });
            for (var i = 0; i < CiAiPipelineGateExpression.MaximumDepth; i++)
                nested = new CiAiAnyOf(new[] { nested, d });
            Throws<ArgumentException>(() => CiAiPipelineGateExpression.ForPipeline("PipelineA", nested));
        });

        Check("expression node count is bounded", () =>
        {
            var leaves = Enumerable.Range(0, CiAiPipelineGateExpression.MaximumNodes)
                .Select(index => (CiAiGateExpression)new CiAiRepositoryGateReference(
                    "gate-" + index, "TrentBest/Repo" + index, "candidate-" + index, new string('a', 40)))
                .ToArray();
            Throws<ArgumentException>(() => CiAiPipelineGateExpression.ForPipeline(
                "PipelineA", new CiAiAllOf(leaves)));
        });

        Check("gate and candidate identities reject control characters", () =>
        {
            Throws<ArgumentException>(() => new CiAiRepositoryGateReference(
                "gate\ninvalid", "TrentBest/B", "candidate", new string('b', 40)));
            Throws<ArgumentException>(() => new CiAiRepositoryGateReference(
                "gate", "TrentBest/B", "candidate\ninvalid", new string('b', 40)));
        });

        Check("empty AND/OR nodes are rejected", () =>
        {
            Throws<ArgumentException>(() => new CiAiAllOf(Array.Empty<CiAiGateExpression>()));
            Throws<ArgumentException>(() => new CiAiAnyOf(Array.Empty<CiAiGateExpression>()));
        });

        Check("waiting gates suppress LLM allocation without polling or dispatch", () =>
        {
            var admission = CiAiPipelineGateAdmissionPolicy.Evaluate(CiAiPipelineGateState.Waiting);
            Assert(admission.State == CiAiPipelineGateAdmissionState.DormantWaiting &&
                   !admission.MayAllocateLlm && admission.Reason == "pipeline.gates-waiting",
                "waiting gate allowed LLM allocation or lost its durable disposition");
        });

        Check("blocked gates deny LLM allocation", () =>
        {
            var admission = CiAiPipelineGateAdmissionPolicy.Evaluate(CiAiPipelineGateState.Blocked);
            Assert(admission.State == CiAiPipelineGateAdmissionState.DeniedBlocked &&
                   !admission.MayAllocateLlm,
                "blocked gate allowed LLM allocation");
        });

        Check("satisfied gates make a repository eligible for normal LLM allocation", () =>
        {
            var admission = CiAiPipelineGateAdmissionPolicy.Evaluate(CiAiPipelineGateState.Satisfied);
            Assert(admission.State == CiAiPipelineGateAdmissionState.Eligible &&
                   admission.MayAllocateLlm,
                "satisfied gate did not restore eligibility");
        });

        Check("unknown gate states fail closed", () =>
        {
            var admission = CiAiPipelineGateAdmissionPolicy.Evaluate((CiAiPipelineGateState)99);
            Assert(admission.State == CiAiPipelineGateAdmissionState.DeniedBlocked &&
                   !admission.MayAllocateLlm,
                "unknown gate state was treated as eligible");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS composite pipeline gate {passed}: {name}");
        }
    }

    private static CiAiRepositoryGateReference Leaf(string id, string repository, string candidateId, string sha) =>
        new(id, repository, candidateId, sha);

    private static DependencyReleaseGateEvaluation Evaluation(
        CiAiRepositoryGateReference leaf, DependencyReleaseGateState state) =>
        new(leaf.CandidateId, leaf.Repository, leaf.CommitSha, "Package", "1.0.0", state,
            Array.Empty<string>(), Array.Empty<string>());

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
