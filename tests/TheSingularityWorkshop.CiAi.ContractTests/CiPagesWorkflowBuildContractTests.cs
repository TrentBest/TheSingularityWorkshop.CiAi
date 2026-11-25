using TheSingularityWorkshop.CiAi;

internal static class CiPagesWorkflowBuildContractTests
{
    public static int Run()
    {
        var passed = 0;
        var pins = new CiPagesActionPins(
            new string('a', 40), new string('b', 40), new string('c', 40), new string('d', 40));
        var built = new CiPagesPublicationPlanDraft(
            "TrentBest/Consumer", "site", "development",
            ArtifactName: "consumer-pages",
            BuildCommand: "npm ci && npm run build",
            OutputPath: "dist");

        Check("configured Pages build runs before artifact validation and packaging", () =>
        {
            var yaml = CiPagesWorkflowGenerator.Generate(built, pins);
            Assert(yaml.Contains("npm ci && npm run build", StringComparison.Ordinal),
                "configured repository build command was omitted");
            Assert(yaml.Contains("test -f \"site/dist/index.html\"", StringComparison.Ordinal),
                "generated output entry point was not validated");
            Assert(yaml.Contains("path: \"site/dist\"", StringComparison.Ordinal),
                "the validated output directory was not packaged");
            Assert(yaml.IndexOf("npm ci && npm run build", StringComparison.Ordinal) <
                   yaml.IndexOf("test -f \"site/dist/index.html\"", StringComparison.Ordinal),
                "output validation ran before the build");
        });

        Check("Pages deploy job consumes only the completed build job artifact", () =>
        {
            var yaml = CiPagesWorkflowGenerator.Generate(built, pins);
            Assert(yaml.Contains("  build:", StringComparison.Ordinal) &&
                   yaml.Contains("  deploy:", StringComparison.Ordinal) &&
                   yaml.Contains("    needs: build", StringComparison.Ordinal),
                "build and deploy jobs are not separated");
            Assert(yaml.Contains("name: \"consumer-pages\"", StringComparison.Ordinal) &&
                   yaml.Contains("artifact_name: \"consumer-pages\"", StringComparison.Ordinal),
                "upload and deploy do not reference the same named Pages artifact");
        });

        Check("static Pages sources remain supported without an invented build command", () =>
        {
            var staticDraft = new CiPagesPublicationPlanDraft(
                "TrentBest/StaticConsumer", "site", "development");
            var yaml = CiPagesWorkflowGenerator.Generate(staticDraft, pins);
            Assert(!yaml.Contains("      - name: Build site", StringComparison.Ordinal),
                "static source unexpectedly received an invented build command");
            Assert(yaml.Contains("test -f \"site/index.html\"", StringComparison.Ordinal),
                "static site entry point was not validated");
        });

        Check("multiline build commands and unsafe output paths fail closed", () =>
        {
            var multiline = built with { BuildCommand = "npm run build\nother-command" };
            var traversal = built with { OutputPath = "../escape" };
            Assert(!CiPagesPublicationPlan.Expand(multiline).IsValid,
                "multiline build command was accepted");
            Assert(!CiPagesPublicationPlan.Expand(traversal).IsValid,
                "output traversal was accepted");
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
