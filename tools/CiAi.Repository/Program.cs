using System.Text.Json;
using TheSingularityWorkshop.CiAi;

if (args.Length != 1 || args[0] != "--check-manifest")
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/CiAi.Repository -- --check-manifest");
    return 2;
}

var path = Path.Combine(Directory.GetCurrentDirectory(), ".ciai", "repository.json");
if (!File.Exists(path))
{
    Console.Error.WriteLine($"CiAi repository manifest not found: {path}");
    return 2;
}

try
{
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
        Console.Error.WriteLine("Repository manifest is valid but not in canonical deterministic form. Regenerate it from the typed contract.");
        return 1;
    }

    Console.WriteLine($"PASS: {manifest.Definition.Repository} manifest schema {CiAiRepositoryManifest.CurrentSchema} is valid and canonical.");
    Console.WriteLine($"Badges: {manifest.Definition.Badges.Count}; dependencies: {manifest.Definition.Dependencies.Count}; extensions: {manifest.Definition.Extensions.Count}; observer: {manifest.ObserverProfile ?? "none"}");
    return 0;
}
catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"CiAi manifest validation failed: {exception.Message}");
    return 1;
}
