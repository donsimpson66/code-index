using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodeIndex.Core;

namespace CodeIndex.Cli;

public static class QueryResultFormatter
{
    public static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlyList<SymbolListItem> ProjectSymbols(IReadOnlyList<SymbolRecord> symbols, CodeIndexSnapshot snapshot)
    {
        var filesById = snapshot.Files.ToDictionary(file => file.Id, file => file.Path, StringComparer.Ordinal);
        return symbols.Select(symbol => ProjectSymbol(symbol, filesById)).ToArray();
    }

    public static SymbolListItem ProjectSymbol(SymbolRecord symbol, IReadOnlyDictionary<string, string>? filePathById = null)
    {
        var file = filePathById is not null && filePathById.TryGetValue(symbol.FileId, out var path)
            ? path
            : symbol.FileId.StartsWith("f:", StringComparison.Ordinal)
                ? symbol.FileId[2..]
                : symbol.FileId;

        var summary = SymbolPresentation.NormalizeSummary(symbol.Summary, symbol.Kind, symbol.Name);
        var shortSignature = SymbolPresentation.ShortenSignature(symbol.Signature);
        return new SymbolListItem(
            symbol.Id,
            symbol.Name,
            symbol.QualifiedName,
            symbol.Kind,
            file,
            symbol.Range.StartLine,
            symbol.Accessibility,
            string.IsNullOrWhiteSpace(shortSignature) ? null : shortSignature,
            string.IsNullOrWhiteSpace(summary) ? null : summary);
    }

    public static SymbolListItem ProjectSymbol(SymbolRecord symbol, CodeIndexSnapshot snapshot)
    {
        var filesById = snapshot.Files.ToDictionary(file => file.Id, file => file.Path, StringComparer.Ordinal);
        return ProjectSymbol(symbol, filesById);
    }

    public static object PrepareSymbolResults(
        IReadOnlyList<SymbolRecord> symbols,
        CodeIndexSnapshot snapshot,
        bool full,
        string? fields)
    {
        if (full)
        {
            var normalized = symbols.Select(symbol => symbol with
            {
                Summary = SymbolPresentation.NormalizeSummary(symbol.Summary, symbol.Kind, symbol.Name)
            }).ToArray();

            return ApplyFields(normalized, fields);
        }

        return ApplyFields(ProjectSymbols(symbols, snapshot), fields);
    }

    public static object PrepareSingleSymbol(SymbolRecord symbol, CodeIndexSnapshot snapshot, bool full, string? fields)
    {
        if (full)
        {
            var normalized = symbol with
            {
                Summary = SymbolPresentation.NormalizeSummary(symbol.Summary, symbol.Kind, symbol.Name)
            };
            return ApplyFields(normalized, fields);
        }

        return ApplyFields(ProjectSymbol(symbol, snapshot), fields);
    }

    public static string Format(object value, string? format, bool pretty = false)
    {
        var normalizedFormat = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();
        return normalizedFormat switch
        {
            "json" => JsonSerializer.Serialize(value, pretty ? PrettyJsonOptions : CompactJsonOptions),
            "text" => FormatText(value),
            _ => throw new InvalidOperationException("Unsupported --format. Use json or text.")
        };
    }

    public static object ApplyFields(object value, string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
        {
            return value;
        }

        var fieldSet = fields
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(field => field.Trim())
            .Where(field => field.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (fieldSet.Count == 0)
        {
            return value;
        }

        var node = JsonSerializer.SerializeToNode(value, CompactJsonOptions);
        return FilterNode(node, fieldSet) ?? value;
    }

    private static JsonNode? FilterNode(JsonNode? node, IReadOnlySet<string> fields)
    {
        if (node is JsonArray array)
        {
            var filtered = new JsonArray();
            foreach (var item in array)
            {
                filtered.Add(FilterNode(item?.DeepClone(), fields));
            }

            return filtered;
        }

        if (node is JsonObject obj)
        {
            var filtered = new JsonObject();
            foreach (var property in obj)
            {
                if (fields.Contains(property.Key))
                {
                    filtered[property.Key] = property.Value?.DeepClone();
                }
            }

            return filtered;
        }

        return node;
    }

    private static string FormatText(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, CompactJsonOptions);
        if (node is JsonArray array)
        {
            return string.Join(Environment.NewLine, array.Select(FormatTextNode).Where(line => line.Length > 0));
        }

        if (node is JsonObject obj && obj.ContainsKey("symbol") && obj.ContainsKey("children"))
        {
            var lines = new List<string>();
            if (obj["symbol"] is not null)
            {
                lines.Add(FormatTextNode(obj["symbol"]));
            }

            if (obj["children"] is JsonArray children)
            {
                foreach (var child in children)
                {
                    var line = FormatTextNode(child);
                    if (line.Length > 0)
                    {
                        lines.Add("  " + line);
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        return FormatTextNode(node);
    }

    private static string FormatTextNode(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return node?.ToJsonString(CompactJsonOptions) ?? string.Empty;
        }

        var kind = obj["kind"]?.GetValue<string>() ?? obj["itemType"]?.GetValue<string>() ?? string.Empty;
        var name = obj["name"]?.GetValue<string>()
            ?? obj["qualifiedName"]?.GetValue<string>()
            ?? obj["path"]?.GetValue<string>()
            ?? string.Empty;
        var file = obj["file"]?.GetValue<string>() ?? obj["path"]?.GetValue<string>() ?? string.Empty;
        var startLine = obj["startLine"]?.GetValue<int?>()
            ?? obj["range"]?["startLine"]?.GetValue<int?>();
        var signature = obj["signature"]?.GetValue<string>();
        var summary = obj["summary"]?.GetValue<string>() ?? obj["symbolSummary"]?.GetValue<string>() ?? obj["fileSummary"]?.GetValue<string>();
        var location = startLine is int line && !string.IsNullOrWhiteSpace(file)
            ? $"{file}:{line}"
            : file;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            parts.Add(kind);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            parts.Add(location);
        }

        if (!string.IsNullOrWhiteSpace(signature))
        {
            parts.Add(signature);
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            parts.Add(summary);
        }

        return string.Join("  ", parts);
    }
}

public sealed record CodeIndexOutlineResult(object Symbol, object Children);
