using CodeIndex.Core;

namespace CodeIndex.Cli;

internal sealed class CodeIndexQueryService(CliRuntime runtime)
{
    public async Task<IReadOnlyList<SymbolRecord>> FindSymbolsAsync(CodeIndexFindSymbolsRequest request, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(request.Query);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return FindSymbols(snapshot, request);
    }

    public async Task<IReadOnlyList<CodeIndexSemanticSearchResult>> SemanticSearchAsync(CodeIndexSemanticSearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new InvalidOperationException("A semantic search query is required.");
        }

        var itemType = NormalizeOptional(request.ItemType);
        if (itemType is not null &&
            !string.Equals(itemType, EmbeddingItemTypes.File, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(itemType, EmbeddingItemTypes.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported --type for semantic-search. Use file or symbol.");
        }

        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        var embeddingBuilder = new SemanticEmbeddingIndexBuilder();
        return embeddingBuilder.Search(request.Query, snapshot.Embeddings, itemType, request.Limit <= 0 ? 10 : request.Limit)
            .Select(result => CreateSemanticSearchResult(snapshot, result))
            .ToArray();
    }

    public async Task<IReadOnlyList<CodeIndexReferenceSearchResult>> FindReferencesAsync(CodeIndexReferenceQuery request, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(request.Query);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return FindReferences(snapshot, request);
    }

    public async Task<SymbolRecord> GetSymbolAsync(string query, string? indexDirectory, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(query);
        var snapshot = await ReadSnapshotAsync(indexDirectory, cancellationToken);
        return ResolveSymbol(snapshot, query);
    }

    public async Task<IReadOnlyList<SymbolRecord>> GetChildrenAsync(CodeIndexChildQueryRequest request, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(request.Query);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return GetChildren(snapshot, request);
    }

    public async Task<IReadOnlyList<SymbolRecord>> GetCalleesAsync(CodeIndexChildQueryRequest request, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(request.Query);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return GetCallees(snapshot, request);
    }

    public async Task<IReadOnlyList<SymbolRecord>> GetCallersAsync(CodeIndexChildQueryRequest request, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(request.Query);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return GetCallers(snapshot, request);
    }

    public async Task<IReadOnlyList<SymbolRecord>> GetTestTargetsAsync(CodeIndexChildQueryRequest request, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(request.Query);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return GetTestTargets(snapshot, request);
    }

    public async Task<IReadOnlyList<SymbolRecord>> GetTestsAsync(CodeIndexChildQueryRequest request, CancellationToken cancellationToken)
    {
        ValidateSymbolQuery(request.Query);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return GetTests(snapshot, request);
    }

    public async Task<IReadOnlyList<CodeIndexExcerptLine>> GetExcerptAsync(CodeIndexExcerptQuery request, CancellationToken cancellationToken)
    {
        ValidateExcerptRequest(request);
        var snapshot = await ReadSnapshotAsync(request.IndexDirectory, cancellationToken);
        return await GetExcerptAsync(snapshot, request.FilePath, request.StartLine, request.EndLine, cancellationToken);
    }

    public async Task<CodeIndexSnapshot> ReadSnapshotAsync(string? indexDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexDirectory))
        {
            throw new InvalidOperationException("An index directory is required. Pass --index <path>.");
        }

