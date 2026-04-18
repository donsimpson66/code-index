namespace CodeIndex.Core;

public sealed record ValidationIssue(string Code, string Message);

public sealed class CodeIndexValidationException : Exception
{
    public CodeIndexValidationException(IReadOnlyList<ValidationIssue> issues)
        : base(string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}")))
    {
        Issues = issues;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }
}

public sealed class CodeIndexValidator
{
    public IReadOnlyList<ValidationIssue> Validate(CodeIndexSnapshot snapshot)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(snapshot.Meta.SchemaVersion))
        {
            issues.Add(new ValidationIssue("meta-schema-version-missing", "SchemaVersion must be set."));
        }

        if (string.IsNullOrWhiteSpace(snapshot.Meta.ToolVersion))
        {
            issues.Add(new ValidationIssue("meta-tool-version-missing", "ToolVersion must be set."));
        }

        if (snapshot.Meta.GeneratedAtUtc.Offset != TimeSpan.Zero)
        {
            issues.Add(new ValidationIssue("meta-generated-at-not-utc", "GeneratedAtUtc must use a UTC offset."));
        }

        if (!IsSorted(snapshot.Files.Select(file => file.Path), StringComparer.Ordinal))
        {
            issues.Add(new ValidationIssue("files-not-sorted", "Files must be sorted by path using ordinal ordering."));
        }

        if (!IsSorted(snapshot.Symbols.Select(symbol => symbol.QualifiedName), StringComparer.Ordinal))
        {
            issues.Add(new ValidationIssue("symbols-not-sorted", "Symbols must be sorted by qualified name using ordinal ordering."));
        }

        if (!IsSorted(snapshot.Edges.Select(edge => $"{edge.Type}|{edge.From}|{edge.To}"), StringComparer.Ordinal))
        {
            issues.Add(new ValidationIssue("edges-not-sorted", "Edges must be sorted by type, from, then to using ordinal ordering."));
        }

        AddDuplicateIssues(issues, snapshot.Files.Select(file => file.Id), "file-id-duplicate", "Duplicate file ID");
        AddDuplicateIssues(issues, snapshot.Files.Select(file => file.Path), "file-path-duplicate", "Duplicate file path");
        AddDuplicateIssues(issues, snapshot.Symbols.Select(symbol => symbol.Id), "symbol-id-duplicate", "Duplicate symbol ID");
        AddDuplicateIssues(issues, snapshot.Edges.Select(edge => $"{edge.Type}|{edge.From}|{edge.To}"), "edge-duplicate", "Duplicate edge");

        var fileIds = snapshot.Files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal);
        var filesById = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var symbolsById = snapshot.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var symbolIds = snapshot.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var validEdgeTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            EdgeTypes.Contains,
            EdgeTypes.Inherits,
            EdgeTypes.Implements,
            EdgeTypes.Overrides
        };

        foreach (var file in snapshot.Files)
        {
            if (Path.IsPathRooted(file.Path) || file.Path.Contains('\\', StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue("file-path-not-normalized", $"File path {file.Path} must be relative and use forward slashes only."));
            }

            if (!string.Equals(file.Id, DeterministicId.CreateFileId(file.Path), StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue("file-id-mismatch", $"File {file.Path} has ID {file.Id} but expected {DeterministicId.CreateFileId(file.Path)}."));
            }

            if (!file.Hash.StartsWith("sha256:", StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue("file-hash-invalid", $"File {file.Id} has invalid hash format {file.Hash}."));
            }
        }

        foreach (var symbol in snapshot.Symbols)
        {
            if (!fileIds.Contains(symbol.FileId))
            {
                issues.Add(new ValidationIssue("symbol-file-missing", $"Symbol {symbol.Id} references missing file {symbol.FileId}."));
            }

            if (symbol.ParentId is not null && !symbolIds.Contains(symbol.ParentId))
            {
                issues.Add(new ValidationIssue("symbol-parent-missing", $"Symbol {symbol.Id} references missing parent {symbol.ParentId}."));
            }

            if (symbol.Range.StartLine <= 0 ||
                symbol.Range.StartColumn <= 0 ||
                symbol.Range.EndLine < symbol.Range.StartLine ||
                (symbol.Range.EndLine == symbol.Range.StartLine && symbol.Range.EndColumn < symbol.Range.StartColumn))
            {
                issues.Add(new ValidationIssue("symbol-range-invalid", $"Symbol {symbol.Id} has invalid range values."));
            }

            if (filesById.TryGetValue(symbol.FileId, out var file) && !string.Equals(symbol.Id, symbol.Id.Trim(), StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue("symbol-id-invalid", $"Symbol {symbol.Id} has invalid whitespace."));
            }
        }

        foreach (var edge in snapshot.Edges)
        {
            if (!validEdgeTypes.Contains(edge.Type))
            {
                issues.Add(new ValidationIssue("edge-type-invalid", $"Edge type {edge.Type} is not supported."));
            }

            if (!symbolIds.Contains(edge.From))
            {
                issues.Add(new ValidationIssue("edge-from-missing", $"Edge {edge.Type} references missing from symbol {edge.From}."));
            }

            if (!symbolIds.Contains(edge.To))
            {
                issues.Add(new ValidationIssue("edge-to-missing", $"Edge {edge.Type} references missing to symbol {edge.To}."));
            }

			if (edge.Type == EdgeTypes.Contains &&
				symbolsById.TryGetValue(edge.To, out var childSymbol) &&
				!string.Equals(childSymbol.ParentId, edge.From, StringComparison.Ordinal))
			{
				issues.Add(new ValidationIssue("edge-contains-inconsistent", $"Contains edge {edge.From} -> {edge.To} does not match child ParentId {childSymbol.ParentId}."));
			}
        }

        return issues;
    }

    public void ValidateOrThrow(CodeIndexSnapshot snapshot)
    {
        var issues = Validate(snapshot);

        if (issues.Count > 0)
        {
            throw new CodeIndexValidationException(issues);
        }
    }

    private static void AddDuplicateIssues(List<ValidationIssue> issues, IEnumerable<string> values, string code, string prefix)
    {
        foreach (var duplicate in values
                     .GroupBy(value => value, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            issues.Add(new ValidationIssue(code, $"{prefix}: {duplicate}."));
        }
    }

    private static bool IsSorted(IEnumerable<string> values, StringComparer comparer)
    {
        string? previous = null;

        foreach (var value in values)
        {
            if (previous is not null && comparer.Compare(previous, value) > 0)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }
}