using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class TrxTestResultParserContractTests
{
    public static int Run()
    {
        var passed = 0;
        Check("TRX parser normalizes pass, failure, and not-executed outcomes", () =>
        {
            var report = TrxTestResultParser.Parse(Encoding.UTF8.GetBytes(ValidTrx));
            Assert(report.Schema == TrxTestResultParser.Schema, "schema identity was not set");
            Assert(report.Total == 3 && report.Passed == 1 && report.Failed == 1 &&
                report.Skipped == 1 && report.Unknown == 0, "TRX outcome counts were incorrect");
            Assert(report.Results[1].ErrorMessage == "expected true but was false",
                "failure message was not preserved");
            Assert(report.Results[1].StackTrace == "at Tests.Sample()", "stack trace was not preserved");
            Assert(report.DiagnosticTrust == TrxTestResultParser.DiagnosticTrustLabel,
                "diagnostic trust boundary was not labelled");
        });

        Check("TRX parser keeps unknown provider outcomes non-passing", () =>
        {
            var report = TrxTestResultParser.Parse(Encoding.UTF8.GetBytes(
                "<TestRun><Results><UnitTestResult testId=\"x\" executionId=\"e\" testName=\"T\" outcome=\"FutureOutcome\" /></Results></TestRun>"));
            Assert(report.Unknown == 1 && report.Passed == 0 && report.Failed == 0,
                "unknown provider outcome was upgraded to pass or failure");
            Assert(report.Results[0].ProviderOutcome == "unknown", "unknown outcome was not normalized");
        });

        Check("TRX parser bounds diagnostic strings and records truncation", () =>
        {
            var xml = "<TestRun><Results><UnitTestResult testId=\"x\" executionId=\"e\" testName=\"T\" outcome=\"Failed\"><Output><ErrorInfo><Message>123456789</Message></ErrorInfo></Output></UnitTestResult></Results></TestRun>";
            var report = TrxTestResultParser.Parse(Encoding.UTF8.GetBytes(xml), maximumDiagnosticCharacters: 4);
            Assert(report.Results[0].ErrorMessage == "1234" && report.Results[0].DiagnosticTruncated,
                "diagnostic text was not bounded");
        });

        Check("TRX parser rejects empty and oversized payloads", () =>
        {
            Throws<InvalidDataException>(() => TrxTestResultParser.Parse(ReadOnlyMemory<byte>.Empty));
            Throws<InvalidDataException>(() => TrxTestResultParser.Parse(new byte[32], maximumPayloadBytes: 16));
        });

        Check("TRX parser rejects malformed XML and missing Results", () =>
        {
            Throws<InvalidDataException>(() => TrxTestResultParser.Parse(Encoding.UTF8.GetBytes("<TestRun><Results>")));
            Throws<InvalidDataException>(() => TrxTestResultParser.Parse(Encoding.UTF8.GetBytes("<TestRun />")));
        });

        Check("TRX parser prohibits DTD and external entity processing", () =>
        {
            const string xml = "<!DOCTYPE TestRun [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><TestRun><Results><UnitTestResult testId=\"x\" executionId=\"e\" testName=\"&xxe;\" outcome=\"Passed\" /></Results></TestRun>";
            Throws<InvalidDataException>(() => TrxTestResultParser.Parse(Encoding.UTF8.GetBytes(xml)));
        });

        Check("TRX parser rejects result counts above the configured limit", () =>
        {
            const string xml = "<TestRun><Results><UnitTestResult testId=\"1\" executionId=\"a\" testName=\"A\" outcome=\"Passed\" /><UnitTestResult testId=\"2\" executionId=\"b\" testName=\"B\" outcome=\"Passed\" /></Results></TestRun>";
            Throws<InvalidDataException>(() => TrxTestResultParser.Parse(Encoding.UTF8.GetBytes(xml), maximumResults: 1));
        });

        Check("TRX parser validates positive limits", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => TrxTestResultParser.Parse(Encoding.UTF8.GetBytes(ValidTrx), maximumResults: 0));
            Throws<ArgumentOutOfRangeException>(() => TrxTestResultParser.Parse(Encoding.UTF8.GetBytes(ValidTrx), maximumDiagnosticCharacters: 0));
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS TRX {passed}: {name}");
        }
    }

    private const string ValidTrx = """
        <TestRun>
          <Results>
            <UnitTestResult testId="pass-id" executionId="pass-exec" testName="Tests.Passes" outcome="Passed" />
            <UnitTestResult testId="fail-id" executionId="fail-exec" testName="Tests.Fails" outcome="Failed">
              <Output><ErrorInfo><Message>expected true but was false</Message><StackTrace>at Tests.Sample()</StackTrace></ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult testId="skip-id" executionId="skip-exec" testName="Tests.Skipped" outcome="NotExecuted" />
          </Results>
        </TestRun>
        """;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
