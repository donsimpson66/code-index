using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageUsageParser
{
    private static readonly Regex EcmaHeritageRegex = new(@"\b(?<kind>class|interface)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)(?<heritage>[^{]*)\{", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ExtendsClauseRegex = new(@"\bextends\s+(?<types>.*?)(?=\bimplements\b|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ImplementsClauseRegex = new(@"\bimplements\s+(?<types>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<IReadOnlyList<EdgeRecord>> BuildInheritanceEdgesAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        CancellationToken cancellationToken)
    {
        var sourceRoot = MultiLanguageFileIndexBuilder.GetSourceRoot(inputPath);
        var edges = new List<EdgeRecord>();

        foreach (var file in files.Where(file => SourceLanguageCatalog.IsEcmaScript(file.Language)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(sourceRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var moduleName = MultiLanguageSymbolParser.BuildLogicalModuleQualifier(file.Path);
            var imports = ParseTypeScriptImportBindings(file.Path, source);
            var moduleSymbols = symbols.Where(symbol =>
                SourceLanguageCatalog.IsEcmaScript(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id)) &&
                (string.Equals(symbol.QualifiedName, moduleName, StringComparison.Ordinal) ||
                 symbol.QualifiedName.StartsWith(moduleName + ".", StringComparison.Ordinal)))
                .ToArray();
            var importedSymbols = symbols.Where(symbol =>
                SourceLanguageCatalog.IsEcmaScript(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id)) &&
                imports.Values.Any(module =>
                    string.Equals(symbol.QualifiedName, module, StringComparison.Ordinal) ||
                    symbol.QualifiedName.StartsWith(module + ".", StringComparison.Ordinal)))
                .ToArray();
            var candidates = moduleSymbols.Concat(importedSymbols).Distinct().ToArray();
            var types = candidates.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToArray();
            var localTypes = types
                .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
            var importedTypes = ResolveImportedTypeLookup(imports, candidates);
            var fileTypes = symbols.Where(symbol =>
                string.Equals(symbol.FileId, file.Id, StringComparison.Ordinal) &&
                symbol.Kind is SymbolKinds.Class or SymbolKinds.Interface)
                .ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);

            var maskedLines = EcmaScriptScanner.Mask(source);
            foreach (var (lineNumber, derived) in fileTypes)
            {
                EcmaScriptScanner.TryJoinDeclaration(maskedLines, lineNumber - 1, out var declaration, out _);
                var match = EcmaHeritageRegex.Match(declaration);
                if (!match.Success)
                {
                    continue;
                }

                AddHeritageEdges(match.Groups["heritage"].Value, ExtendsClauseRegex, EdgeTypes.Inherits, derived, localTypes, importedTypes, edges);
                AddHeritageEdges(match.Groups["heritage"].Value, ImplementsClauseRegex, EdgeTypes.Implements, derived, localTypes, importedTypes, edges);
            }
        }

        return edges
            .Distinct()
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddHeritageEdges(
        string heritage,
        Regex clauseRegex,
        string edgeType,
        SymbolRecord derived,
        IReadOnlyDictionary<string, SymbolRecord> localTypes,
        IReadOnlyDictionary<string, SymbolRecord> importedTypes,
        ICollection<EdgeRecord> edges)
    {
        var clause = clauseRegex.Match(heritage);
        if (!clause.Success)
        {
            return;
        }

        foreach (var token in clause.Groups["types"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = NormalizeHeritageType(token);
            if ((localTypes.TryGetValue(name, out var target) || importedTypes.TryGetValue(name, out target)) &&
                !string.Equals(target.Id, derived.Id, StringComparison.Ordinal))
            {
                edges.Add(new EdgeRecord(edgeType, derived.Id, target.Id));
            }
        }
    }

    private static string NormalizeHeritageType(string value)
    {
        var genericStart = value.IndexOf('<');
        var withoutGenerics = genericStart >= 0 ? value[..genericStart] : value;
        return withoutGenerics.Trim().Split('.').Last();
    }
}
