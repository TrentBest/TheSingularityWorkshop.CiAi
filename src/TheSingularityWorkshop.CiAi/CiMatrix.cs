using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheSingularityWorkshop.CiAi;

/// <summary>Untrusted named dimension and its allowed values.</summary>
public sealed record CiMatrixDimensionDraft(string Name, IReadOnlyList<string> Values);

/// <summary>Untrusted partial combination that is intentionally excluded from the expanded matrix.</summary>
public sealed record CiMatrixExclusionDraft(IReadOnlyDictionary<string, string> Match, string Reason);

/// <summary>Untrusted provider-neutral matrix definition.</summary>
public sealed record CiMatrixDefinitionDraft(
    string Id,
    IReadOnlyList<CiMatrixDimensionDraft> Dimensions,
    IReadOnlyList<CiMatrixExclusionDraft> Exclusions,
    int MaximumCells = CiMatrixExpander.DefaultMaximumCells);

/// <summary>One immutable expanded matrix cell with a stable content-derived identity.</summary>
public sealed class CiMatrixCell
{
    internal CiMatrixCell(string cellId, IReadOnlyDictionary<string, string> values)
    {
        CellId = cellId;
        Values = values;
    }

    public string CellId { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
}

/// <summary>Immutable expansion of one validated matrix definition.</summary>
public sealed class CiMatrixExpansionReport
{
    internal CiMatrixExpansionReport(
        string matrixId,
        string definitionDigest,
        int cartesianCombinationCount,
        int excludedCombinationCount,
        IReadOnlyList<CiMatrixCell> cells)
    {
        MatrixId = matrixId;
        DefinitionDigest = definitionDigest;
        CartesianCombinationCount = cartesianCombinationCount;
        ExcludedCombinationCount = excludedCombinationCount;
        Cells = cells;
    }

    public string MatrixId { get; }
    public string DefinitionDigest { get; }
    public int CartesianCombinationCount { get; }
    public int ExcludedCombinationCount { get; }
    public IReadOnlyList<CiMatrixCell> Cells { get; }
    public int RequiredCellCount => Cells.Count;
}

/// <summary>
/// Validates and deterministically expands a bounded Cartesian matrix. The maximum applies before exclusions,
/// so exclusions cannot be used to hide an unbounded expansion request.
/// </summary>
public static class CiMatrixExpander
{
    public const int DefaultMaximumCells = 256;
    public const int AbsoluteMaximumCells = 1024;
    public const int MaximumDimensions = 12;
    public const int MaximumValuesPerDimension = 128;
    public const int MaximumValueLength = 128;
    public const int MaximumExclusions = 256;
    public const int MaximumReasonLength = 512;

    private static readonly Regex StableId = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    public static CiMatrixExpansionReport Expand(CiMatrixDefinitionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Id) || !StableId.IsMatch(draft.Id))
            throw new ArgumentException("Matrix ID must be a stable lowercase identifier.", nameof(draft));
        if (draft.MaximumCells is < 1 or > AbsoluteMaximumCells)
            throw new ArgumentOutOfRangeException(nameof(draft), $"Maximum cells must be between 1 and {AbsoluteMaximumCells}.");
        if (draft.Dimensions is null || draft.Dimensions.Count is < 1 or > MaximumDimensions)
            throw new ArgumentException($"A matrix must declare between 1 and {MaximumDimensions} dimensions.", nameof(draft));

        var dimensions = draft.Dimensions.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        long cartesianCount = 1;
        foreach (var dimension in dimensions)
        {
            if (dimension is null || string.IsNullOrWhiteSpace(dimension.Name) || !StableId.IsMatch(dimension.Name))
                throw new ArgumentException("Every matrix dimension must have a stable lowercase name.", nameof(draft));
            if (!names.Add(dimension.Name))
                throw new ArgumentException($"Matrix dimension '{dimension.Name}' is duplicated.", nameof(draft));
            if (dimension.Values is null || dimension.Values.Count is < 1 or > MaximumValuesPerDimension)
                throw new ArgumentException($"Dimension '{dimension.Name}' must have between 1 and {MaximumValuesPerDimension} values.", nameof(draft));
            if (dimension.Values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > MaximumValueLength))
                throw new ArgumentException($"Dimension '{dimension.Name}' contains an empty or oversized value.", nameof(draft));
            if (dimension.Values.Distinct(StringComparer.Ordinal).Count() != dimension.Values.Count)
                throw new ArgumentException($"Dimension '{dimension.Name}' contains duplicate values.", nameof(draft));

