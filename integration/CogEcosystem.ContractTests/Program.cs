using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;
using TheSingularityWorkshop.CiAi.CogA;
using TheSingularityWorkshop.CiAi.CogB;
using TheSingularityWorkshop.CiAi.CogC;
using TheSingularityWorkshop.CiAi.CogD;
using TheSingularityWorkshop.CiAi.CogE;
using TheSingularityWorkshop.ProtocolAi;

var checks = 0;
var now = DateTimeOffset.Parse("2026-10-24T00:30:00Z");
const string sourceSha = "abcdef0123456789abcdef0123456789abcdef01";
const string provider = "TrentBest/UmlProvider";
const string target = "TrentBest/Gui";
const string client = "client-ci";
var input = Encoding.UTF8.GetBytes("pinned source snapshot");
var inputDigest = Digest(input);
var output = Encoding.UTF8.GetBytes("source-derived UML artifact");
var outputDigest = Digest(output);

// CogA: discover an explicitly declared, revision-pinned capability.
var catalog = new CapabilityCatalog();
catalog.Register(new CapabilityDescriptor("uml.generate", "1.0.0", provider, sourceSha, 1,
    ["text/plain"], ["image/svg+xml", "text/markdown"]));
Assert(catalog.TryFind("uml.generate", "1.0.0", provider, out var descriptor) && descriptor!.Enabled,
    "CogA did not resolve the exact capability declaration");
Pass("CogA catalog resolves exact pinned capability");

// CogD: authorization is separate from catalog discovery.
var policy = new CapabilityPolicy();
policy.Add(new CapabilityPolicyRule(client, target, "uml.generate", "1.0.0", provider,
    now.AddHours(-1), now.AddHours(1)));
var decision = policy.Evaluate(new CapabilityAuthorizationRequest(client, target, "uml.generate", "1.0.0", provider, now));
Assert(decision.IsAllowed, "CogD denied an exact authorized request");
Pass("CogD authorizes only the exact in-window scope");

// CogB: record a bounded request pinned to source and input digest.
var request = new CapabilityRequest("request-1", "operation-1", "idem-1", client,
    descriptor!.CapabilityId, descriptor.CapabilityVersion, descriptor.ProviderRepository,
    target, sourceSha, inputDigest, 1, 1024, now, now.AddHours(1));
var requestBook = new CapabilityRequestBook();
Assert(ReferenceEquals(request, requestBook.Submit(request)), "CogB failed to record request");
Assert(ReferenceEquals(request, requestBook.Submit(new CapabilityRequest("retry-id", "operation-1", "idem-1", client,
    "uml.generate", "1.0.0", provider, target, sourceSha, inputDigest, 1, 1024, now, now.AddHours(1)))),
    "CogB did not suppress an identical idempotent retry");
Pass("CogB records pinned request and suppresses identical retry");

// CogC: returned result must match the exact request, provider, source, and contract.
var result = new CapabilityResult(request.RequestId, provider, "artifact-1", "v1", outputDigest,
    sourceSha, CapabilityResultStatus.Published, 1, 42);
var correlation = ResultCorrelator.Validate(
    new CapabilityRequestExpectation(request.RequestId, provider, sourceSha, 1), result);
Assert(correlation.Count == 0, "CogC rejected matching result metadata");
Pass("CogC correlates provider result to request");

// Parent CiAi: verify the bytes against the immutable artifact manifest.
var manifest = new ArtifactManifest(request.RequestId, request.OperationId, request.IdempotencyKey,
    provider, request.CapabilityId, request.CapabilityVersion, descriptor.ImplementationRevision,
    "artifact-1", "v1", sourceSha, "SHA-256", outputDigest, "application/octet-stream", output.Length,
    new Uri("https://provider.example/artifacts/artifact-1"), now, now.AddHours(1), "temporary", client, 42);
