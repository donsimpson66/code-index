namespace CodeIndex.Core;

public sealed class CodeIndexReader
{
    public async Task<CodeIndexSnapshot> ReadAsync(string indexDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);

        var directory = Path.GetFullPath(indexDirectory);
        var meta = await CodeIndexJson.ReadFromFileAsync<CodeIndexMeta>(Path.Combine(directory, "code-index.meta.json"), cancellationToken);
        var files = await CodeIndexJson.ReadFromFileAsync<IReadOnlyList<FileRecord>>(Path.Combine(directory, "code-index.files.json"), cancellationToken);
        var symbols = await CodeIndexJson.ReadFromFileAsync<IReadOnlyList<SymbolRecord>>(Path.Combine(directory, "code-index.symbols.json"), cancellationToken);
        var edges = await CodeIndexJson.ReadFromFileAsync<IReadOnlyList<EdgeRecord>>(Path.Combine(directory, "code-index.edges.json"), cancellationToken);

        return new CodeIndexSnapshot(meta, files, symbols, edges);
    }
}