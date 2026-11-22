using System.Text.Json;
using TheSingularityWorkshop.CiAi;

if (args.Length != 1 || args[0] is not ("--stamp-readmes" or "--check-readmes"))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/CiAi.Repository -- --stamp-readmes|--check-readmes");
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
catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine($"Invalid CiAi repository manifest: {exception.Message}");
    return 2;
}

if (args[0] == "--stamp-readmes")
{
    var changed = CiAiReadmeBadgeStamper.StampRepositoryReadmes(root, configuration);
    Console.WriteLine($"Stamped {changed} README.md file(s) for {configuration.Repository}.");
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
