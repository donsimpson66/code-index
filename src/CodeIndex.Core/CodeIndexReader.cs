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
        var referencesPath = Path.Combine(directory, "code-index.references.json");
        var references = File.Exists(referencesPath)
            ? await CodeIndexJson.ReadFromFileAsync<IReadOnlyList<ReferenceRecord>>(referencesPath, cancellationToken)
            : Array.Empty<ReferenceRecord>();
        var embeddingsPath = Path.Combine(directory, "code-index.embeddings.json");
        var embeddings = File.Exists(embeddingsPath)
            ? await CodeIndexJson.ReadFromFileAsync<IReadOnlyList<EmbeddingRecord>>(embeddingsPath, cancellationToken)
            : Array.Empty<EmbeddingRecord>();

        return new CodeIndexSnapshot(meta, files, symbols, edges, references, embeddings);
    }
}