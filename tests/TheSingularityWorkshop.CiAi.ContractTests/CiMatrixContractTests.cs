using TheSingularityWorkshop.CiAi;

internal static class CiMatrixContractTests
{
    public static int Run()
    {
        var passed = 0;

        Check("matrix expansion is deterministic and creates content-derived cell identities", () =>
        {
            var draft = TwoByTwo();
            var first = CiMatrixExpander.Expand(draft);
            var second = CiMatrixExpander.Expand(draft);
            Assert(first.Cells.Count == 4 && first.RequiredCellCount == 4,
                "Cartesian expansion did not produce all four cells");
            Assert(first.DefinitionDigest == second.DefinitionDigest &&
                first.Cells.Select(cell => cell.CellId).SequenceEqual(second.Cells.Select(cell => cell.CellId)),
                "identical matrix definitions produced different identities");
            Assert(first.Cells.Select(cell => cell.CellId).Distinct(StringComparer.Ordinal).Count() == 4,
                "expanded cells did not have unique identities");
        });

        Check("matrix exclusions remove partial combinations and preserve reasons", () =>
        {
            var draft = new CiMatrixDefinitionDraft("platform-matrix",
                new[]
                {
                    new CiMatrixDimensionDraft("framework", new[] { "net8.0", "net9.0" }),
                    new CiMatrixDimensionDraft("os", new[] { "linux", "windows" })
                },
                new[]
                {
                    new CiMatrixExclusionDraft(
                        new Dictionary<string, string> { ["os"] = "windows" },
                        "Windows runner is not available for this validation profile.")
                });
            var expansion = CiMatrixExpander.Expand(draft);
            Assert(expansion.CartesianCombinationCount == 4 && expansion.ExcludedCombinationCount == 2 &&
                expansion.RequiredCellCount == 2, "partial exclusion did not remove the expected cells");
            Assert(expansion.Exclusions.Count == 1 &&
                expansion.Exclusions[0].Reason.Contains("Windows runner", StringComparison.Ordinal),
                "exclusion reason was not retained in the expansion record");
            Assert(expansion.Cells.All(cell => cell.Values["os"] == "linux"),
                "an excluded Windows cell remained in the expansion");
        });

        Check("matrix expansion rejects Cartesian products above the configured bound", () =>
        {
            var draft = new CiMatrixDefinitionDraft("too-large",
                new[]
                {
                    new CiMatrixDimensionDraft("framework", new[] { "net8.0", "net9.0" }),
                    new CiMatrixDimensionDraft("os", new[] { "linux", "windows" })
                },
                Array.Empty<CiMatrixExclusionDraft>(),
                MaximumCells: 3);
            Throws<InvalidDataException>(() => CiMatrixExpander.Expand(draft));
        });

        Check("matrix expansion rejects malformed dimensions and duplicate values", () =>
        {
            Throws<ArgumentException>(() => CiMatrixExpander.Expand(new CiMatrixDefinitionDraft(
                "bad-dimensions",
                new[]
                {
                    new CiMatrixDimensionDraft("os", new[] { "linux" }),
                    new CiMatrixDimensionDraft("os", new[] { "windows" })
                },
                Array.Empty<CiMatrixExclusionDraft>())));
            Throws<ArgumentException>(() => CiMatrixExpander.Expand(new CiMatrixDefinitionDraft(
                "duplicate-values",
                new[] { new CiMatrixDimensionDraft("os", new[] { "linux", "linux" }) },
                Array.Empty<CiMatrixExclusionDraft>())));
            Throws<ArgumentException>(() => CiMatrixExpander.Expand(new CiMatrixDefinitionDraft(
                "bad-name",
                new[] { new CiMatrixDimensionDraft("Operating System", new[] { "linux" }) },
                Array.Empty<CiMatrixExclusionDraft>())));
        });

        Check("matrix expansion rejects unknown exclusion dimensions and values", () =>
        {
            var unknownDimension = new CiMatrixDefinitionDraft("unknown-dimension",
                new[] { new CiMatrixDimensionDraft("os", new[] { "linux" }) },
                new[] { new CiMatrixExclusionDraft(new Dictionary<string, string> { ["runtime"] = "net8.0" }, "not declared") });
            var unknownValue = new CiMatrixDefinitionDraft("unknown-value",
                new[] { new CiMatrixDimensionDraft("os", new[] { "linux" }) },
                new[] { new CiMatrixExclusionDraft(new Dictionary<string, string> { ["os"] = "windows" }, "not declared") });
            Throws<ArgumentException>(() => CiMatrixExpander.Expand(unknownDimension));
            Throws<ArgumentException>(() => CiMatrixExpander.Expand(unknownValue));
        });

        Check("matrix expansion rejects exclusions that remove every combination", () =>
        {
            var draft = new CiMatrixDefinitionDraft("all-excluded",
                new[] { new CiMatrixDimensionDraft("os", new[] { "linux" }) },
                new[] { new CiMatrixExclusionDraft(new Dictionary<string, string> { ["os"] = "linux" }, "No supported runner.") });
            Throws<InvalidDataException>(() => CiMatrixExpander.Expand(draft));
        });

        Check("matrix aggregator passes only when every exact required cell succeeds", () =>
        {
            var expansion = CiMatrixExpander.Expand(TwoByTwo());
            var results = expansion.Cells.Select(cell =>
                new CiMatrixCellResult(cell.CellId, expansion.DefinitionDigest, CiStepOutcome.Succeeded)).ToArray();
            var report = CiMatrixResultAggregator.Aggregate(expansion, results);
            Assert(report.IsPassing && report.RequiredCellCount == 4 &&
                report.Cells.All(cell => cell.Status == CiMatrixCellStatus.Passed),
                "complete exact-cell success did not pass");
        });

        Check("matrix aggregator treats a missing required cell as incomplete", () =>
        {
            var expansion = CiMatrixExpander.Expand(TwoByTwo());
            var results = expansion.Cells.Take(3).Select(cell =>
                new CiMatrixCellResult(cell.CellId, expansion.DefinitionDigest, CiStepOutcome.Succeeded));
            var report = CiMatrixResultAggregator.Aggregate(expansion, results);
            Assert(!report.IsPassing && report.Cells.Count(cell => cell.Status == CiMatrixCellStatus.Missing) == 1,
                "missing matrix cell was treated as passing");
        });

        Check("matrix aggregator rejects duplicate cell results", () =>
        {
            var expansion = CiMatrixExpander.Expand(SingleCell());
            var cell = expansion.Cells[0];
            var result = new CiMatrixCellResult(cell.CellId, expansion.DefinitionDigest, CiStepOutcome.Succeeded);
            var report = CiMatrixResultAggregator.Aggregate(expansion, new[] { result, result });
            Assert(!report.IsPassing && report.Cells[0].Status == CiMatrixCellStatus.Duplicate,
                "duplicate cell evidence was accepted");
        });

        Check("matrix aggregator rejects results bound to another definition digest", () =>
        {
            var expansion = CiMatrixExpander.Expand(SingleCell());
            var result = new CiMatrixCellResult(expansion.Cells[0].CellId, new string('a', 64), CiStepOutcome.Succeeded);
            var report = CiMatrixResultAggregator.Aggregate(expansion, new[] { result });
            Assert(!report.IsPassing && report.Cells[0].Status == CiMatrixCellStatus.Unknown &&
                report.Cells[0].ReasonCode == "matrix.definition-digest-mismatch",
                "mismatched definition digest was accepted");
        });

        Check("matrix aggregation preserves every non-passing outcome distinctly", () =>
        {
            var outcomes = new[]
            {
                CiStepOutcome.Succeeded,
                CiStepOutcome.Failed,
                CiStepOutcome.Skipped,
                CiStepOutcome.Cancelled,
                CiStepOutcome.Blocked,
                CiStepOutcome.Indeterminate
            };
            var expansion = CiMatrixExpander.Expand(new CiMatrixDefinitionDraft("outcome-matrix",
                new[] { new CiMatrixDimensionDraft("leg", outcomes.Select(outcome => outcome.ToString()).ToArray()) },
                Array.Empty<CiMatrixExclusionDraft>()));
            var results = expansion.Cells.Zip(outcomes, (cell, outcome) =>
                new CiMatrixCellResult(cell.CellId, expansion.DefinitionDigest, outcome));
            var report = CiMatrixResultAggregator.Aggregate(expansion, results);
            Assert(report.Cells.Select(cell => cell.Status).SequenceEqual(new[]
                {
                    CiMatrixCellStatus.Passed,
                    CiMatrixCellStatus.Failed,
                    CiMatrixCellStatus.Skipped,
                    CiMatrixCellStatus.Cancelled,
                    CiMatrixCellStatus.Blocked,
                    CiMatrixCellStatus.Indeterminate
                }), "matrix outcome states were collapsed or reordered");
            Assert(!report.IsPassing, "non-passing matrix outcomes were accepted");
        });

        Check("matrix aggregator rejects unknown cells and invalid outcome values", () =>
        {
            var expansion = CiMatrixExpander.Expand(SingleCell());
            var unknown = new CiMatrixCellResult("cell-not-in-matrix", expansion.DefinitionDigest, CiStepOutcome.Succeeded);
            var invalid = new CiMatrixCellResult(expansion.Cells[0].CellId, expansion.DefinitionDigest, (CiStepOutcome)99);
            var report = CiMatrixResultAggregator.Aggregate(expansion, new[] { unknown, invalid });
            Assert(!report.IsPassing && report.Errors.Any(error => error.StartsWith("matrix.result-unknown-cell:", StringComparison.Ordinal)) &&
                report.Cells[0].Status == CiMatrixCellStatus.Unknown,
                "unknown cell or outcome evidence was accepted");
        });

        Check("matrix reports render deterministic JSON and Markdown", () =>
        {
            var expansion = CiMatrixExpander.Expand(TwoByTwo());
            var results = expansion.Cells.Select(cell =>
                new CiMatrixCellResult(cell.CellId, expansion.DefinitionDigest, CiStepOutcome.Failed, "assertion | mismatch"));
            var report = CiMatrixResultAggregator.Aggregate(expansion, results);
            Assert(CiMatrixReportRenderer.RenderExpansionJson(expansion) == CiMatrixReportRenderer.RenderExpansionJson(expansion),
                "expansion JSON was not deterministic");
            var json = CiMatrixReportRenderer.RenderEvaluationJson(report);
            var markdown = CiMatrixReportRenderer.RenderEvaluationMarkdown(report);
            Assert(json.Contains("\"schema\": \"ciai.matrix-evaluation.v1\"", StringComparison.Ordinal),
                "evaluation JSON omitted its schema identity");
            Assert(markdown.Contains("assertion \\| mismatch", StringComparison.Ordinal) &&
                markdown.Contains("not execute jobs", StringComparison.Ordinal),
                "Markdown report did not safely render diagnostics or preserve its boundary");
        });

        Console.WriteLine($"PASS: {passed} CiAi matrix contract checks.");
        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS {passed}: {name}");
        }
    }

    private static CiMatrixDefinitionDraft TwoByTwo() =>
        new("platform-matrix",
            new[]
            {
                new CiMatrixDimensionDraft("framework", new[] { "net8.0", "net9.0" }),
                new CiMatrixDimensionDraft("os", new[] { "linux", "windows" })
            },
            Array.Empty<CiMatrixExclusionDraft>());

    private static CiMatrixDefinitionDraft SingleCell() =>
        new("single-cell", new[] { new CiMatrixDimensionDraft("os", new[] { "linux" }) },
            Array.Empty<CiMatrixExclusionDraft>());

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
