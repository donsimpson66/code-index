namespace CodeIndex.Core;

public sealed record EmbeddingRecord(
    string ItemType,
    string ItemId,
    IReadOnlyList<float> Vector);

public static class EmbeddingItemTypes
{
    public const string File = "file";
    public const string Symbol = "symbol";
}

public sealed record EmbeddingSearchResult(
    string ItemType,
    string ItemId,
    float Score);