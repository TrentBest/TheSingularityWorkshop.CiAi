using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

internal sealed class CiTrxResultWriter
{
    private readonly List<(string Name, bool Passed, string? Diagnostic)> results = new();
    public int ResultCount => results.Count;
    public int FailedCount => results.Count(item => !item.Passed);

    public void RecordPassed(string name, string? detail) => results.Add((name, true, detail));

    public void RecordFailed(string name, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var diagnostic = exception.ToString();
        if (diagnostic.Length > 8000) diagnostic = diagnostic[^8000..];
        results.Add((name, false, diagnostic));
    }

    private static string StableTestId(string name) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(name))[..16]).ToString("D");

    public void Write(string path)
    {
        if (results.Count == 0) throw new InvalidOperationException("No harness results were recorded.");
        if (results.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != results.Count)
            throw new InvalidOperationException("Harness result names must be unique for stable TRX identity.");
        var now = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        var root = new XElement("TestRun",
            new XAttribute("id", Guid.NewGuid().ToString("D")),
            new XAttribute("name", "CiAi Contract Harness"),
            new XAttribute("runUser", Environment.UserName),
            new XElement("Times", new XAttribute("creation", now), new XAttribute("queuing", now),
                new XAttribute("start", now), new XAttribute("finish", now)),
            new XElement("Results", results.Select(item =>
            {
                var id = Guid.NewGuid().ToString("D");
                var result = new XElement("UnitTestResult",
                    new XAttribute("executionId", id),
                    new XAttribute("testId", StableTestId(item.Name)),
                    new XAttribute("testName", item.Name),
                    new XAttribute("outcome", item.Passed ? "Passed" : "Failed"),
                    new XAttribute("duration", "00:00:00.000"),
                    new XAttribute("startTime", now),
                    new XAttribute("endTime", now));
                if (!item.Passed)
                    result.Add(new XElement("Output", new XElement("ErrorInfo",
                        new XElement("Message", "CiAi harness record failed."),
                        new XElement("StackTrace", item.Diagnostic ?? "No diagnostic was captured."))));
                return result;
            })),
            new XElement("ResultSummary", new XAttribute("outcome", FailedCount == 0 ? "Passed" : "Failed"),
                new XElement("Counters",
                    new XAttribute("total", results.Count),
                    new XAttribute("executed", results.Count),
                    new XAttribute("passed", results.Count - FailedCount),
                    new XAttribute("failed", FailedCount),
                    new XAttribute("error", 0), new XAttribute("timeout", 0),
                    new XAttribute("aborted", 0), new XAttribute("notExecuted", 0),
                    new XAttribute("inconclusive", 0), new XAttribute("notRunnable", 0),
                    new XAttribute("disconnected", 0), new XAttribute("warning", 0),
                    new XAttribute("passedButRunAborted", 0), new XAttribute("pending", 0),
                    new XAttribute("inProgress", 0))));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        document.Save(temporaryPath);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}
