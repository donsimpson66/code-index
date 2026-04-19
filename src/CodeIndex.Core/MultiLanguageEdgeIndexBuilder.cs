namespace CodeIndex.Core;

public sealed class MultiLanguageEdgeIndexBuilder
{
    public async Task<IReadOnlyList<EdgeRecord>> BuildAsync(string inputPath, bool includeGenerated = false, CancellationToken cancellationToken = default)
    {
        var fileBuilder = new MultiLanguageFileIndexBuilder();
        var files = await fileBuilder.BuildAsync(inputPath, includeGenerated, cancellationToken);
        return await BuildForFilesCoreAsync(inputPath, files, cancellationToken);
    }

    public Task<IReadOnlyList<EdgeRecord>> BuildAsync(
        string inputPath,
        IReadOnlyCollection<string> indexedFilePaths,
        IReadOnlySet<string> knownSymbolIds,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = MultiLanguageFileIndexBuilder.GetSourceRoot(inputPath);
        var fileRecords = indexedFilePaths
            .Select(path => new FileRecord(DeterministicId.CreateFileId(path), path, Path.GetFileName(sourceRoot), InferLanguage(path), string.Empty, string.Empty))
            .ToArray();

        return BuildForFilesCoreAsync(inputPath, fileRecords, cancellationToken);
    }

    public Task<IReadOnlyList<EdgeRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyCollection<string>? indexedFilePaths,
        CancellationToken cancellationToken = default)
    {
        var selectedFiles = indexedFilePaths is null
            ? files
            : files.Where(file => indexedFilePaths.Contains(file.Path)).ToArray();

        return BuildForFilesCoreAsync(inputPath, selectedFiles, cancellationToken);
    }

    public IReadOnlyList<EdgeRecord> BuildFromSymbols(IReadOnlyList<SymbolRecord> symbols)
    {
        return symbols
            .Where(symbol => symbol.ParentId is not null)
            .Select(symbol => new EdgeRecord(EdgeTypes.Contains, symbol.ParentId!, symbol.Id))
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<EdgeRecord>> BuildForFilesCoreAsync(string inputPath, IReadOnlyList<FileRecord> files, CancellationToken cancellationToken)
    {
        var symbolBuilder = new MultiLanguageSymbolIndexBuilder();
        var symbols = await symbolBuilder.BuildAsync(inputPath, files, includeGenerated: true, cancellationToken: cancellationToken);
        var containsEdges = BuildFromSymbols(symbols);
        var callEdges = await MultiLanguageUsageParser.BuildCallEdgesAsync(inputPath, files, symbols, cancellationToken);
        return containsEdges
            .Concat(callEdges)
            .Distinct()
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static string InferLanguage(string path)
    {
        return SourceLanguageCatalog.TryGetLanguage(path, out var language) ? language : "Unknown";
    }
}