var notice = new ArtifactReadyNotice(1, "notice-1", client, manifest, now.AddMinutes(1));
Assert(ReferenceEquals(notice.Manifest, manifest) && notice.RecipientIdentity == client,
    "parent CiAi rejected the intended recipient or changed the manifest reference");
Pass("Parent CiAi binds the ready notice to the intended client and manifest");

var verification = ArtifactPayloadVerifier.Verify(manifest, output, request.MaximumOutputBytes);
Assert(verification.IsVerified, "parent CiAi rejected matching artifact bytes");
var receipt = new ArtifactReceiptAcknowledgement(1, "ack-1", "request-1:artifact-1:v1",
    request.RequestId, "artifact-1", "v1", outputDigest, client, true, true, now.AddMinutes(2),
    ArtifactLocalValidationOutcome.Accepted, provider, 42, sourceSha);
Assert(receipt.VerifyAgainst(manifest).Count == 0, "parent CiAi rejected matching receipt provenance");
Pass("Parent CiAi verifies payload integrity and reconciles receipt provenance");

var handoffEvidence = CiWorkflowDomainEvaluator.Evaluate("artifact-handoff", provider, sourceSha,
    new[] { new CiRequiredEvidence("digest"), new CiRequiredEvidence("receipt") },
    new[]
    {
        new CiEvidenceFact("digest", ReleaseEvidenceState.Passed, provider, sourceSha),
        new CiEvidenceFact("receipt", ReleaseEvidenceState.Passed, provider, sourceSha)
    });
Assert(handoffEvidence.IsPassing, "CiAi artifact-handoff domain did not accept complete Cog-backed evidence");
Pass("CiAi artifact-handoff domain reconciles Cog-backed artifact, digest, and receipt evidence");

var mismatchedHandoff = CiWorkflowDomainEvaluator.Evaluate("artifact-handoff", provider, sourceSha,
    new[] { new CiRequiredEvidence("digest"), new CiRequiredEvidence("receipt") },
    new[]
    {
        new CiEvidenceFact("digest", ReleaseEvidenceState.Passed, provider, sourceSha),
        new CiEvidenceFact("receipt", ReleaseEvidenceState.Passed, provider, "123456789abcdef0123456789abcdef012345678")
    });
Assert(!mismatchedHandoff.IsPassing, "cross-commit Cog receipt was accepted by the artifact-handoff domain");
Pass("CiAi artifact-handoff domain rejects cross-commit Cog evidence");

// CogE: project independent facts without inventing missing transitions.
var observations = new[]
{
    new LifecycleObservation(request.RequestId, "artifact-1", "v1", LifecycleFact.Requested, now, client),
    new LifecycleObservation(request.RequestId, "artifact-1", "v1", LifecycleFact.Published, now.AddMinutes(1), provider, outputDigest),
    new LifecycleObservation(request.RequestId, "artifact-1", "v1", LifecycleFact.Retrieved, now.AddMinutes(2), client, outputDigest),
    new LifecycleObservation(request.RequestId, "artifact-1", "v1", LifecycleFact.IntegrityVerified, now.AddMinutes(2), client, outputDigest),
    new LifecycleObservation(request.RequestId, "artifact-1", "v1", LifecycleFact.LocallyValidated, now.AddMinutes(3), client, outputDigest),
    new LifecycleObservation(request.RequestId, "artifact-1", "v1", LifecycleFact.Acknowledged, now.AddMinutes(3), client, outputDigest),
    new LifecycleObservation(request.RequestId, "artifact-1", "v1", LifecycleFact.Accepted, now.AddMinutes(4), client, outputDigest)
};
var projection = LifecycleProjector.Project(request.RequestId, "artifact-1", "v1", observations);
Assert(projection.Has(LifecycleFact.Accepted) && projection.Has(LifecycleFact.IntegrityVerified) &&
       !projection.Has(LifecycleFact.Reclaimed) && projection.Diagnostics.Count == 0,
    "CogE projection lost facts, invented cleanup, or reported a contradiction");
Pass("CogE projects distinct lifecycle facts and does not infer reclamation");


