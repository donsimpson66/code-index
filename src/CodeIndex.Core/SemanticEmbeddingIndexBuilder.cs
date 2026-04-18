using System.Text;

namespace CodeIndex.Core;

public sealed class SemanticEmbeddingIndexBuilder
{
    public const int DefaultDimensions = 96;

    public IReadOnlyList<EmbeddingRecord> Build(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        int dimensions = DefaultDimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);

        var embeddings = files
            .Select(file => new EmbeddingRecord(
                EmbeddingItemTypes.File,
                file.Id,
                EmbedText(CreateFileText(file), dimensions)))
            .Concat(symbols.Select(symbol => new EmbeddingRecord(
                EmbeddingItemTypes.Symbol,
                symbol.Id,
                EmbedText(CreateSymbolText(symbol), dimensions))))
            .OrderBy(embedding => embedding.ItemType, StringComparer.Ordinal)
            .ThenBy(embedding => embedding.ItemId, StringComparer.Ordinal)
            .ToArray();

        return embeddings;
    }

    public IReadOnlyList<EmbeddingSearchResult> Search(
        string query,
        IReadOnlyList<EmbeddingRecord> embeddings,
        string? itemType = null,
        int limit = 10,
        int dimensions = DefaultDimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);

        var queryVector = EmbedText(query, dimensions);

        return embeddings
            .Where(embedding => string.IsNullOrWhiteSpace(itemType) || string.Equals(embedding.ItemType, itemType, StringComparison.OrdinalIgnoreCase))
            .Select(embedding => new EmbeddingSearchResult(
                embedding.ItemType,
                embedding.ItemId,
                Dot(queryVector, embedding.Vector)))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.ItemType, StringComparer.Ordinal)
            .ThenBy(result => result.ItemId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    internal static IReadOnlyList<float> EmbedText(string text, int dimensions)
    {
        var vector = new float[dimensions];

        foreach (var token in Tokenize(text))
        {
            var hash = GetStableHash(token);
            var index = (int)(hash % (uint)dimensions);
            var sign = (hash & 1) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        Normalize(vector);
        return vector;
    }

    private static string CreateFileText(FileRecord file)
    {
        return string.Join(' ', new[]
        {
            file.Path,
            Path.GetFileNameWithoutExtension(file.Path),
            file.ProjectName,
            file.Language,
            file.Summary
        });
    }

    private static string CreateSymbolText(SymbolRecord symbol)
    {
        return string.Join(' ', new[]
        {
            symbol.Name,
            symbol.QualifiedName,
            symbol.Kind,
            symbol.Signature,
            symbol.Summary,
            symbol.Accessibility
        });
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var buffer = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (buffer.Length > 0 && char.IsUpper(character) && char.IsLower(buffer[^1]))
                {
                    yield return buffer.ToString().ToLowerInvariant();
                    buffer.Clear();
                }

                buffer.Append(character);
            }
            else if (buffer.Length > 0)
            {
                yield return buffer.ToString().ToLowerInvariant();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().ToLowerInvariant();
        }
    }

    private static uint GetStableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;

            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static void Normalize(float[] vector)
    {
        var magnitudeSquared = 0d;

        foreach (var value in vector)
        {
            magnitudeSquared += value * value;
        }

        if (magnitudeSquared <= 0)
        {
            return;
        }

        var magnitude = (float)Math.Sqrt(magnitudeSquared);

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= magnitude;
        }
    }

    private static float Dot(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        var total = 0f;

        for (var index = 0; index < length; index++)
        {
            total += left[index] * right[index];
        }

        return total;
    }
}