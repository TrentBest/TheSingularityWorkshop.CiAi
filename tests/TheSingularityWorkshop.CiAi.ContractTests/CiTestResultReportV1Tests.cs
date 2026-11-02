using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiTestResultReportV1Tests
{
    public static int Run()
    {
        var xml = Encoding.UTF8.GetBytes("""
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="id-1" testName="Passes" outcome="Passed" />
            <UnitTestResult testId="id-2" testName="Fails" outcome="Failed">
              <Output><ErrorInfo><Message>expected value</Message><StackTrace>actual trace</StackTrace></ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult testId="id-3" testName="Skipped" outcome="NotExecuted" />
            <UnitTestResult testId="id-4" testName="Unknown" outcome="Timeout" />
          </Results>
        </TestRun>
        """);
        var report = Parse(xml);
        Assert(report.Total == 4 && report.Passed == 1 && report.Failed == 1 &&
            report.Skipped == 1 && report.Other == 1, "TRX outcomes were not normalized distinctly");
        Assert(report.Repository == "TrentBest/CiAi" && report.RunId == 42 && report.RunAttempt == 2 &&
            report.JobId == 124 && report.ArtifactId == "trx-1", "provider-observed identity was not bound");
        Assert(report.Results[1].Message == "expected value" && report.Results[1].Actual == "actual trace",
            "bounded failure diagnostics were not extracted");
        Assert(report.Results.All(x => x.RecordSha256.Length == 64) && report.ContentSha256.Length == 64,
            "normalized content digests were not generated");

        Throws<InvalidDataException>(() => CiTestResultReportV1.ParseTrx(xml, new string('0', 64),
            Provenance(), 124, "test", "trx-1"));
        Throws<InvalidDataException>(() => CiTestResultReportV1.ParseTrx(xml, Digest(xml),
            Provenance(runId: 43), 124, "test", "trx-1"));
        Throws<InvalidDataException>(() => CiTestResultReportV1.ParseTrx(Encoding.UTF8.GetBytes(
            "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><TestRun>&e;</TestRun>"),
            Digest(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><TestRun>&e;</TestRun>")),
            Provenance(), 124, "test", "trx-1"));
        Throws<InvalidDataException>(() => CiTestResultReportV1.ParseTrx(Encoding.UTF8.GetBytes("<TestRun/>"),
            Digest(Encoding.UTF8.GetBytes("<TestRun/>")), Provenance(), 124, "test", "trx-1"));
        Throws<InvalidDataException>(() => CiTestResultReportV1.ParseTrx(new byte[CiTestResultReportV1.MaximumArtifactBytes + 1],
            Digest(new byte[CiTestResultReportV1.MaximumArtifactBytes + 1]), Provenance(), 124, "test", "trx-1"));

        Console.WriteLine("PASS: 8 bounded TRX normalization contract checks.");
        return 8;
    }

    private static CiTestResultReportV1 Parse(byte[] bytes) =>
        CiTestResultReportV1.ParseTrx(bytes, Digest(bytes), Provenance(), 124, "test-job", "trx-1");

    private static CiWorkflowProvenanceRecord Provenance()
    {
        var json = Encoding.UTF8.GetBytes("""
        {
          "schema":"ciai.workflow-provenance.v1",
          "repository":"TrentBest/CiAi",
          "runId":42,
          "runAttempt":2,
          "workflow":{"ref":"TrentBest/CiAi/.github/workflows/ci.yml@refs/heads/development","sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
          "event":{"name":"workflow_dispatch","ref":"refs/heads/development","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","pullRequest":null},
          "checkout":{"ref":"refs/heads/development","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}
        }
        """);
        return CiWorkflowProvenanceRecord.ParseAndValidate(json, "TrentBest/CiAi", 42, 2, Digest(json));
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