var mapperRunSnapshot = new CiRunSnapshot(
    provider, 84, "Build and test", sourceSha, CiRunStatus.Completed, CiRunConclusion.Success,
    new Uri("https://github.com/TrentBest/UmlProvider/actions/runs/84"), now);
var mapperRun = new CiWorkflowRunObservation(
    mapperRunSnapshot, 2, ".github/workflows/build.yml", "push", Array.Empty<int>());
var mapperJob = new CiWorkflowJobObservation(
    901, 84, 2, "build-and-test", "completed", "success",
    new Uri("https://github.com/TrentBest/UmlProvider/actions/runs/84/job/901"),
    Array.Empty<CiWorkflowStepObservation>());
var mapperEvidence = new CiProtocolEvidenceBinding(
    "refs/heads/development", sourceSha, sourceSha,
    ".github/workflows/build.yml@refs/heads/development", sourceSha,
    "https://github.com/TrentBest/UmlProvider/actions/runs/84/job/901",
    new string('b', 64));
var mappedEnvelope = GitHubActionsProtocolSourceEventMapper.CreateEnvelope(
    mapperRun, mapperJob, mapperEvidence, "message-map-84", "candidate-map-84", now);
var mappedPayload = ProtocolTypedEventEnvelope.DeserializePayload<ProtocolSourceEvent>(
    ProtocolEventEnvelopeJson.Deserialize(ProtocolEventEnvelopeJson.Serialize(mappedEnvelope)));
Assert(mappedPayload.Outcome == ProtocolSourceEventOutcome.Succeeded &&
       mappedEnvelope.Provenance?.WorkflowRunId == 84 &&
       mappedEnvelope.Provenance?.WorkflowRunAttempt == 2 &&
       mappedEnvelope.Provenance?.ObservedJobIdentity == "901:build-and-test",
    "GitHub Actions mapper did not preserve exact run-attempt/job provenance");
Pass("GitHub Actions mapper emits a typed source event with exact run-attempt/job identity");

var mismatchedCheckoutRejected = false;
try
{
    _ = GitHubActionsProtocolSourceEventMapper.CreateEnvelope(
        mapperRun, mapperJob, mapperEvidence with { TestedCheckoutSha = new string('c', 40) },
        "message-map-bad", "candidate-map-bad", now);
}
catch (InvalidDataException) { mismatchedCheckoutRejected = true; }
Assert(mismatchedCheckoutRejected, "mapper accepted an unverified/different tested checkout SHA");
Pass("GitHub Actions mapper rejects a tested checkout that differs from the candidate");

var missingDigestRejected = false;
try
{
    _ = GitHubActionsProtocolSourceEventMapper.CreateEnvelope(
        mapperRun, mapperJob, mapperEvidence with { EvidenceSha256 = string.Empty },
        "message-map-no-digest", "candidate-map-no-digest", now);
}
catch (InvalidDataException) { missingDigestRejected = true; }
Assert(missingDigestRejected, "mapper accepted missing evidence digest");
Pass("GitHub Actions mapper refuses to emit an envelope without an evidence digest");

var pendingJob = mapperJob with { Status = "in_progress", Conclusion = null };
var pendingEnvelope = GitHubActionsProtocolSourceEventMapper.CreateEnvelope(
    mapperRun, pendingJob, mapperEvidence, "message-map-pending", "candidate-map-pending", now);
var pendingPayload = ProtocolTypedEventEnvelope.DeserializePayload<ProtocolSourceEvent>(
    ProtocolEventEnvelopeJson.Deserialize(ProtocolEventEnvelopeJson.Serialize(pendingEnvelope)));
Assert(pendingPayload.Outcome == ProtocolSourceEventOutcome.Unknown,
    "mapper converted an incomplete provider observation into success");
Pass("GitHub Actions mapper preserves incomplete provider observations as Unknown");

