using System.Text.Json;
using TheSingularityWorkshop.CiAi;

if (args.Length != 1 || args[0] is not ("--check-manifest" or "--check-progress"))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/CiAi.Repository -- --check-manifest|--check-progress");
    return 2;
}

try
{
    if (args[0] == "--check-manifest")
        return CheckManifest();
    return CheckProgress();
}
catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException or FormatException)
{
    Console.Error.WriteLine($"CiAi repository validation failed: {exception.Message}");
    return 1;
}

int CheckManifest()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), ".ciai", "repository.json");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"CiAi repository manifest not found: {path}");
        return 2;
    }

    var source = File.ReadAllText(path);
    var manifest = CiAiRepositoryManifest.ParseJson(source);
    var expectedRepository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
    if (!string.IsNullOrWhiteSpace(expectedRepository) &&
        !string.Equals(expectedRepository, manifest.Definition.Repository, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Manifest repository '{manifest.Definition.Repository}' does not match '{expectedRepository}'.");
        return 1;
    }

    if (!string.Equals(source.Replace("\r\n", "\n", StringComparison.Ordinal), manifest.ToJson(), StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Repository manifest is valid but not in canonical deterministic form.");
        return 1;
    }

    Console.WriteLine($"PASS: {manifest.Definition.Repository} manifest schema {CiAiRepositoryManifest.CurrentSchema} is valid and canonical.");
    return 0;
}

int CheckProgress()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), ".ciai", "progress.json");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"CiAi progress declaration not found: {path}");
        return 2;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var root = document.RootElement;
    Require(root.ValueKind == JsonValueKind.Object, "root must be an object");
    Require(RequiredString(root, "schema") == "ciai.progress.v1", "unsupported progress schema");
    var repository = RequiredString(root, "repository");
    Require(repository.Contains('/') && repository.IndexOf('/') == repository.LastIndexOf('/'),
        "repository must use owner/repository identity");
    var expectedRepository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
    Require(string.IsNullOrWhiteSpace(expectedRepository) ||
        string.Equals(expectedRepository, repository, StringComparison.OrdinalIgnoreCase),
        $"progress repository '{repository}' does not match '{expectedRepository}'");
    _ = RequiredString(root, "title");
    Require(RequiredString(root, "method") == "weighted-workstreams-v1", "unsupported calculation method");
    Require(DateTimeOffset.TryParse(RequiredString(root, "updatedUtc"), out var updated) &&
        updated.Offset == TimeSpan.Zero, "updatedUtc must be an explicit UTC timestamp");

    Require(root.TryGetProperty("workstreams", out var workstreams) &&
        workstreams.ValueKind == JsonValueKind.Array && workstreams.GetArrayLength() > 0,
        "workstreams must be a non-empty array");

    var ids = new HashSet<string>(StringComparer.Ordinal);
    var totalWeight = 0;
    decimal weightedPoints = 0;
    var fullyAssessed = true;
    foreach (var item in workstreams.EnumerateArray())
    {
        Require(item.ValueKind == JsonValueKind.Object, "each workstream must be an object");
        var id = RequiredString(item, "id");
        Require(ids.Add(id), $"duplicate workstream id '{id}'");
        _ = RequiredString(item, "label");
        Require(item.TryGetProperty("weight", out var weightElement) &&
            weightElement.TryGetInt32(out var weight) && weight > 0, $"workstream '{id}' weight must be a positive integer");
        totalWeight = checked(totalWeight + weight);

        Require(item.TryGetProperty("completionPercent", out var completion), $"workstream '{id}' requires completionPercent");
        int? score = completion.ValueKind == JsonValueKind.Null ? null :
            completion.TryGetInt32(out var parsed) ? parsed : throw new InvalidDataException($"workstream '{id}' completionPercent must be an integer or null");
        Require(score is null || score is >= 0 and <= 100, $"workstream '{id}' completionPercent must be between 0 and 100");

        var status = RequiredString(item, "assessmentStatus");
        Require(status is "not-assessed" or "estimated" or "reviewed", $"workstream '{id}' has unknown assessmentStatus");
        Require(item.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Array,
            $"workstream '{id}' evidence must be an array");
        if (status == "not-assessed")
        {
            Require(score is null, $"unassessed workstream '{id}' must have a null score");
            fullyAssessed = false;
        }
        else
        {
            Require(score is not null, $"assessed workstream '{id}' requires a score");
            if (status == "reviewed")
                Require(evidence.GetArrayLength() > 0, $"reviewed workstream '{id}' requires evidence references");
            if (score is not null) weightedPoints += weight * score.Value;
            if (status != "reviewed") fullyAssessed = false;
        }

        foreach (var reference in evidence.EnumerateArray())
        {
            Require(reference.ValueKind == JsonValueKind.Object, $"workstream '{id}' evidence entries must be objects");
            _ = RequiredString(reference, "kind");
            var uriText = RequiredString(reference, "uri");
            Require(Uri.TryCreate(uriText, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                $"workstream '{id}' evidence URI must be HTTPS");
        }
    }

    Require(totalWeight == 100, $"workstream weights must total 100 (found {totalWeight})");
    Console.WriteLine($"PASS: {repository} progress schema ciai.progress.v1 is valid; {ids.Count} workstreams, weights total 100.");
    if (fullyAssessed)
        Console.WriteLine($"Weighted readiness: {weightedPoints / 100m:0.##}% (reviewed workstreams only).");
    else
        Console.WriteLine("Weighted readiness: unavailable — at least one workstream is unassessed or not reviewed.");
    Console.WriteLine("Evidence references are review pointers, not automatic verification or release authorization.");
    return 0;
}

static string RequiredString(JsonElement parent, string name)
{
    Require(parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()), $"'{name}' must be a non-empty string");
    return value.GetString()!;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
