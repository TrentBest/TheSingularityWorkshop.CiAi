<!-- The Singularity Workshop moniker -->
<p align="center"><a href="https://github.com/TrentBest"><img src="https://raw.githubusercontent.com/TrentBest/TheSingularityWorkshop.Visualize/development/docs/assets/moniker.svg" alt="The Singularity Workshop animated moniker" width="720" /></a></p>

# CiAi Matrix Workflow Domain

## 01 — What

A CI matrix is a declared set of configuration dimensions whose Cartesian product defines the work that must be validated. Typical dimensions include target framework, runtime, operating system, architecture, configuration, or a repository-specific environment.

A matrix definition has four parts:

- **Dimensions:** stable names and ordered, non-empty value sets.
- **Exclusions:** explicit partial combinations that are intentionally unsupported, each with a reason.
- **Expansion bound:** a strict maximum on the Cartesian product before exclusions.
- **Required cells:** every cell remaining after exclusions is required in this initial contract.

The first implementation slice introduces `CiMatrixDefinitionDraft`, `CiMatrixExpander`, `CiMatrixCellResult`, and `CiMatrixResultAggregator`. It is provider-neutral and does not launch jobs.

## 02 — Why

A matrix is not complete merely because a workflow reports success. The declared set and observed set must agree.

A trustworthy matrix contract must prevent several common false passes:

- A required operating-system or framework leg was never created.
- A leg was skipped or cancelled but counted as successful.
- The same cell reported twice and one result silently overwrote the other.
- A result from another matrix definition was attached to this candidate.
- An unsupported combination disappeared without a documented exclusion.
- An enormous Cartesian product consumed unbounded memory or runner capacity.

The core invariant is:

> Every non-excluded cell in the exact expanded definition has exactly one result bound to that definition's digest, and every required result is successful.

## 03 — How

### Bounded expansion

`CiMatrixExpander.Expand` validates stable matrix and dimension identifiers, unique dimension names and values, explicit exclusions, and configured bounds. The default maximum is 256 Cartesian combinations; the absolute maximum is 1,024. The bound applies before exclusions, so a large expansion cannot be hidden behind an exclusion list.

Each expanded cell receives a content-derived SHA-256 identity from its sorted dimension/value pairs. The matrix definition receives its own digest, which includes dimension ordering and values, exclusions and reasons, and the expansion limit. Cell results carry this definition digest.

Exclusions can match one or more dimensions. For example, excluding `os=windows` removes every combination containing that value. The reason is retained in the expansion report and contributes to the definition digest.

### Fail-closed aggregation

The aggregator preserves these cell states:

| Status | Meaning |
|---|---|
| `Passed` | Exactly one result reports success for this definition and cell |
| `Failed` | The cell reported failure |
| `Missing` | No result was observed for a required cell |
| `Duplicate` | More than one result claims the same required cell |
| `Skipped` | The provider or executor skipped the cell |
| `Cancelled` | The cell was cancelled |
| `Blocked` | The cell was blocked by policy or a prerequisite |
| `Indeterminate` | The outcome cannot be established |
| `Unknown` | The result has an invalid outcome or mismatched definition digest |

Unknown cell IDs are retained as report errors. A report passes only when every required cell is present exactly once, every result is bound to the exact definition digest, and every cell is `Passed`.

### Example

```csharp
var matrix = new CiMatrixDefinitionDraft(
    "library-validation",
    new[]
    {
        new CiMatrixDimensionDraft("framework", new[] { "net8.0", "net9.0" }),
        new CiMatrixDimensionDraft("os", new[] { "linux", "windows" })
    },
    new[]
    {
        new CiMatrixExclusionDraft(
            new Dictionary<string, string> { ["os"] = "windows" },
            "Windows runners are not available for this validation profile.")
    });

var expansion = CiMatrixExpander.Expand(matrix);

// The provider/host runs each expanded cell and returns exactly one result per cell.
var results = expansion.Cells.Select(cell =>
    new CiMatrixCellResult(
        cell.CellId,
        expansion.DefinitionDigest,
        CiStepOutcome.Succeeded));

var report = CiMatrixResultAggregator.Aggregate(expansion, results);
var json = CiMatrixReportRenderer.RenderEvaluationJson(report);
var markdown = CiMatrixReportRenderer.RenderEvaluationMarkdown(report);
```

The example's successful results are illustrative input. The expander does not execute tests, and the aggregator does not query a provider.

## 04 — Evidence and reporting

The expansion report preserves the matrix ID, definition digest, raw Cartesian combination count, excluded combination count, explicit exclusion rules and reasons, and every required cell's identity and values.

The evaluation report preserves each cell's status, observed outcome, stable reason code, and bounded diagnostic. It also reports unknown cell results that do not belong to the expanded definition.

Two machine-readable schemas are emitted:

- `ciai.matrix-expansion.v1`
- `ciai.matrix-evaluation.v1`

The Markdown report is deterministic and escapes table delimiters in diagnostics. These reports describe matrix definition and evidence; they do not authorize publication or deployment.

## 05 — Limits and next work

This is a **partial matrix-domain implementation**, not an end-to-end matrix runner.

Not implemented in this slice:

- Provider-specific matrix YAML generation or workflow dispatch.
- Job scheduling, parallelism, cancellation orchestration, retries, and timeouts.
- Include/override rules, conditional dimensions, optional cells, or dynamic matrix discovery.
- GitHub Actions job/run ingestion and exact provider-job-to-cell mapping.
- Cross-run aggregation or durable matrix-result storage.

Those capabilities require separate contracts and host/provider integration. Do not mark the matrix family fully implemented until the exact expanded required set is executed, ingested from authoritative provider evidence, and evaluated end-to-end.

<p align="center"><img src="https://raw.githubusercontent.com/TrentBest/FSM_API/master/Documentation/Branding/TheSingularityWorkshop.png" alt="The Singularity Workshop" width="480" /></p>
