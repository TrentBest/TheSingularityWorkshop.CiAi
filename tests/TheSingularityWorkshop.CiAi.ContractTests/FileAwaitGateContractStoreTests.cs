using TheSingularityWorkshop.CiAi;

internal static class FileAwaitGateContractStoreTests
{
    public static int Run()
    {
        var passed = 0;
        var root = Path.Combine(Path.GetTempPath(), "ciai-gate-contract-tests-" + Guid.NewGuid().ToString("N"));
        var contract = CreateContract();
        try
        {
            Check("approved gate contract persists and reloads with its canonical digest", () =>
            {
                var store = new FileAwaitGateContractStore(root);
                Assert(store.Save(contract) == AwaitGateContractStoreOutcome.Saved,
                    "gate contract was not saved");
                var loaded = new FileAwaitGateContractStore(root).Load(
                    contract.ProviderRepository, contract.GateId, contract.Version);
                Assert(loaded is not null && loaded.ContractDigest == contract.ContractDigest &&
                    loaded.Approval.ApprovalReference == contract.Approval.ApprovalReference &&
                    loaded.Requirements.SequenceEqual(contract.Requirements),
                    "reloaded gate contract changed its approval or acceptance requirements");
            });

            Check("identical gate contract retry is idempotent", () =>
                Assert(new FileAwaitGateContractStore(root).Save(contract) ==
                    AwaitGateContractStoreOutcome.AlreadyExists, "identical gate retry was not idempotent"));

            Check("same provider gate version cannot be rebound to a different contract", () =>
            {
                var changed = CreateContract(policyVersion: "policy-v2");
                Assert(new FileAwaitGateContractStore(root).Save(changed) ==
                    AwaitGateContractStoreOutcome.Conflict,
                    "existing gate version was silently rebound");
            });

            Check("new gate version is independently persisted", () =>
            {
                var next = CreateContract(version: 2);
                var store = new FileAwaitGateContractStore(root);
                Assert(store.Save(next) == AwaitGateContractStoreOutcome.Saved,
                    "new gate version was not saved");
                Assert(store.ReadAll().Count == 2, "gate contract versions were not retained independently");
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS Await gate contract store {passed}: {name}");
        }
    }

    private static AwaitGateContract CreateContract(int version = 1, string policyVersion = "policy-v1") =>
        new("B", version, "TrentBest/The", "TrentBest/MyVR",
            "abcdef0123456789abcdef0123456789abcdef01",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            policyVersion,
            new AwaitGateApproval("approval-1", "gate-approval-v1", "authorized-owner",
                AwaitGateApprovalKind.Human, DateTimeOffset.Parse("2026-10-31T16:00:00Z")),
            new[] { new AwaitGateRequirement("dimensions", "Dimensions and weight are validated",
                "valid", AwaitGateEvidenceKind.Machine) });

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
