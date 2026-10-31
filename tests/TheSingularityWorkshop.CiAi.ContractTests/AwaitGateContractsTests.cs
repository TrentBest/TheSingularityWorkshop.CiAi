using TheSingularityWorkshop.CiAi;

internal static class AwaitGateContractsTests
{
    private const string Consumer = "TrentBest/The";
    private const string Provider = "TrentBest/MyVR";
    private const string SourceSha = "abcdef0123456789abcdef0123456789abcdef01";
    private const string ManifestDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string EvidenceDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    public static int Run()
    {
        var passed = 0;
        var contract = NewContract();
        var machine = NewEvidence();

        Check("gate contract requires an exact provider source and SHA-256 identities", () =>
        {
            Throws<ArgumentException>(() => new AwaitGateContract("B", 1, Consumer, Provider, "short",
                ManifestDigest, "policy-v1", Approval(),
                new[] { new AwaitGateRequirement("dimensions", "Dimensions are validated", "valid", AwaitGateEvidenceKind.Machine) }));
            Throws<ArgumentException>(() => new AwaitGateContract("B", 1, Consumer, Provider, SourceSha,
                "not-a-digest", "policy-v1",
                new[] { new AwaitGateRequirement("dimensions", "Dimensions are validated", "valid", AwaitGateEvidenceKind.Machine) }));
        });

        Check("gate contract rejects duplicate requirement identities", () =>
            Throws<ArgumentException>(() => NewContract(new[]
            {
                MachineRequirement(), MachineRequirement()
            })));

        Check("missing evidence remains pending and cannot satisfy a gate", () =>
        {
            var result = AwaitGateEvaluator.Evaluate(contract, Array.Empty<AwaitGateEvidence>());
            Assert(result.State == AwaitGateState.Pending && !result.IsSatisfied,
                "missing evidence opened the gate");
            Assert(result.Requirements.Single().ReasonCode == "evidence.missing",
                "missing evidence reason was not retained");
        });

        Check("exact machine evidence satisfies the approved gate", () =>
        {
            var result = AwaitGateEvaluator.Evaluate(contract, new[] { machine });
            Assert(result.IsSatisfied && result.ProviderSourceSha == SourceSha,
                "exact evidence did not satisfy its bound gate");
        });

        Check("duplicate evidence is indeterminate rather than successful", () =>
        {
            var result = AwaitGateEvaluator.Evaluate(contract, new[] { machine, machine });
            Assert(result.State == AwaitGateState.Indeterminate &&
                result.Requirements.Single().ReasonCode == "evidence.duplicate",
                "duplicate evidence was accepted");
        });

        Check("evidence from a changed provider SHA is indeterminate", () =>
        {
            var changed = NewEvidence(providerSourceSha: "123456789abcdef0123456789abcdef012345678");
            var result = AwaitGateEvaluator.Evaluate(contract, new[] { changed });
            Assert(result.State == AwaitGateState.Indeterminate &&
                result.Requirements.Single().ReasonCode == "evidence.provenance-mismatch",
                "evidence for another provider revision was accepted");
        });

        Check("evidence from another manifest digest is indeterminate", () =>
        {
            var changed = NewEvidence(manifestDigest: new string('d', 64));
            Assert(AwaitGateEvaluator.Evaluate(contract, new[] { changed }).State == AwaitGateState.Indeterminate,
                "evidence for another artifact manifest was accepted");
        });

        Check("negative evidence rejects rather than silently waiting", () =>
        {
            var rejected = NewEvidence(value: "invalid");
            Assert(AwaitGateEvaluator.Evaluate(contract, new[] { rejected }).State == AwaitGateState.Rejected,
                "negative evidence did not reject the gate");
        });

        Check("human adjudication requires explicit policy and adjudicator identity", () =>
        {
            Throws<ArgumentException>(() => new AwaitGateEvidence("review", "B", 1, NewContract(new[] { new AwaitGateRequirement("review", "Human review is approved", "approved", AwaitGateEvidenceKind.HumanAdjudication) }).ContractDigest,
                Provider, SourceSha, ManifestDigest, EvidenceDigest, AwaitGateEvidenceKind.HumanAdjudication,
                "approved", "approval-1"));
            var humanContract = NewContract(new[]
            {
                new AwaitGateRequirement("review", "Human review is approved", "approved",
                    AwaitGateEvidenceKind.HumanAdjudication)
            });
            var humanEvidence = new AwaitGateEvidence("review", "B", 1, humanContract.ContractDigest, Provider, SourceSha,
                ManifestDigest, EvidenceDigest, AwaitGateEvidenceKind.HumanAdjudication, "approved",
                "approval-1", authorityPolicyId: "release-review-v1", adjudicatorId: "authorized-reviewer");
            Assert(AwaitGateEvaluator.Evaluate(humanContract, new[] { humanEvidence }).IsSatisfied,
                "explicit human adjudication did not satisfy the requirement");
        });

        Check("an Await remains blocked when only one of multiple required gates is satisfied", () =>
        {
            var registration = new AwaitRegistration("await-1", Consumer, "dock-integration",
                new[]
                {
                    new AwaitGateReference("B", 1, contract.ContractDigest, Provider),
                    new AwaitGateReference("C", 1, NewContract(gateId: "C").ContractDigest, Provider)
                });
            var gateB = AwaitGateEvaluator.Evaluate(contract, new[] { machine });
            Assert(!registration.IsReady(new[] { gateB }),
                "partial gate satisfaction incorrectly unblocked dependent work");
        });

        Check("an Await becomes ready only when every exact referenced gate is satisfied", () =>
        {
            var registration = new AwaitRegistration("await-1", Consumer, "dock-integration",
                new[]
                {
                    new AwaitGateReference("B", 1, contract.ContractDigest, Provider),
                    new AwaitGateReference("C", 1, NewContract(gateId: "C").ContractDigest, Provider)
                });
            var gateB = AwaitGateEvaluator.Evaluate(contract, new[] { machine });
            var gateCContract = NewContract(gateId: "C");
            var gateC = AwaitGateEvaluator.Evaluate(gateCContract, new[] { NewEvidence(gateId: "C") });
            Assert(registration.IsReady(new[] { gateB, gateC }),
                "all satisfied prerequisites did not make the workstream eligible");
        });

        Check("Await rejects duplicate gate references and missing gate declarations", () =>
        {
            Throws<ArgumentException>(() => new AwaitRegistration("await-1", Consumer, "work",
                new[] { new AwaitGateReference("B", 1, contract.ContractDigest, Provider),
                    new AwaitGateReference("B", 1, contract.ContractDigest, Provider) }));
            Throws<ArgumentException>(() => new AwaitRegistration("await-1", Consumer, "work",
                Array.Empty<AwaitGateReference>()));
        });

        Check("gate evaluator rejects null evidence entries", () =>
            Throws<ArgumentException>(() => AwaitGateEvaluator.Evaluate(contract,
                new AwaitGateEvidence[] { null! })));

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS Await/Gate contracts {passed}: {name}");
        }
    }

    private static AwaitGateContract NewContract(IReadOnlyList<AwaitGateRequirement>? requirements = null,
        string gateId = "B") =>
        new(gateId, 1, Consumer, Provider, SourceSha, ManifestDigest, "policy-v1", Approval(),
            requirements ?? new[] { MachineRequirement() });

    private static AwaitGateApproval Approval() =>
        new("gate-approval-1", "gate-contract-approval-v1", "authorized-owner",
            AwaitGateApprovalKind.Human, DateTimeOffset.Parse("2026-10-31T16:00:00Z"));

    private static AwaitGateRequirement MachineRequirement() =>
        new("dimensions", "Ship dimensions and weight are validated", "valid", AwaitGateEvidenceKind.Machine);

    private static AwaitGateEvidence NewEvidence(string? providerSourceSha = null,
        string? manifestDigest = null, string value = "valid", string gateId = "B") =>
        new("dimensions", gateId, 1, NewContract(gateId: gateId).ContractDigest, Provider, providerSourceSha ?? SourceSha,
            manifestDigest ?? ManifestDigest, EvidenceDigest, AwaitGateEvidenceKind.Machine, value,
            "workflow-artifact:run-42:attempt-1", "42", 1);

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
