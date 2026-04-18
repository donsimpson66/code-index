namespace CodeIndex.Core;

public sealed record CodeIndexSnapshot(
    CodeIndexMeta Meta,
    IReadOnlyList<FileRecord> Files,
    IReadOnlyList<SymbolRecord> Symbols,
    IReadOnlyList<EdgeRecord> Edges,
    IReadOnlyList<ReferenceRecord> References,
    IReadOnlyList<EmbeddingRecord> Embeddings);