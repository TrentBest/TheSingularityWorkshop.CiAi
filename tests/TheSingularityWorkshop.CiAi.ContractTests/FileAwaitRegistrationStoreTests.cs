using TheSingularityWorkshop.CiAi;

internal static class FileAwaitRegistrationStoreTests
{
    public static int Run()
    {
        var passed = 0;
        var root = Path.Combine(Path.GetTempPath(), "ciai-await-registration-tests-" + Guid.NewGuid().ToString("N"));
        var registration = new AwaitRegistration("await-1", "TrentBest/The", "dock-integration",
            new[]
            {
                new AwaitGateReference("B", 1, ContractDigest("TrentBest/The", "TrentBest/MyVR", "B"),
                    "TrentBest/MyVR"),
                new AwaitGateReference("C", 2, ContractDigest("TrentBest/The", "TrentBest/FSM_COS", "C"),
                    "TrentBest/FSM_COS")
            });
        try
        {
            Check("Await registration persists and reloads after store recreation", () =>
            {
                var store = new FileAwaitRegistrationStore(root);
                Assert(store.Save(registration) == AwaitRegistrationStoreOutcome.Saved,
                    "registration was not saved");
                var loaded = new FileAwaitRegistrationStore(root).Load("await-1");
                Assert(loaded is not null && loaded.ConsumerRepository == registration.ConsumerRepository &&
                    loaded.WorkstreamId == registration.WorkstreamId &&
                    loaded.RequiredGates.SequenceEqual(registration.RequiredGates),
                    "reloaded registration changed its consumer or prerequisites");
            });

            Check("identical Await registration retries are idempotent", () =>
                Assert(new FileAwaitRegistrationStore(root).Save(registration) ==
                    AwaitRegistrationStoreOutcome.AlreadyExists, "identical retry was not idempotent"));

            Check("Await ID cannot be reused for another workstream or gate set", () =>
            {
                var changed = new AwaitRegistration("await-1", "TrentBest/The", "different-workstream",
                    new[] { new AwaitGateReference("B", 1,
                        ContractDigest("TrentBest/OtherConsumer", "TrentBest/MyVR", "B"), "TrentBest/MyVR") });
                Assert(new FileAwaitRegistrationStore(root).Save(changed) == AwaitRegistrationStoreOutcome.Conflict,
                    "conflicting Await registration overwrote the original");
            });

            Check("registration enumeration is stable and includes independent Await consumers", () =>
            {
                var other = new AwaitRegistration("await-2", "TrentBest/OtherConsumer", "other-work",
                    new[] { new AwaitGateReference("B", 1, "TrentBest/MyVR") });
                var store = new FileAwaitRegistrationStore(root);
                Assert(store.Save(other) == AwaitRegistrationStoreOutcome.Saved, "second consumer was not saved");
                var all = store.ReadAll();
                Assert(all.Count == 2 && all[0].AwaitId == "await-1" && all[1].AwaitId == "await-2",
                    "multiple Await registrations were not independently retained");
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
            Console.WriteLine($"PASS Await registration store {passed}: {name}");
        }
    }

    private static string ContractDigest(string consumer, string provider, string gateId) =>
        new AwaitGateContract(gateId, 1, consumer, provider,
            "abcdef0123456789abcdef0123456789abcdef01",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "policy-v1",
            new[] { new AwaitGateRequirement("dimensions", "Dimensions validated", "valid",
                AwaitGateEvidenceKind.Machine) }).ContractDigest;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
