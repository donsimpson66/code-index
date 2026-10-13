using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodeIndex.Core;

public static class SymbolPresentation
{
    private static readonly Regex GenericAritySuffix = new(@"`\d+", RegexOptions.Compiled);
    private static readonly Regex NestedTypePlus = new(@"\+", RegexOptions.Compiled);

    public static bool IsBoilerplateSummary(string? summary, string kind, string name)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return true;
        }

        var trimmed = summary.Trim();
        if (string.Equals(trimmed, $"{kind} {name}.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.StartsWith($"{kind} {name} in ", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith(" source.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static string NormalizeSummary(string? summary, string kind, string name)
    {
        return IsBoilerplateSummary(summary, kind, name) ? string.Empty : summary!.Trim();
    }

    public static string ShortenSignature(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return string.Empty;
        }

        var result = signature;
        result = result.Replace("System.Threading.Tasks.", string.Empty, StringComparison.Ordinal);
        result = result.Replace("System.Collections.Generic.", string.Empty, StringComparison.Ordinal);
        result = result.Replace("System.Collections.", string.Empty, StringComparison.Ordinal);
        result = result.Replace("System.Linq.", string.Empty, StringComparison.Ordinal);
        result = result.Replace("System.Text.", string.Empty, StringComparison.Ordinal);
        result = result.Replace("System.", string.Empty, StringComparison.Ordinal);
        result = GenericAritySuffix.Replace(result, string.Empty);
        result = NestedTypePlus.Replace(result, ".");
        result = Regex.Replace(result, @"\b(?:[A-Za-z_][\w]*\.)+([A-Za-z_][\w]*)", "$1");
        return result;
    }

    public static (IReadOnlyList<SymbolRecord> Symbols, IReadOnlyList<EdgeRecord> Edges, IReadOnlyList<ReferenceRecord> References, IReadOnlyList<EmbeddingRecord> Embeddings)
        FilterLocals(
            IReadOnlyList<SymbolRecord> symbols,
            IReadOnlyList<EdgeRecord> edges,
            IReadOnlyList<ReferenceRecord> references,
            IReadOnlyList<EmbeddingRecord> embeddings,
            bool includeLocals)
    {
        if (includeLocals)
        {
            return (symbols, edges, references, embeddings);
        }

        var localIds = symbols
            .Where(symbol => string.Equals(symbol.Kind, SymbolKinds.Local, StringComparison.Ordinal))
            .Select(symbol => symbol.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (localIds.Count == 0)
        {
            return (symbols, edges, references, embeddings);
        }

        var filteredSymbols = symbols
            .Where(symbol => !localIds.Contains(symbol.Id))
            .Select(symbol => localIds.Contains(symbol.ParentId ?? string.Empty)
                ? symbol with { ParentId = null }
                : symbol)
            .ToArray();

        var filteredEdges = edges
            .Where(edge => !localIds.Contains(edge.From) && !localIds.Contains(edge.To))
            .ToArray();

        var filteredReferences = references
            .Where(reference =>
                !localIds.Contains(reference.TargetSymbolId) &&
                (reference.SourceSymbolId is null || !localIds.Contains(reference.SourceSymbolId)))
            .ToArray();

        var filteredEmbeddings = embeddings
            .Where(embedding =>
                !string.Equals(embedding.ItemType, EmbeddingItemTypes.Symbol, StringComparison.Ordinal) ||
                !localIds.Contains(embedding.ItemId))
            .ToArray();

        return (filteredSymbols, filteredEdges, filteredReferences, filteredEmbeddings);
    }
}

public sealed record SymbolListItem(
    string Id,
    string Name,
    string QualifiedName,
    string Kind,
    string File,
    int StartLine,
    string Accessibility,
    string? Signature = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Summary = null);
