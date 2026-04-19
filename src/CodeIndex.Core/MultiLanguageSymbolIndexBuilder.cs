namespace CodeIndex.Core;

public sealed class MultiLanguageSymbolIndexBuilder
{
    public Task<IReadOnlyList<SymbolRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        return BuildAsync(inputPath, files, indexedFilePaths: null, includeGenerated, cancellationToken);
    }

    public async Task<IReadOnlyList<SymbolRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyCollection<string>? indexedFilePaths,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = MultiLanguageFileIndexBuilder.GetSourceRoot(inputPath);
        var indexedPaths = indexedFilePaths is null
            ? null
            : new HashSet<string>(indexedFilePaths, StringComparer.Ordinal);
        var symbolRecords = new List<SymbolRecord>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (indexedPaths is not null && !indexedPaths.Contains(file.Path))
            {
                continue;
            }

            var fullPath = Path.Combine(sourceRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                continue;
            }

            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            symbolRecords.AddRange(MultiLanguageSymbolParser.Parse(file, source));
        }

        return symbolRecords
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.FileId, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Range.StartLine)
            .ThenBy(symbol => symbol.Range.StartColumn)
            .ToArray();
    }
}
