using TheSingularityWorkshop.CiAi;

if (args.Length != 2 || !long.TryParse(args[1], out var runId) || runId <= 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/TheSingularityWorkshop.CiAi.ObserveRun -- <owner/repository> <run-id>");
    return 64;
}

using var httpClient = new HttpClient();
var token = Environment.GetEnvironmentVariable("CIAI_GITHUB_TOKEN");
try
{
    var run = await new GitHubActionsRunClient(httpClient, token)
        .GetRunAsync(args[0], runId, CancellationToken.None);
    Console.WriteLine($"Repository:  {run.Repository}");
    Console.WriteLine($"Run ID:      {run.RunId}");
    Console.WriteLine($"Workflow:    {run.WorkflowName}");
    Console.WriteLine($"Commit:      {run.HeadSha}");
    Console.WriteLine($"Status:      {run.Status}");
    Console.WriteLine($"Conclusion:  {run.Conclusion}");
    Console.WriteLine($"Updated UTC: {run.UpdatedAt:O}");
    Console.WriteLine($"Evidence:    {run.EvidenceUrl}");
    Console.WriteLine($"Successful:  {run.IsSuccessful}");
    return 0;
}
catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"CiAi could not validate the requested run: {exception.Message}");
    return 1;
}