var failedJob = mapperJob with { Conclusion = "failure" };
var failedEnvelope = GitHubActionsProtocolSourceEventMapper.CreateEnvelope(
    mapperRun, failedJob, mapperEvidence, "message-map-failed", "candidate-map-failed", now);
var failedPayload = ProtocolTypedEventEnvelope.DeserializePayload<ProtocolSourceEvent>(
    ProtocolEventEnvelopeJson.Deserialize(ProtocolEventEnvelopeJson.Serialize(failedEnvelope)));
Assert(failedPayload.Outcome == ProtocolSourceEventOutcome.Failed,
    "mapper failed to preserve a provider-reported failed job");
Pass("GitHub Actions mapper maps a provider-reported failed job to Failed");


Console.WriteLine($"PASS: {checks} cross-Cog integration checks.");

void Pass(string message) { checks++; Console.WriteLine($"PASS {checks}: {message}"); }
static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }


// ProtocolAi v1: CiAi emits a typed, exact-candidate source event. The envelope is
// transport metadata only; the CI provider snapshot remains the evidence authority.
var provenance = new ProtocolCandidateProvenance(
    provider, "refs/heads/development", sourceSha, sourceSha, sourceSha,
    ".github/workflows/build.yml", sourceSha, 42, 1, "build-and-test",
    "https://github.com/TrentBest/UmlProvider/actions/runs/42",
    new string('a', 64));
var sourceEvent = new ProtocolSourceEvent(
    "github-actions", "workflow.completed", "run:42/attempt:1",
    ProtocolSourceEventOutcome.Succeeded, "Provider reported a completed successful run.");
var sourceEnvelope = ProtocolTypedEventEnvelope.Create(
    "message-ci-42", "candidate-ci-42", "ciai", sourceEvent, now, provenance);
var serializedSource = ProtocolEventEnvelopeJson.Serialize(sourceEnvelope);
var receivedEnvelope = ProtocolEventEnvelopeJson.Deserialize(serializedSource);
var receivedSource = ProtocolTypedEventEnvelope.DeserializePayload<ProtocolSourceEvent>(receivedEnvelope);
Assert(receivedEnvelope.Schema == ProtocolEventEnvelope.CurrentSchema &&
       receivedEnvelope.SchemaVersion == 1 &&
       receivedEnvelope.Kind == ProtocolEventKind.SourceEvent &&
       receivedEnvelope.Provenance?.CandidateSha == sourceSha &&
       receivedEnvelope.Provenance.TestedCheckoutSha == sourceSha &&
       receivedSource.Outcome == ProtocolSourceEventOutcome.Succeeded,
    "CiAi failed to round-trip the ProtocolAi v1 exact-candidate source event");
Pass("CiAi emits and reads a ProtocolAi v1 source event with exact candidate provenance");

var unknownSource = new ProtocolSourceEvent(
    "github-actions", "workflow.completed", "run:43/attempt:1",
    ProtocolSourceEventOutcome.Unknown, "Provider evidence was unavailable or incomplete.");
var unknownEnvelope = ProtocolTypedEventEnvelope.Create(
    "message-ci-43", "candidate-ci-43", "ciai", unknownSource, now, provenance);
var unknownRoundTrip = ProtocolTypedEventEnvelope.DeserializePayload<ProtocolSourceEvent>(
    ProtocolEventEnvelopeJson.Deserialize(ProtocolEventEnvelopeJson.Serialize(unknownEnvelope)));
Assert(unknownRoundTrip.Outcome == ProtocolSourceEventOutcome.Unknown,
    "CiAi converted unknown provider evidence into a successful source outcome");
Pass("CiAi preserves Unknown as non-success in the ProtocolAi event contract");

var mismatchedKindRejected = false;
try
{
    _ = ProtocolTypedEventEnvelope.DeserializePayload<ProtocolObserverEvaluation>(receivedEnvelope);
}
catch (ArgumentException)
{
    mismatchedKindRejected = true;
}
Assert(mismatchedKindRejected, "ProtocolAi accepted a source event as an observer evaluation");
Pass("CiAi rejects event-kind/payload mismatches through ProtocolAi typed reader");


