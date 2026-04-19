namespace CodeIndex.Core;

public sealed class MultiLanguageReferenceIndexBuilder
{
    public async Task<IReadOnlyList<ReferenceRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        return await MultiLanguageUsageParser.BuildReferencesAsync(inputPath, files, symbols, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyCollection<string> indexedFilePaths,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        var selectedFiles = files.Where(file => indexedFilePaths.Contains(file.Path)).ToArray();
        return await MultiLanguageUsageParser.BuildReferencesAsync(inputPath, selectedFiles, symbols, cancellationToken);
    }
}
