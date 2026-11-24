using System.Text.Json;
namespace TheSingularityWorkshop.CiAi;

/// <summary>Normalized metadata for an authenticated GitHub Issue or Pull Request webhook.</summary>
/// <remarks>The host must authenticate the raw-body HMAC before parsing. Item content remains untrusted.</remarks>
public sealed record GitHubWorkItemEvent(
    int ContractVersion, string DeliveryId, string EventName, string Action,
    long RepositoryId, string Repository, string ItemKind, long ItemId, string ItemNodeId,
    int Number, Uri HtmlUrl, DateTimeOffset UpdatedAt,
    string? BaseRef, string? BaseSha, string? HeadRef, string? HeadSha)
{
    public string WorkItemIdentity => $"{RepositoryId}:{ItemKind}:{ItemNodeId}";

    /// <summary>Parses supported webhook metadata after host-side signature verification.</summary>
    public static GitHubWorkItemEvent Parse(string eventName, string deliveryId, ReadOnlySpan<byte> rawPayload)
    {
        RequireBounded(eventName, nameof(eventName), 64);
        RequireBounded(deliveryId, nameof(deliveryId), 128);
        if (rawPayload.IsEmpty || rawPayload.Length > 1024 * 1024)
            throw new ArgumentException("GitHub webhook payload must be between 1 byte and 1 MiB.", nameof(rawPayload));
        var isIssue = eventName == "issues";
        var isPullRequest = eventName == "pull_request";
        if (!isIssue && !isPullRequest)
            throw new NotSupportedException($"GitHub event '{eventName}' is not enabled for work-item dispatch.");
        try
        {
            using var document = JsonDocument.Parse(rawPayload.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            var action = RequiredString(root, "action", 64);
            var allowed = isIssue
                ? new[] { "opened", "reopened", "edited", "labeled", "assigned", "closed" }
                : new[] { "opened", "reopened", "edited", "ready_for_review", "synchronize", "converted_to_draft", "closed" };
            if (!allowed.Contains(action, StringComparer.Ordinal))
                throw new NotSupportedException($"GitHub {eventName} action '{action}' is not enabled for work-item dispatch.");
            var repository = RequiredObject(root, "repository");
            var repositoryId = RequiredPositiveInt64(repository, "id");
            var repositoryName = RequiredString(repository, "full_name", 200);
            if (!RepositoryIdentity.IsValid(repositoryName))
                throw new InvalidDataException("GitHub repository full_name is not canonical owner/repository identity.");
            var item = RequiredObject(root, isIssue ? "issue" : "pull_request");
            var itemId = RequiredPositiveInt64(item, "id");
            var nodeId = RequiredString(item, "node_id", 128);
            var number = RequiredPositiveInt32(item, "number");
            var urlText = RequiredString(item, "html_url", 512);
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var htmlUrl) ||
                htmlUrl.Scheme != Uri.UriSchemeHttps || htmlUrl.Host != "github.com" ||
                !string.Equals(htmlUrl.AbsolutePath.TrimEnd('/'),
                    $"/{repositoryName}/{(isIssue ? "issues" : "pull")}/{number}", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(htmlUrl.UserInfo) || !string.IsNullOrEmpty(htmlUrl.Query) ||
                !string.IsNullOrEmpty(htmlUrl.Fragment))
                throw new InvalidDataException("GitHub work-item URL does not identify the exact repository and item.");
            var updatedText = RequiredString(item, "updated_at", 64);
            if (!DateTimeOffset.TryParse(updatedText, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var updatedAt))
                throw new InvalidDataException("GitHub work-item updated_at timestamp is invalid.");
            string? baseRef = null, baseSha = null, headRef = null, headSha = null;
            if (isPullRequest)
            {
                var head = RequiredObject(item, "head");
                var @base = RequiredObject(item, "base");
                headRef = RequiredString(head, "ref", 256);
                headSha = RequiredCommitSha(head, "sha");
                baseRef = RequiredString(@base, "ref", 256);
                baseSha = RequiredCommitSha(@base, "sha");
            }
            return new GitHubWorkItemEvent(1, deliveryId, eventName, action, repositoryId, repositoryName,
                isIssue ? "issue" : "pull_request", itemId, nodeId, number, htmlUrl, updatedAt,
                baseRef, baseSha, headRef, headSha);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("GitHub work-item webhook payload must be valid JSON.", nameof(rawPayload), exception);
        }
    }

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : throw new InvalidDataException($"GitHub webhook is missing object '{name}'.");
    private static string RequiredString(JsonElement parent, string name, int max)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"GitHub webhook is missing string '{name}'.");
        var result = value.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(result) || result.Length > max || result.Any(char.IsControl))
            throw new InvalidDataException($"GitHub webhook string '{name}' is invalid or exceeds its bound.");
        return result;
    }
    private static long RequiredPositiveInt64(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) && result > 0
            ? result : throw new InvalidDataException($"GitHub webhook field '{name}' must be a positive integer.");
    private static int RequiredPositiveInt32(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) && result > 0
            ? result : throw new InvalidDataException($"GitHub webhook field '{name}' must be a positive integer.");
    private static string RequiredCommitSha(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name, 40);
        if (value.Length != 40 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Pull-request '{name}' must be a full 40-character commit SHA.");
        return value.ToLowerInvariant();
    }
    private static void RequireBounded(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl))
            throw new ArgumentException($"{name} must be non-empty, bounded, and control-free.", name);
    }
}