            cartesianCount = checked(cartesianCount * dimension.Values.Count);
            if (cartesianCount > draft.MaximumCells)
                throw new InvalidDataException($"Matrix Cartesian expansion ({cartesianCount}) exceeds the configured limit ({draft.MaximumCells}).");
        }

        var exclusions = draft.Exclusions?.ToArray() ?? Array.Empty<CiMatrixExclusionDraft>();
        if (exclusions.Length > MaximumExclusions)
            throw new ArgumentException($"A matrix cannot declare more than {MaximumExclusions} exclusions.", nameof(draft));

        var dimensionByName = dimensions.ToDictionary(dimension => dimension.Name, StringComparer.Ordinal);
        var canonicalExclusions = new HashSet<string>(StringComparer.Ordinal);
        var validatedExclusions = new List<(IReadOnlyDictionary<string, string> Match, string Reason)>();
        foreach (var exclusion in exclusions)
        {
            if (exclusion is null || exclusion.Match is null || exclusion.Match.Count == 0)
                throw new ArgumentException("Every exclusion must match at least one dimension.", nameof(draft));
            if (string.IsNullOrWhiteSpace(exclusion.Reason) || exclusion.Reason.Length > MaximumReasonLength)
                throw new ArgumentException("Every exclusion must include a bounded non-empty reason.", nameof(draft));

            var match = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in exclusion.Match)
            {
                if (!dimensionByName.TryGetValue(pair.Key, out var dimension))
                    throw new ArgumentException($"Exclusion references unknown dimension '{pair.Key}'.", nameof(draft));
                if (!dimension.Values.Contains(pair.Value, StringComparer.Ordinal))
                    throw new ArgumentException($"Exclusion value '{pair.Value}' is not declared for dimension '{pair.Key}'.", nameof(draft));
                match.Add(pair.Key, pair.Value);
            }

            var canonical = JsonSerializer.Serialize(match);
            if (!canonicalExclusions.Add(canonical))
                throw new ArgumentException("Duplicate matrix exclusions are not allowed.", nameof(draft));
            validatedExclusions.Add((new ReadOnlyDictionary<string, string>(match), exclusion.Reason.Trim()));
        }

        var definitionPayload = new
        {
            id = draft.Id,
            maximumCells = draft.MaximumCells,
            dimensions = dimensions.Select(dimension => new
            {
                name = dimension.Name,
                values = dimension.Values.ToArray()
            }).ToArray(),
            exclusions = validatedExclusions
                .Select(exclusion => new
                {
                    match = exclusion.Match.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new { name = pair.Key, value = pair.Value }).ToArray(),
                    reason = exclusion.Reason
                })
                .OrderBy(exclusion => JsonSerializer.Serialize(exclusion.match), StringComparer.Ordinal)
                .ToArray()
        };
        var definitionBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definitionPayload));
        var definitionDigest = Convert.ToHexString(SHA256.HashData(definitionBytes)).ToLowerInvariant();

        var cells = new List<CiMatrixCell>((int)cartesianCount);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        ExpandDimension(0);

        if (cells.Count == 0)
            throw new InvalidDataException("Matrix exclusions removed every possible cell.");

        return new CiMatrixExpansionReport(
            draft.Id,
            definitionDigest,
            (int)cartesianCount,
            (int)cartesianCount - cells.Count,
            Array.AsReadOnly(cells.ToArray()));

        void ExpandDimension(int dimensionIndex)
        {
            if (dimensionIndex == dimensions.Length)
            {
                if (validatedExclusions.Any(exclusion =>
                    exclusion.Match.All(pair => current.TryGetValue(pair.Key, out var value) &&
                        string.Equals(value, pair.Value, StringComparison.Ordinal))))
                    return;

                var orderedValues = new SortedDictionary<string, string>(current, StringComparer.Ordinal);
                var cellPayload = JsonSerializer.Serialize(orderedValues);
                var cellDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cellPayload))).ToLowerInvariant();
                cells.Add(new CiMatrixCell(
                    "cell-" + cellDigest,
                    new ReadOnlyDictionary<string, string>(orderedValues)));
                return;
            }

            var dimension = dimensions[dimensionIndex];
            foreach (var value in dimension.Values)
            {
                current[dimension.Name] = value;
                ExpandDimension(dimensionIndex + 1);
            }
            current.Remove(dimension.Name);
        }
    }
}

/// <summary>One observed result for an exact expanded matrix cell.</summary>
public sealed record CiMatrixCellResult(
    string CellId,
    string DefinitionDigest,
    CiStepOutcome Outcome,
    string? Diagnostic = null);

public enum CiMatrixCellStatus
{
    Passed = 0,
    Failed = 1,
    Missing = 2,
    Duplicate = 3,
    Skipped = 4,
    Cancelled = 5,
    Blocked = 6,
    Indeterminate = 7,
    Unknown = 8
}

public sealed record CiMatrixCellEvaluation(
    string CellId,
    CiMatrixCellStatus Status,
    CiStepOutcome? ObservedOutcome,
    string ReasonCode,
    string? Diagnostic = null);