if (string.Equals(Environment.GetEnvironmentVariable("CIAI_VERIFY_PROVIDER_EVIDENCE"), "1", StringComparison.Ordinal))
{
    await VerifyLiveProviderEvidenceAsync();
}

async Task VerifyLiveProviderEvidenceAsync()
{
    var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
        ?? throw new InvalidOperationException("GITHUB_REPOSITORY is unavailable.");
    var headSha = Environment.GetEnvironmentVariable("GITHUB_SHA")
        ?? throw new InvalidOperationException("GITHUB_SHA is unavailable.");
    var runId = long.Parse(Environment.GetEnvironmentVariable("GITHUB_RUN_ID")
        ?? throw new InvalidOperationException("GITHUB_RUN_ID is unavailable."));
    var attempt = int.Parse(Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT")
        ?? throw new InvalidOperationException("GITHUB_RUN_ATTEMPT is unavailable."));
    var candidateRef = Environment.GetEnvironmentVariable("GITHUB_REF")
        ?? throw new InvalidOperationException("GITHUB_REF is unavailable.");
    var token = Environment.GetEnvironmentVariable("GH_TOKEN")
        ?? throw new InvalidOperationException("GH_TOKEN is unavailable.");

    using var http = new HttpClient();
    var runCollector = new GitHubActionsWorkflowRunCollector(http, token);
    var runCollection = await runCollector.GetRunsForCommitAsync(repository, headSha);
    Assert(!runCollection.IsTruncated, "Live workflow-run discovery was truncated.");
    var run = runCollection.Runs.SingleOrDefault(item =>
        item.Run.RunId == runId && item.RunAttempt == attempt)
        ?? throw new InvalidDataException("The current GitHub Actions run/attempt was not found by exact commit identity.");

    var jobCollector = new GitHubActionsWorkflowJobCollector(http, token);
    var jobs = await jobCollector.GetJobsForAttemptAsync(repository, runId, attempt);
    Assert(!jobs.IsTruncated, "Live workflow-job discovery was truncated.");
    var job = jobs.Jobs.SingleOrDefault(item => item.Name == "vertical-contract")
        ?? throw new InvalidDataException("The completed vertical-contract job was not found in this run attempt.");

    var evidenceCollector = new GitHubActionsWorkflowEvidenceCollector(http, token);
    var verified = await evidenceCollector.CollectAsync(
        repository, run, job, repository, repository);
    Assert(string.Equals(verified.TestedCheckoutSha, headSha, StringComparison.OrdinalIgnoreCase),
        "Provider job logs prove a tested checkout SHA different from the candidate.");
    var binding = new CiProtocolEvidenceBinding(
        candidateRef, headSha, verified.TestedCheckoutSha,
        verified.WorkflowDefinitionRef, verified.WorkflowDefinitionSha256,
        verified.EvidenceReference, verified.EvidenceSha256);
    var envelope = GitHubActionsProtocolSourceEventMapper.CreateEnvelope(
        run, job, binding, $"github-actions:{runId}:{attempt}:{job.JobId}",
        $"candidate:{repository}:{headSha}", DateTimeOffset.UtcNow);
    var payload = ProtocolTypedEventEnvelope.DeserializePayload<ProtocolSourceEvent>(
        ProtocolEventEnvelopeJson.Deserialize(ProtocolEventEnvelopeJson.Serialize(envelope)));
    Assert(payload.Outcome == ProtocolSourceEventOutcome.Succeeded,
        "Live GitHub provider evidence did not produce a successful source event.");
    Console.WriteLine($"PASS: live GitHub evidence bound run {runId}, attempt {attempt}, job {job.JobId}; checkout {verified.TestedCheckoutSha}; workflow SHA-256 {verified.WorkflowDefinitionSha256}; log SHA-256 {verified.EvidenceSha256}.");
}
