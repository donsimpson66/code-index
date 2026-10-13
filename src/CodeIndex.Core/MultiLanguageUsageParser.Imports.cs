using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageUsageParser
{
    private static JavaImportInfo ParseJavaImports(string source)
    {
        var importedTypeAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedPackages = new HashSet<string>(StringComparer.Ordinal);
        var importedStaticMemberAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedStaticTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in JavaImportRegex.Matches(source))
        {
            var importPath = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(importPath))
            {
                continue;
            }

            if (match.Groups["static"].Success)
            {
                if (importPath.EndsWith(".*", StringComparison.Ordinal))
                {
                    importedStaticTypes.Add(importPath[..^2]);
                }
                else
                {
                    importedStaticMemberAliases[importPath.Split('.').Last()] = importPath;
                }

                continue;
            }

            if (importPath.EndsWith(".*", StringComparison.Ordinal))
            {
                importedPackages.Add(importPath[..^2]);
                continue;
            }

            var alias = importPath.Split('.').Last();
            importedTypeAliases[alias] = importPath;
        }

        return new JavaImportInfo(importedTypeAliases, importedPackages, importedStaticMemberAliases, importedStaticTypes);
    }

    private static GoImportInfo ParseGoImports(string source)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedPackages = new HashSet<string>(StringComparer.Ordinal);
        var inImportBlock = false;

        foreach (var rawLine in MultiLanguageSymbolParser.SplitLines(source))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "import (", StringComparison.Ordinal))
            {
                inImportBlock = true;
                continue;
            }

            if (inImportBlock && string.Equals(line, ")", StringComparison.Ordinal))
            {
                inImportBlock = false;
                continue;
            }

            var importSpec = inImportBlock
                ? line
                : line.StartsWith("import ", StringComparison.Ordinal) ? line[7..].Trim() : null;

            if (string.IsNullOrWhiteSpace(importSpec))
            {
                continue;
            }

            var match = GoImportSpecRegex.Match(importSpec);
            if (!match.Success)
            {
                continue;
            }

            var packageName = match.Groups["path"].Value.Split('/').Last();
            var alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : packageName;
            if (alias is "_" or ".")
            {
                continue;
            }

            bindings[alias] = packageName;
            importedPackages.Add(packageName);
        }

        return new GoImportInfo(bindings, importedPackages);
    }

    private static PythonImportInfo ParsePythonImports(string source)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in PythonImportRegex.Matches(source))
        {
            var moduleName = match.Groups["module"].Value;
            var alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : moduleName.Split('.').Last();
            bindings[alias] = moduleName;
        }

        foreach (Match match in PythonFromImportRegex.Matches(source))
        {
            var moduleName = match.Groups["module"].Value;
            foreach (var nameBinding in match.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var aliasParts = nameBinding.Split(" as ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (aliasParts.Length == 0)
                {
                    continue;
                }

                var importedName = aliasParts[0];
                var alias = aliasParts.Length > 1 ? aliasParts[1] : importedName;
                bindings[alias] = $"{moduleName}.{importedName}";
            }
        }

        return new PythonImportInfo(bindings);
    }

    private static RustImportInfo ParseRustImports(string source)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in RustUseRegex.Matches(source))
        {
            var rest = match.Groups["rest"].Value.Trim();
            if (string.IsNullOrWhiteSpace(rest))
            {
                continue;
            }

            var braceStart = rest.IndexOf('{');
            if (braceStart >= 0)
            {
                var braceEnd = rest.LastIndexOf('}');
                if (braceEnd <= braceStart)
                {
                    continue;
                }

                var modulePath = rest[..braceStart].Trim().TrimEnd(':').Replace("::", ".", StringComparison.Ordinal);
                var names = rest[(braceStart + 1)..braceEnd]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var rawName in names)
                {
                    var name = rawName.Split([" as "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
                    if (string.IsNullOrWhiteSpace(modulePath))
                    {
                        bindings[name] = name;
                    }
                    else
                    {
                        bindings[name] = $"{modulePath}.{name}";
                    }
                }

                continue;
            }

            var segments = rest.Replace("::", ".", StringComparison.Ordinal)
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var importedName = segments[^1].Split([" as "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
            var aliasParts = rest.Split([" as "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (aliasParts.Length > 1)
            {
                importedName = aliasParts[^1].Trim();
            }

            var qualifiedName = string.Join('.', segments.Select(segment =>
                segment.Split([" as "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0].Trim()));
            bindings[importedName] = qualifiedName;
        }

        return new RustImportInfo(bindings);
    }

    private static PhpImportInfo ParsePhpImports(string source)
    {
        var typeBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        var functionBindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in PhpUseRegex.Matches(source))
        {
            var path = match.Groups["path"].Value.Replace('\\', '.');
            var alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : path.Split('.').Last();
            if (match.Groups["function"].Success)
            {
                functionBindings[alias] = path;
            }
            else
            {
                typeBindings[alias] = path;
            }
        }

        return new PhpImportInfo(typeBindings, functionBindings);
    }

    private static HashSet<string> ParseTypeScriptImports(string currentFilePath, string source)
    {
        return ParseTypeScriptImportBindings(currentFilePath, source).Values.ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> ParseTypeScriptImportBindings(string currentFilePath, string source)
    {
        var currentModule = MultiLanguageSymbolParser.BuildLogicalModuleQualifier(currentFilePath);
        var currentSegments = currentModule.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (currentSegments.Count > 0)
        {
            currentSegments.RemoveAt(currentSegments.Count - 1);
        }

        var importedBindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in TypeScriptImportRegex.Matches(source))
        {
            var importPath = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(importPath) || !importPath.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var resolvedSegments = new List<string>(currentSegments);
            foreach (var segment in importPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (resolvedSegments.Count > 0)
                    {
                        resolvedSegments.RemoveAt(resolvedSegments.Count - 1);
                    }

                    continue;
                }

                resolvedSegments.Add(segment);
            }

            if (resolvedSegments.Count > 0)
            {
                var resolvedModule = string.Join('.', resolvedSegments);
                if (match.Groups["default"].Success)
                {
                    importedBindings[match.Groups["default"].Value] = resolvedModule;
                }

                if (match.Groups["named"].Success)
                {
                    foreach (var binding in match.Groups["named"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var aliasParts = binding.Split(" as ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (aliasParts.Length == 0)
                        {
                            continue;
                        }

                        var alias = aliasParts.Length > 1 ? aliasParts[1] : aliasParts[0];
                        importedBindings[alias] = resolvedModule;
                    }
                }

                if (match.Groups["namespace"].Success)
                {
                    importedBindings[match.Groups["namespace"].Value] = resolvedModule;
                }
            }
        }

        return importedBindings;
    }

    private sealed record JavaImportInfo(
        IReadOnlyDictionary<string, string> ImportedTypeAliases,
        IReadOnlySet<string> ImportedPackages,
        IReadOnlyDictionary<string, string> ImportedStaticMemberAliases,
        IReadOnlySet<string> ImportedStaticTypes)
    {
        public IEnumerable<string> ImportedTypes => ImportedTypeAliases.Values;

        public IEnumerable<string> ImportedStaticMembers => ImportedStaticMemberAliases.Values;
    }

    private sealed record GoImportInfo(
        IReadOnlyDictionary<string, string> Bindings,
        IReadOnlySet<string> ImportedPackages);

    private sealed record PythonImportInfo(IReadOnlyDictionary<string, string> Bindings);

    private sealed record PhpImportInfo(
        IReadOnlyDictionary<string, string> TypeBindings,
        IReadOnlyDictionary<string, string> FunctionBindings);

    private sealed record RustImportInfo(IReadOnlyDictionary<string, string> Bindings);
}