public sealed record CiMatrixEvaluationReport(
    string MatrixId,
    string DefinitionDigest,
    int RequiredCellCount,
    IReadOnlyList<CiMatrixCellEvaluation> Cells,
    IReadOnlyList<string> Errors)
{
    public bool IsPassing =>
        RequiredCellCount > 0 &&
        Cells.Count == RequiredCellCount &&
        Errors.Count == 0 &&
        Cells.All(cell => cell.Status == CiMatrixCellStatus.Passed);
}

/// <summary>Aggregates exact-cell outcomes and fails closed on missing, ambiguous, or non-passing required cells.</summary>
public static class CiMatrixResultAggregator
{
    public const int MaximumDiagnosticLength = 2048;

    public static CiMatrixEvaluationReport Aggregate(
        CiMatrixExpansionReport expansion,
        IEnumerable<CiMatrixCellResult> results)
    {
        ArgumentNullException.ThrowIfNull(expansion);
        ArgumentNullException.ThrowIfNull(results);
        var supplied = results.ToArray();
        if (supplied.Any(result => result is null))
            throw new ArgumentException("Matrix results cannot contain null entries.", nameof(results));

        var knownCells = expansion.Cells.ToDictionary(cell => cell.CellId, StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var unknown in supplied.Where(result => !knownCells.ContainsKey(result.CellId)))
            errors.Add("matrix.result-unknown-cell:" + (unknown.CellId ?? "<null>"));

        var evaluations = new List<CiMatrixCellEvaluation>(expansion.RequiredCellCount);
        foreach (var cell in expansion.Cells)
        {
            var matches = supplied.Where(result => string.Equals(result.CellId, cell.CellId, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
            {
                evaluations.Add(new(cell.CellId, CiMatrixCellStatus.Missing, null, "matrix.cell-missing"));
                continue;
            }
            if (matches.Length > 1)
            {
                evaluations.Add(new(cell.CellId, CiMatrixCellStatus.Duplicate, null, "matrix.cell-duplicate"));
                continue;
            }

            var result = matches[0];
            var diagnostic = Bound(result.Diagnostic);
            if (!string.Equals(result.DefinitionDigest, expansion.DefinitionDigest, StringComparison.OrdinalIgnoreCase))
            {
                evaluations.Add(new(cell.CellId, CiMatrixCellStatus.Unknown, result.Outcome,
                    "matrix.definition-digest-mismatch", diagnostic));
                continue;
            }
            if (!Enum.IsDefined(result.Outcome))
            {
                evaluations.Add(new(cell.CellId, CiMatrixCellStatus.Unknown, null,
                    "matrix.outcome-invalid", diagnostic));
                continue;
            }

            var evaluation = result.Outcome switch
            {
                CiStepOutcome.Succeeded => new CiMatrixCellEvaluation(cell.CellId, CiMatrixCellStatus.Passed,
                    result.Outcome, "matrix.cell-passed", diagnostic),
                CiStepOutcome.Failed => new CiMatrixCellEvaluation(cell.CellId, CiMatrixCellStatus.Failed,
                    result.Outcome, "matrix.cell-failed", diagnostic),
                CiStepOutcome.Skipped => new CiMatrixCellEvaluation(cell.CellId, CiMatrixCellStatus.Skipped,
                    result.Outcome, "matrix.cell-skipped", diagnostic),
                CiStepOutcome.Cancelled => new CiMatrixCellEvaluation(cell.CellId, CiMatrixCellStatus.Cancelled,
                    result.Outcome, "matrix.cell-cancelled", diagnostic),
                CiStepOutcome.Blocked => new CiMatrixCellEvaluation(cell.CellId, CiMatrixCellStatus.Blocked,
                    result.Outcome, "matrix.cell-blocked", diagnostic),
                CiStepOutcome.Indeterminate => new CiMatrixCellEvaluation(cell.CellId, CiMatrixCellStatus.Indeterminate,
                    result.Outcome, "matrix.cell-indeterminate", diagnostic),
                _ => new CiMatrixCellEvaluation(cell.CellId, CiMatrixCellStatus.Unknown,
                    result.Outcome, "matrix.outcome-unknown", diagnostic)
            };
            evaluations.Add(evaluation);
        }

        return new CiMatrixEvaluationReport(
            expansion.MatrixId,
            expansion.DefinitionDigest,
            expansion.RequiredCellCount,
            Array.AsReadOnly(evaluations.ToArray()),
            Array.AsReadOnly(errors.Distinct(StringComparer.Ordinal).OrderBy(error => error, StringComparer.Ordinal).ToArray()));
    }

    private static string? Bound(string? diagnostic) =>
        diagnostic is null || diagnostic.Length <= MaximumDiagnosticLength
            ? diagnostic
            : diagnostic[..MaximumDiagnosticLength];
}