        return await runtime.ReadSnapshotAsync(indexDirectory, cancellationToken);
    }

    internal IReadOnlyList<SymbolRecord> FindSymbols(CodeIndexSnapshot snapshot, CodeIndexFindSymbolsRequest request)
    {
        return LimitResults(OrderFindSymbolResults(snapshot.Symbols
            .Where(symbol =>
                string.Equals(symbol.Name, request.Query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(symbol.QualifiedName, request.Query, StringComparison.OrdinalIgnoreCase) ||
                symbol.QualifiedName.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
            .Where(symbol => string.IsNullOrWhiteSpace(request.Kind) || string.Equals(symbol.Kind, request.Kind, StringComparison.OrdinalIgnoreCase))
            .Where(symbol => string.IsNullOrWhiteSpace(request.Accessibility) || string.Equals(symbol.Accessibility, request.Accessibility, StringComparison.OrdinalIgnoreCase)), request.Query, request.Sort), request.Limit)
            .ToArray();
    }

    internal IReadOnlyList<CodeIndexReferenceSearchResult> FindReferences(CodeIndexSnapshot snapshot, CodeIndexReferenceQuery request)
    {
        var targetSymbol = ResolveSymbol(snapshot, request.Query);
        var filesById = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var symbolsById = snapshot.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var referencesForSymbol = snapshot.References
            .Where(reference => string.Equals(reference.TargetSymbolId, targetSymbol.Id, StringComparison.Ordinal))
            .OrderBy(reference => filesById.TryGetValue(reference.FileId, out var file) ? file.Path : reference.FileId, StringComparer.Ordinal)
            .ThenBy(reference => reference.Range.StartLine)
            .ThenBy(reference => reference.Range.StartColumn);

        return LimitResults(referencesForSymbol.Select(reference => new CodeIndexReferenceSearchResult(
            reference.TargetSymbolId,
            targetSymbol.QualifiedName,
            reference.SourceSymbolId,
            reference.SourceSymbolId is not null && symbolsById.TryGetValue(reference.SourceSymbolId, out var sourceSymbol)
                ? sourceSymbol.QualifiedName
                : null,
            filesById.TryGetValue(reference.FileId, out var file) ? file.Path : reference.FileId,
            reference.Range,
            reference.LineText)), request.Limit).ToArray();
    }

    internal IReadOnlyList<SymbolRecord> GetChildren(CodeIndexSnapshot snapshot, CodeIndexChildQueryRequest request)
    {
        var parent = ResolveSymbol(snapshot, request.Query);
        return GetRelatedSymbols(snapshot, parent.Id, EdgeTypes.Contains, outgoing: true, request);
    }

    internal IReadOnlyList<SymbolRecord> GetCallees(CodeIndexSnapshot snapshot, CodeIndexChildQueryRequest request)
    {
        var symbol = ResolveSymbol(snapshot, request.Query);
        return GetRelatedSymbols(snapshot, symbol.Id, EdgeTypes.Calls, outgoing: true, request);
    }

    internal IReadOnlyList<SymbolRecord> GetCallers(CodeIndexSnapshot snapshot, CodeIndexChildQueryRequest request)
    {
        var symbol = ResolveSymbol(snapshot, request.Query);
        return GetRelatedSymbols(snapshot, symbol.Id, EdgeTypes.Calls, outgoing: false, request);
    }

    internal IReadOnlyList<SymbolRecord> GetTestTargets(CodeIndexSnapshot snapshot, CodeIndexChildQueryRequest request)
    {
        var symbol = ResolveSymbol(snapshot, request.Query);
        return GetRelatedSymbols(snapshot, symbol.Id, EdgeTypes.Tests, outgoing: true, request);
    }

    internal IReadOnlyList<SymbolRecord> GetTests(CodeIndexSnapshot snapshot, CodeIndexChildQueryRequest request)
    {
        var symbol = ResolveSymbol(snapshot, request.Query);
        return GetRelatedSymbols(snapshot, symbol.Id, EdgeTypes.Tests, outgoing: false, request);
    }

    internal async Task<IReadOnlyList<CodeIndexExcerptLine>> GetExcerptAsync(CodeIndexSnapshot snapshot, string filePath, int startLine, int endLine, CancellationToken cancellationToken)
    {
        var fileRecord = snapshot.Files.FirstOrDefault(candidate => string.Equals(candidate.Path, filePath, StringComparison.OrdinalIgnoreCase));
        if (fileRecord is null)
        {
            throw new InvalidOperationException($"No indexed file found for path: {filePath}");
        }

        var fullFilePath = Path.Combine(snapshot.Meta.SourceRoot, fileRecord.Path.Replace('/', Path.DirectorySeparatorChar));
        var lines = await File.ReadAllLinesAsync(fullFilePath, cancellationToken);
        if (startLine > lines.Length)
        {
            return Array.Empty<CodeIndexExcerptLine>();
        }

        return Enumerable.Range(startLine, Math.Min(endLine, lines.Length) - startLine + 1)
            .Select(lineNumber => new CodeIndexExcerptLine(lineNumber, lines[lineNumber - 1]))
            .ToArray();
    }

    private static IReadOnlyList<SymbolRecord> GetRelatedSymbols(CodeIndexSnapshot snapshot, string symbolId, string edgeType, bool outgoing, CodeIndexChildQueryRequest request)
    {
        var relatedSymbols = (outgoing
                ? snapshot.Edges.Where(edge => edge.Type == edgeType && edge.From == symbolId).Join(snapshot.Symbols, edge => edge.To, candidate => candidate.Id, (_, candidate) => candidate)
                : snapshot.Edges.Where(edge => edge.Type == edgeType && edge.To == symbolId).Join(snapshot.Symbols, edge => edge.From, candidate => candidate.Id, (_, candidate) => candidate))
            .Where(candidate => string.IsNullOrWhiteSpace(request.Kind) || string.Equals(candidate.Kind, request.Kind, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => string.IsNullOrWhiteSpace(request.Accessibility) || string.Equals(candidate.Accessibility, request.Accessibility, StringComparison.OrdinalIgnoreCase));

        return LimitResults(OrderChildResults(relatedSymbols, request.Sort), request.Limit).ToArray();
    }

    private static SymbolRecord ResolveSymbol(CodeIndexSnapshot snapshot, string query)
    {
        return snapshot.Symbols.FirstOrDefault(candidate =>
                   string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
                   string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"No symbol found for query: {query}");
    }

    private static CodeIndexSemanticSearchResult CreateSemanticSearchResult(CodeIndexSnapshot snapshot, EmbeddingSearchResult result)
    {
        var filesById = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var symbolsById = snapshot.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);

        if (string.Equals(result.ItemType, EmbeddingItemTypes.File, StringComparison.Ordinal))
        {
            var file = filesById[result.ItemId];
            return new CodeIndexSemanticSearchResult(
                result.ItemType,
                result.ItemId,
                Math.Round(result.Score, 4),
                file.Path,
                file.ProjectName,
                file.Language,
                file.Summary,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var symbol = symbolsById[result.ItemId];
        return new CodeIndexSemanticSearchResult(
            result.ItemType,
            result.ItemId,
            Math.Round(result.Score, 4),
            null,
            null,
            null,
            null,
            symbol.Name,
            symbol.QualifiedName,
            symbol.Kind,
            symbol.FileId,
            symbol.ParentId,
            symbol.Signature,
            symbol.Summary);
    }

    private static void ValidateSymbolQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("A symbol query is required.");
        }
    }

    private static void ValidateExcerptRequest(CodeIndexExcerptQuery request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new InvalidOperationException("A file path is required.");
        }

        if (request.StartLine <= 0 || request.EndLine < request.StartLine)
        {
            throw new InvalidOperationException("Use positive line numbers and ensure --end is greater than or equal to --start.");
        }
    }

    private static int GetMatchRank(SymbolRecord symbol, string query)
    {
        if (string.Equals(symbol.QualifiedName, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(symbol.Id, query, StringComparison.Ordinal))
        {
            return 1;
        }

        if (string.Equals(symbol.Name, query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (symbol.QualifiedName.EndsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (symbol.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return 5;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static int GetKindRank(string kind)
    {
        return SymbolKinds.GetRank(kind);
    }

    private static int GetAccessibilityRank(string accessibility)
    {
        return accessibility switch
        {
            "public" => 0,
            "protected" => 1,
            "protected internal" => 2,
            "internal" => 3,
            "private protected" => 4,
            "private" => 5,
            _ => 6
        };
    }

    private static IEnumerable<SymbolRecord> OrderFindSymbolResults(IEnumerable<SymbolRecord> symbols, string query, string? sort)
    {
        return NormalizeSort(sort, "ranked") switch
        {
            "ranked" => symbols
                .OrderBy(symbol => GetMatchRank(symbol, query))
                .ThenBy(symbol => GetKindRank(symbol.Kind))
                .ThenBy(symbol => GetAccessibilityRank(symbol.Accessibility))
                .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
            "name" => symbols
                .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
                .ThenBy(symbol => symbol.Id, StringComparer.Ordinal),
            "accessibility" => symbols
                .OrderBy(symbol => GetAccessibilityRank(symbol.Accessibility))
                .ThenBy(symbol => GetKindRank(symbol.Kind))
                .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
            _ => throw new InvalidOperationException("Unsupported --sort for find-symbol. Use ranked, name, or accessibility.")
        };
    }

    private static IEnumerable<SymbolRecord> OrderChildResults(IEnumerable<SymbolRecord> symbols, string? sort)
    {
        return NormalizeSort(sort, "name") switch
        {
            "name" => symbols
                .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
                .ThenBy(symbol => symbol.Id, StringComparer.Ordinal),
            "accessibility" => symbols
                .OrderBy(symbol => GetAccessibilityRank(symbol.Accessibility))
                .ThenBy(symbol => GetKindRank(symbol.Kind))
                .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
            "declaration" => symbols
                .OrderBy(symbol => symbol.FileId, StringComparer.Ordinal)
                .ThenBy(symbol => symbol.Range.StartLine)
                .ThenBy(symbol => symbol.Range.StartColumn)
                .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
            _ => throw new InvalidOperationException("Unsupported --sort for get-children. Use name, accessibility, or declaration.")
        };
    }

    private static string NormalizeSort(string? sort, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(sort) ? defaultValue : sort.Trim().ToLowerInvariant();
    }

    private static IEnumerable<T> LimitResults<T>(IEnumerable<T> source, int limit)
    {
        return limit > 0 ? source.Take(limit) : source;
    }
}

internal sealed record CodeIndexFindSymbolsRequest(string Query, string? IndexDirectory, int Limit, string? Kind, string? Accessibility, string? Sort);

internal sealed record CodeIndexSemanticSearchRequest(string Query, string? IndexDirectory, int Limit, string? ItemType);

internal sealed record CodeIndexReferenceQuery(string Query, string? IndexDirectory, int Limit);

internal sealed record CodeIndexChildQueryRequest(string Query, string? IndexDirectory, int Limit, string? Kind, string? Accessibility, string? Sort);

internal sealed record CodeIndexExcerptQuery(string FilePath, string? IndexDirectory, int StartLine, int EndLine);

internal sealed record CodeIndexSemanticSearchResult(
    string ItemType,
    string ItemId,
    double Score,
    string? Path,
    string? ProjectName,
    string? Language,
    string? FileSummary,
    string? Name,
    string? QualifiedName,
    string? Kind,
    string? FileId,
    string? ParentId,
    string? Signature,
    string? SymbolSummary);

internal sealed record CodeIndexReferenceSearchResult(
    string TargetSymbolId,
    string TargetQualifiedName,
    string? SourceSymbolId,
    string? SourceQualifiedName,
    string File,
    TextRangeRecord Range,
    string LineText);

internal sealed record CodeIndexExcerptLine(int Line, string Text);