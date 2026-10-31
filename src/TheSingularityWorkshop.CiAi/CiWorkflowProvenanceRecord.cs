using System.Security.Cryptography;
using System.Text.Json;

namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Validated workflow-side provenance exported by the trusted CI workflow.
/// The caller must bind the supplied digest to provider artifact metadata before parsing.
/// </summary>
public sealed record CiWorkflowProvenanceRecord
{
    public const string CurrentSchema = "ciai.workflow-provenance.v1";

    private CiWorkflowProvenanceRecord(
        string repository,
        long runId,
        int runAttempt,
        string workflowPath,
        string workflowRef,
        string workflowSha,
        string eventName,
        string eventRef,
        string eventSha,
        CiWorkflowPullRequestProvenance? pullRequest,
        string checkoutRef,
        string checkoutSha)
    {
        Repository = repository;
        RunId = runId;
        RunAttempt = runAttempt;
        WorkflowPath = workflowPath;
        WorkflowRef = workflowRef;
        WorkflowSha = workflowSha.ToLowerInvariant();
        EventName = eventName;
        EventRef = eventRef;
        EventSha = eventSha.ToLowerInvariant();
        PullRequest = pullRequest;
        CheckoutRef = checkoutRef;
        CheckoutSha = checkoutSha.ToLowerInvariant();
    }

    public string Repository { get; }
    public long RunId { get; }
    public int RunAttempt { get; }
    public string WorkflowPath { get; }
    public string WorkflowRef { get; }
    public string WorkflowSha { get; }
    public string EventName { get; }
    public string EventRef { get; }
    public string EventSha { get; }
    public CiWorkflowPullRequestProvenance? PullRequest { get; }
    public string CheckoutRef { get; }
    public string CheckoutSha { get; }

