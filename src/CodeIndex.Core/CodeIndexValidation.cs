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
    private static readonly IComparer<EdgeRecord> EdgeComparer = Comparer<EdgeRecord>.Create(static (left, right) =>
    {
        var typeComparison = StringComparer.Ordinal.Compare(left.Type, right.Type);

        if (typeComparison != 0)
        {
            return typeComparison;
        }

        var fromComparison = StringComparer.Ordinal.Compare(left.From, right.From);

        if (fromComparison != 0)
        {
            return fromComparison;
        }

        return StringComparer.Ordinal.Compare(left.To, right.To);
    });

    private static readonly IComparer<ReferenceRecord> ReferenceComparer = Comparer<ReferenceRecord>.Create(static (left, right) =>
    {
        var targetComparison = StringComparer.Ordinal.Compare(left.TargetSymbolId, right.TargetSymbolId);

        if (targetComparison != 0)
        {
            return targetComparison;
        }

        var fileComparison = StringComparer.Ordinal.Compare(left.FileId, right.FileId);

        if (fileComparison != 0)
        {
            return fileComparison;
        }

        var lineComparison = left.Range.StartLine.CompareTo(right.Range.StartLine);

        if (lineComparison != 0)
        {
            return lineComparison;
        }

        var columnComparison = left.Range.StartColumn.CompareTo(right.Range.StartColumn);

        if (columnComparison != 0)
        {
            return columnComparison;
        }

        return StringComparer.Ordinal.Compare(left.SourceSymbolId, right.SourceSymbolId);
    });

    private static readonly IComparer<EmbeddingRecord> EmbeddingComparer = Comparer<EmbeddingRecord>.Create(static (left, right) =>
    {
        var itemTypeComparison = StringComparer.Ordinal.Compare(left.ItemType, right.ItemType);

        if (itemTypeComparison != 0)
        {
            return itemTypeComparison;
        }

        return StringComparer.Ordinal.Compare(left.ItemId, right.ItemId);
    });

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

        if (!IsSorted(snapshot.Edges, EdgeComparer))
        {
            issues.Add(new ValidationIssue("edges-not-sorted", "Edges must be sorted by type, from, then to using ordinal ordering."));
        }

        if (!IsSorted(snapshot.References, ReferenceComparer))
        {
            issues.Add(new ValidationIssue("references-not-sorted", "References must be sorted by target symbol, file, and range using ordinal ordering."));
        }

        if (!IsSorted(snapshot.Embeddings, EmbeddingComparer))
        {
            issues.Add(new ValidationIssue("embeddings-not-sorted", "Embeddings must be sorted by item type and item ID using ordinal ordering."));
        }

        AddDuplicateIssues(issues, snapshot.Files.Select(file => file.Id), "file-id-duplicate", "Duplicate file ID");
        AddDuplicateIssues(issues, snapshot.Files.Select(file => file.Path), "file-path-duplicate", "Duplicate file path");
        AddDuplicateIssues(issues, snapshot.Symbols.Select(symbol => symbol.Id), "symbol-id-duplicate", "Duplicate symbol ID");
        AddDuplicateEdgeIssues(issues, snapshot.Edges);
        AddDuplicateReferenceIssues(issues, snapshot.References);
        AddDuplicateEmbeddingIssues(issues, snapshot.Embeddings);

        var fileIds = snapshot.Files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal);
        var filesById = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var symbolsById = snapshot.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var symbolIds = snapshot.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var validEdgeTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            EdgeTypes.Contains,
            EdgeTypes.Inherits,
            EdgeTypes.Implements,
            EdgeTypes.Overrides,
            EdgeTypes.Calls,
            EdgeTypes.Tests
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

        foreach (var reference in snapshot.References)
        {
            if (!symbolIds.Contains(reference.TargetSymbolId))
            {
                issues.Add(new ValidationIssue("reference-target-missing", $"Reference targets missing symbol {reference.TargetSymbolId}."));
            }

            if (reference.SourceSymbolId is not null && !symbolIds.Contains(reference.SourceSymbolId))
            {
                issues.Add(new ValidationIssue("reference-source-missing", $"Reference references missing source symbol {reference.SourceSymbolId}."));
            }

            if (!fileIds.Contains(reference.FileId))
            {
                issues.Add(new ValidationIssue("reference-file-missing", $"Reference targets missing file {reference.FileId}."));
            }

            if (reference.Range.StartLine <= 0 ||
                reference.Range.StartColumn <= 0 ||
                reference.Range.EndLine < reference.Range.StartLine ||
                (reference.Range.EndLine == reference.Range.StartLine && reference.Range.EndColumn < reference.Range.StartColumn))
            {
                issues.Add(new ValidationIssue("reference-range-invalid", $"Reference for target {reference.TargetSymbolId} has invalid range values."));
            }
        }

        foreach (var embedding in snapshot.Embeddings)
        {
            if (embedding.Vector.Count == 0)
            {
                issues.Add(new ValidationIssue("embedding-vector-empty", $"Embedding {embedding.ItemType}:{embedding.ItemId} must have at least one dimension."));
            }

            if (embedding.Vector.Any(static value => float.IsNaN(value) || float.IsInfinity(value)))
            {
                issues.Add(new ValidationIssue("embedding-vector-invalid", $"Embedding {embedding.ItemType}:{embedding.ItemId} contains non-finite values."));
            }

            var itemExists = embedding.ItemType switch
            {
                EmbeddingItemTypes.File => fileIds.Contains(embedding.ItemId),
                EmbeddingItemTypes.Symbol => symbolIds.Contains(embedding.ItemId),
                _ => false
            };

            if (!itemExists)
            {
                issues.Add(new ValidationIssue("embedding-item-missing", $"Embedding {embedding.ItemType}:{embedding.ItemId} does not reference an indexed file or symbol."));
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

    private static void AddDuplicateEdgeIssues(List<ValidationIssue> issues, IEnumerable<EdgeRecord> edges)
    {
        foreach (var duplicate in edges
                     .GroupBy(edge => edge)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .OrderBy(edge => edge, EdgeComparer))
        {
            issues.Add(new ValidationIssue("edge-duplicate", $"Duplicate edge: {duplicate.Type} {duplicate.From} -> {duplicate.To}."));
        }
    }

    private static void AddDuplicateReferenceIssues(List<ValidationIssue> issues, IEnumerable<ReferenceRecord> references)
    {
        foreach (var duplicate in references
                     .GroupBy(reference => reference)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .OrderBy(reference => reference, ReferenceComparer))
        {
            issues.Add(new ValidationIssue("reference-duplicate", $"Duplicate reference: {duplicate.TargetSymbolId} in {duplicate.FileId} at {duplicate.Range.StartLine}:{duplicate.Range.StartColumn}."));
        }
    }

    private static void AddDuplicateEmbeddingIssues(List<ValidationIssue> issues, IEnumerable<EmbeddingRecord> embeddings)
    {
        foreach (var duplicate in embeddings
                     .GroupBy(embedding => (embedding.ItemType, embedding.ItemId))
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .OrderBy(key => key.ItemType, StringComparer.Ordinal)
                     .ThenBy(key => key.ItemId, StringComparer.Ordinal))
        {
            issues.Add(new ValidationIssue("embedding-duplicate", $"Duplicate embedding: {duplicate.ItemType}:{duplicate.ItemId}."));
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

    private static bool IsSorted<T>(IEnumerable<T> values, IComparer<T> comparer)
    {
        var hasPrevious = false;
        var previous = default(T);

        foreach (var value in values)
        {
            if (hasPrevious && comparer.Compare(previous!, value) > 0)
            {
                return false;
            }

            previous = value;
            hasPrevious = true;
        }

        return true;
    }
}