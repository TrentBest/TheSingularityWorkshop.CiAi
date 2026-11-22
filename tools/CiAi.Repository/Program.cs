using System.Text.Json;
using TheSingularityWorkshop.CiAi;

if (args.Length != 1 || args[0] is not ("--preview-readmes" or "--apply-readmes" or "--stamp-readmes" or "--check-readmes"))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/CiAi.Repository -- --preview-readmes|--apply-readmes|--check-readmes");
    return 2;
}

var root = Directory.GetCurrentDirectory();
var manifestPath = Path.Combine(root, ".ciai", "repository.json");
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"CiAi repository manifest not found: {manifestPath}");
    return 2;
}

CiAiRepositoryConfiguration configuration;
try
{
    configuration = CiAiRepositoryConfiguration.ParseJson(File.ReadAllText(manifestPath));
}
catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
{
    Console.Error.WriteLine($"Invalid CiAi repository manifest: {exception.Message}");
    return 2;
}

var expectedRepository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
if (!string.IsNullOrWhiteSpace(expectedRepository) &&
    !string.Equals(expectedRepository, configuration.Repository, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Manifest repository '{configuration.Repository}' does not match workflow repository '{expectedRepository}'.");
    return 2;
}

if (args[0] is "--preview-readmes" or "--apply-readmes" or "--stamp-readmes")
{
    var plan = CiAiReadmeBadgeStamper.CreateReadmeStampPlan(root, configuration);
    Console.WriteLine($"README plan for {configuration.Repository}: {plan.Changes.Count} file(s) to update.");
    foreach (var change in plan.Changes) Console.WriteLine($" - {change.RelativePath}");
    if (args[0] == "--preview-readmes") return 0;
    var changed = CiAiReadmeBadgeStamper.ApplyReadmeStampPlan(plan);
    Console.WriteLine($"Applied {changed} README.md update(s).");
    return 0;
}

var stale = CiAiReadmeBadgeStamper.FindUnstampedRepositoryReadmes(root, configuration);
if (stale.Count == 0)
{
    Console.WriteLine($"All README.md files are stamped for {configuration.Repository}.");
    return 0;
}
Console.Error.WriteLine("README.md files need CiAi badge stamping:");
foreach (var path in stale) Console.Error.WriteLine($" - {path}");
return 1;
