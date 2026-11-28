using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class FileCiAiDependencyRegistrationLedgerStoreTests
{
    private const string WorkflowSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ciai-dependency-ledger-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileCiAiDependencyRegistrationLedgerStore(root);
            var first = CreateSnapshot("0123456789abcdef0123456789abcdef01234567", "TrentBest/Provider");
            var applied = store.ApplyVerifiedSnapshot(CreateVerification(first, 42));
            Assert(applied.Status == CiAiDependencyRegistrationApplyStatus.Applied, "initial snapshot was not applied");
            Assert(store.ApplyVerifiedSnapshot(CreateVerification(first, 42)).Status == CiAiDependencyRegistrationApplyStatus.Duplicate,
                "identical replay was not idempotent");

            var reopened = new FileCiAiDependencyRegistrationLedgerStore(root);
            var loaded = reopened.Load("TrentBest/Consumer");
            Assert(loaded is not null && loaded.SourceRevision == 42 &&
                loaded.Registrations.Single().TargetRepository == "TrentBest/Provider",
                "ledger did not survive store reconstruction");

            var stale = CreateSnapshot("1123456789abcdef0123456789abcdef01234567", "TrentBest/Stale");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(stale, 41)).Status == CiAiDependencyRegistrationApplyStatus.Stale,
                "older source run was not rejected");
            AssertThrows<ArgumentOutOfRangeException>(() =>
                CiAiDependencyRegistrationLedger.ApplyVerifiedSnapshot(null, stale, 0));

            var conflict = CreateSnapshot("2123456789abcdef0123456789abcdef01234567", "TrentBest/Conflict");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(conflict, 42)).Status == CiAiDependencyRegistrationApplyStatus.Conflict,
                "same-run changed snapshot was not rejected");

            var replacement = CreateSnapshot("3123456789abcdef0123456789abcdef01234567");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(replacement, 43)).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "newer complete empty snapshot was not applied");
            Assert(reopened.Load("TrentBest/Consumer")!.Registrations.Count == 0,
                "newer empty snapshot did not withdraw prior registrations");

            var other = CreateSnapshot("4123456789abcdef0123456789abcdef01234567", source: "TrentBest/Other");
            Assert(reopened.ApplyVerifiedSnapshot(CreateVerification(other, 7)).Status == CiAiDependencyRegistrationApplyStatus.Applied,
                "independent source ledger was not stored");
            Assert(reopened.ReadAll().Count == 2, "source ledgers were not enumerated independently");

            var corrupt = Directory.GetFiles(root, "*.json").OrderBy(path => path, StringComparer.Ordinal).First();
            var corruptJson = File.ReadAllText(corrupt);
            var alteredDigest = System.Text.RegularExpressions.Regex.Replace(corruptJson,
                "\"SnapshotSha256\": \"[0-9a-f]{64}\"",
                "\"SnapshotSha256\": \"" + new string('0', 64) + "\"");
            Assert(!string.Equals(corruptJson, alteredDigest, StringComparison.Ordinal),
                "corruption fixture did not locate the stored digest");
            File.WriteAllText(corrupt, alteredDigest);
            AssertThrows<InvalidDataException>(() => reopened.ReadAll());

            Console.WriteLine("PASS: 10 durable dependency ledger store and source-verification contract checks.");
            return 10;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CiAiDependencyRegistrationSnapshot CreateSnapshot(
        string sha, string? target = null, string source = "TrentBest/Consumer")
    {
        var builder = CiAi.ForRepository(source);
        if (target is not null) builder = builder.WithDependency(target);
        return CiAiDependencyRegistrationSnapshot.FromDefinition(builder.Build(), sha);
    }

    private static CiAiDependencyRegistrationSourceVerification CreateVerification(
        CiAiDependencyRegistrationSnapshot snapshot, long runId)
    {
        var sourceRef = "refs/heads/development";
        var provenanceJson = $$"""
        {
          "schema": "{{CiWorkflowProvenanceRecord.CurrentSchema}}",
          "repository": "{{snapshot.SourceRepository}}",
          "runId": {{runId}},
          "runAttempt": 1,
          "workflow": {
            "ref": "{{snapshot.SourceRepository}}/.github/workflows/ci.yml@{{sourceRef}}",
            "sha": "{{WorkflowSha}}"
          },
          "event": {
            "name": "push",
            "ref": "{{sourceRef}}",
            "sha": "{{snapshot.SourceCommitSha}}",
            "pullRequest": null
          },
          "checkout": {
            "ref": "{{sourceRef}}",
            "sha": "{{snapshot.SourceCommitSha}}"
          }
        }
        """;
        var bytes = Encoding.UTF8.GetBytes(provenanceJson);
        var provenance = CiWorkflowProvenanceRecord.ParseAndValidate(bytes, snapshot.SourceRepository,
            runId, 1, Convert.ToHexString(SHA256.HashData(bytes)));
        var policy = new CiWorkflowDefinitionTrustPolicy(snapshot.SourceRepository,
            ".github/workflows/ci.yml", sourceRef, WorkflowSha);
        var workflowBytes = Encoding.UTF8.GetBytes("name: trusted source CI");
        var trustDecision = policy.Evaluate(provenance, workflowBytes, workflowBytes);

        var job = new CiWorkflowJobObservation(100, runId, 1, "build-and-test", "completed", "success",
            new Uri($"https://github.com/{snapshot.SourceRepository}/actions/runs/{runId}/job/100"),
            Array.AsReadOnly(new[]
            {
                new CiWorkflowStepObservation(1, "Build and test", "completed", "success", "2026-11-28T00:00:00Z")
            }))
        {
            StartedAt = DateTimeOffset.Parse("2026-11-28T00:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-11-28T00:01:00Z")
        };
        var observedRun = new CiWorkflowRunEvidenceObservation(snapshot.SourceRepository,
            ".github/workflows/ci.yml", runId, 1, sourceRef, snapshot.SourceCommitSha,
            "push", "CI", CiRunStatus.Completed, CiRunConclusion.Success,
            new Uri($"https://github.com/{snapshot.SourceRepository}/actions/runs/{runId}"),
            DateTimeOffset.Parse("2026-11-28T00:02:00Z"),
            Array.AsReadOnly(new[] { job }), true, true);
        var packet = CiWorkflowEvidencePacket.FromValidatedProvenance(observedRun, provenance, trustDecision);
        var packetV2 = CiWorkflowEvidencePacketV2.FromPacket(packet,
            new[] { new CiWorkflowRequiredCheckBinding("build-and-test", "build-and-test") });
        return CiAiDependencyRegistrationSourceVerification.Verify(snapshot, packetV2, trustDecision,
            new[] { sourceRef });
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }
}