    /// <summary>
    /// Parses the extracted JSON payload only when its exact bytes match the trusted retriever's
    /// content SHA-256 and its repository/run/attempt identity matches the requested workflow attempt.
    /// For GitHub Actions artifacts, the host must first verify the provider-reported digest of the
    /// downloaded ZIP archive, safely extract this exact payload, and compute this content digest.
    /// </summary>
    public static CiWorkflowProvenanceRecord ParseAndValidate(
        ReadOnlyMemory<byte> artifactBytes,
        string expectedRepository,
        long expectedRunId,
        int expectedRunAttempt,
        string expectedContentSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentSha256);
        if (!RepositoryIdentity.IsValid(expectedRepository))
            throw new ArgumentException("Expected repository must use canonical owner/repository identifiers.", nameof(expectedRepository));
        if (expectedRunId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRunId));
        if (expectedRunAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRunAttempt));
        if (expectedContentSha256.Length != 64 || !expectedContentSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected artifact digest must be a full SHA-256 hexadecimal value.", nameof(expectedContentSha256));
        if (artifactBytes.IsEmpty)
            throw new InvalidDataException("Workflow provenance artifact is empty.");

        var actualDigest = Convert.ToHexString(SHA256.HashData(artifactBytes.Span));
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actualDigest),
            Convert.FromHexString(expectedContentSha256)))
            throw new InvalidDataException("Workflow provenance artifact digest does not match provider metadata.");

        using var document = JsonDocument.Parse(artifactBytes);
        var root = document.RootElement;
        ValidateNoDuplicateProperties(root);
        if (root.ValueKind != JsonValueKind.Object ||
            RequiredString(root, "schema") != CurrentSchema)
            throw new InvalidDataException("Workflow provenance schema is unsupported.");

        var repository = RequiredString(root, "repository");
        var runId = RequiredInt64(root, "runId");
        var runAttempt = RequiredInt32(root, "runAttempt");
        if (!string.Equals(repository, expectedRepository, StringComparison.OrdinalIgnoreCase) ||
            runId != expectedRunId || runAttempt != expectedRunAttempt)
            throw new InvalidDataException("Workflow provenance belongs to a different repository, run, or attempt.");

        var workflow = RequiredObject(root, "workflow");
        var workflowRef = RequiredWorkflowRef(workflow, "ref", expectedRepository);
        var workflowSeparator = workflowRef.LastIndexOf('@');
        var workflowPath = workflowRef[(expectedRepository.Length + 1)..workflowSeparator];
        var workflowSha = RequiredSha(workflow, "sha");

        var @event = RequiredObject(root, "event");
        var eventName = RequiredIdentity(@event, "name", 100);
        var eventRef = RequiredRef(@event, "ref");
        var eventSha = RequiredSha(@event, "sha");
        CiWorkflowPullRequestProvenance? pullRequest = null;
        if (@event.TryGetProperty("pullRequest", out var pr) && pr.ValueKind != JsonValueKind.Null)
        {
            if (pr.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Pull-request provenance must be an object or null.");
            var head = RequiredObject(pr, "head");
            var @base = RequiredObject(pr, "base");
            pullRequest = new CiWorkflowPullRequestProvenance(
                RequiredPositiveInt32(pr, "number"),
                RequiredRef(head, "ref"),
                RequiredSha(head, "sha"),
                RequiredRef(@base, "ref"),
                RequiredSha(@base, "sha"));
        }

        var checkout = RequiredObject(root, "checkout");
        var checkoutRef = RequiredRef(checkout, "ref");
        var checkoutSha = RequiredSha(checkout, "sha");

        if (string.Equals(eventName, "pull_request", StringComparison.Ordinal) != (pullRequest is not null))
            throw new InvalidDataException("Pull-request event identity and pull-request provenance are inconsistent.");

        return new CiWorkflowProvenanceRecord(
            repository, runId, runAttempt, workflowPath, workflowRef, workflowSha,
            eventName, eventRef, eventSha, pullRequest, checkoutRef, checkoutSha);
    }

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Workflow provenance contains duplicate property '{property.Name}'.");
                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item);
        }
    }

    private static JsonElement RequiredObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Workflow provenance is missing object '{property}'.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Workflow provenance is missing string '{property}'.");
        return value.GetString()!;
    }

    private static string RequiredIdentity(JsonElement parent, string property, int maximumLength)
    {
        var value = RequiredString(parent, property);
        if (value.Length > maximumLength || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace))
            throw new InvalidDataException($"Workflow provenance identity '{property}' is invalid.");
        return value;
    }

    private static string RequiredWorkflowRef(JsonElement parent, string property, string expectedRepository)
    {
        var value = RequiredString(parent, property);
        var separator = value.LastIndexOf('@');
        var path = separator > 0 ? value[..separator] : string.Empty;
        var prefix = expectedRepository + "/.github/workflows/";
        var relativePath = path.StartsWith(prefix, StringComparison.Ordinal)
            ? path[prefix.Length..]
            : string.Empty;
        var pathSegments = relativePath.Split('/');
        if (value.Length > 1024 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) ||
            value.Contains('\\') || value.Contains('#') || value.Contains('?') ||
            separator <= 0 || !value[(separator + 1)..].StartsWith("refs/", StringComparison.Ordinal) ||
            !path.StartsWith(prefix, StringComparison.Ordinal) ||
            relativePath.Length == 0 ||
            pathSegments.Any(segment => segment.Length == 0 || segment is "." or "..") ||
            !(path.EndsWith(".yml", StringComparison.Ordinal) ||
              path.EndsWith(".yaml", StringComparison.Ordinal)))
            throw new InvalidDataException("Workflow definition ref is invalid.");
        return value;
    }

    private static string RequiredRef(JsonElement parent, string property)
    {
        var value = RequiredString(parent, property);
        if (value.Length > 512 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) ||
            !value.StartsWith("refs/", StringComparison.Ordinal))
            throw new InvalidDataException($"Workflow provenance ref '{property}' is invalid.");
        return value;
    }

    private static string RequiredSha(JsonElement parent, string property)
    {
        var value = RequiredString(parent, property);
        if (value.Length != 40 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Workflow provenance SHA '{property}' is invalid.");
        return value;
    }

    private static long RequiredInt64(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || !value.TryGetInt64(out var parsed) || parsed <= 0)
            throw new InvalidDataException($"Workflow provenance integer '{property}' is invalid.");
        return parsed;
    }

    private static int RequiredInt32(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || !value.TryGetInt32(out var parsed) || parsed <= 0)
            throw new InvalidDataException($"Workflow provenance integer '{property}' is invalid.");
        return parsed;
    }

    private static int RequiredPositiveInt32(JsonElement parent, string property) => RequiredInt32(parent, property);
}

/// <summary>Pull-request source and target identities preserved independently from the merge event SHA.</summary>
public sealed record CiWorkflowPullRequestProvenance(
    int Number,
    string HeadRef,
    string HeadSha,
    string BaseRef,
    string BaseSha);
