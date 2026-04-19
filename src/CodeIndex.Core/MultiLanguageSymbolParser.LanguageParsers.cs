using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageSymbolParser
{
    private static IReadOnlyList<SymbolRecord> ParseJavaOrCSharp(FileRecord file, string source)
    {
        var namespaceName = PackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
        return ParseBraceLanguage(file, source, namespaceName, JavaOrCSharpTypeRegex, JavaOrCSharpMethodRegex, "constructor");
    }

    private static IReadOnlyList<SymbolRecord> ParseTypeScript(FileRecord file, string source)
    {
        var moduleName = BuildLogicalModuleQualifier(file.Path);
        var symbols = new List<SymbolRecord>();
        var moduleSymbol = CreateContainerSymbol(file, Path.GetFileNameWithoutExtension(file.Path), moduleName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);
        symbols.AddRange(ParseBraceLanguage(file, source, moduleName, TypeScriptTypeRegex, TypeScriptMethodRegex, "constructor", TypeScriptFunctionRegex, TypeScriptArrowFunctionRegex, TypeScriptFieldRegex, moduleSymbol.Id, TypeScriptTypeRegex));
        return symbols;
    }

    private static IReadOnlyList<SymbolRecord> ParsePhp(FileRecord file, string source)
    {
        var namespaceName = PhpNamespaceRegex.Match(source) is { Success: true } namespaceMatch
            ? namespaceMatch.Groups[1].Value.Replace('\\', '.')
            : BuildLogicalModuleQualifier(file.Path);
        var symbols = new List<SymbolRecord>();
        var namespaceLine = PhpNamespaceRegex.Match(source) is { Success: true } phpNamespaceMatch ? GetLineNumber(source, phpNamespaceMatch.Index) : 1;
        var namespaceSymbol = CreateContainerSymbol(file, namespaceName.Split('.').Last(), namespaceName, SymbolKinds.Namespace, namespaceLine);
        symbols.Add(namespaceSymbol);
        symbols.AddRange(ParseBraceLanguage(file, source, namespaceName, PhpTypeRegex, PhpFunctionRegex, "__construct", PhpFunctionRegex, fieldRegex: PhpFieldRegex, rootParentId: namespaceSymbol.Id));
        return symbols;
    }

    private static IReadOnlyList<SymbolRecord> ParseGo(FileRecord file, string source)
    {
        var packageName = GoPackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
        var lines = SplitLines(source);
        var symbols = new List<SymbolRecord>();
        var typesByName = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);
        var moduleSymbol = CreateContainerSymbol(file, packageName, packageName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            var typeMatch = GoTypeRegex.Match(line);
            if (typeMatch.Success)
            {
                var typeName = typeMatch.Groups[1].Value;
                var typeQualifiedName = BuildQualifiedName(packageName, typeName);
                var kind = line.Contains("interface", StringComparison.Ordinal) ? SymbolKinds.Interface : SymbolKinds.Struct;
                var symbol = CreateSymbol(file, typeName, typeQualifiedName, kind, moduleSymbol.Id, lineNumber, line);
                symbols.Add(symbol);
                typesByName[typeName] = symbol;
                continue;
            }

            var funcMatch = GoFuncRegex.Match(line);
            if (!funcMatch.Success)
            {
                continue;
            }

            var methodName = funcMatch.Groups["name"].Value;
            var receiverType = ExtractGoReceiverType(funcMatch.Groups["receiver"].Value);
            SymbolRecord? parentSymbol = null;
            var parentId = receiverType is not null && typesByName.TryGetValue(receiverType, out parentSymbol)
                ? parentSymbol.Id
                : null;
            var methodQualifiedName = parentSymbol is not null
                ? BuildQualifiedName(parentSymbol.QualifiedName, methodName)
                : BuildQualifiedName(packageName, methodName);
            symbols.Add(CreateSymbol(file, methodName, methodQualifiedName, SymbolKinds.Method, parentId ?? moduleSymbol.Id, lineNumber, line));
        }

        return symbols;
    }

    private static IReadOnlyList<SymbolRecord> ParsePython(FileRecord file, string source)
    {
        var moduleName = BuildLogicalModuleQualifier(file.Path);
        var lines = SplitLines(source);
        var symbols = new List<SymbolRecord>();
        var classScopes = new Stack<IndentScope>();
        var moduleSymbol = CreateContainerSymbol(file, moduleName.Split('.').Last(), moduleName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);
        var propertiesByQualifiedName = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = GetIndentWidth(line);

            while (classScopes.Count > 0 && indent <= classScopes.Peek().Indent)
            {
                classScopes.Pop();
            }

            var classMatch = PythonClassRegex.Match(line);
            if (classMatch.Success)
            {
                var className = classMatch.Groups["name"].Value;
                var parentSymbol = classScopes.Count > 0 ? classScopes.Peek().Symbol : null;
                var classQualifiedName = parentSymbol is null
                    ? BuildQualifiedName(moduleName, className)
                    : BuildQualifiedName(parentSymbol.QualifiedName, className);
                var symbol = CreateSymbol(file, className, classQualifiedName, SymbolKinds.Class, parentSymbol?.Id ?? moduleSymbol.Id, lineNumber, line);
                symbols.Add(symbol);
                classScopes.Push(new IndentScope(indent, symbol));
                continue;
            }

            var methodMatch = PythonMethodRegex.Match(line);
            if (!methodMatch.Success)
            {
                continue;
            }

            var methodName = methodMatch.Groups["name"].Value;
            var parent = classScopes.Count > 0 ? classScopes.Peek().Symbol : null;
            var kind = parent is not null && string.Equals(methodName, "__init__", StringComparison.Ordinal)
                ? SymbolKinds.Constructor
                : SymbolKinds.Method;
            var methodQualifiedName = parent is null
                ? BuildQualifiedName(moduleName, methodName)
                : BuildQualifiedName(parent.QualifiedName, methodName);
            symbols.Add(CreateSymbol(file, methodName, methodQualifiedName, kind, parent?.Id ?? moduleSymbol.Id, lineNumber, line));

            if (parent is not null)
            {
                continue;
            }
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (classScopes.Count == 0)
            {
            }
            var propertyMatch = Regex.Match(line, @"self\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.CultureInvariant);
            if (!propertyMatch.Success)
            {
                continue;
            }

            SymbolRecord? owner = null;
            for (var lookback = index; lookback >= 0; lookback--)
            {
                var currentLine = lines[lookback];
                var classMatch = PythonClassRegex.Match(currentLine);
                if (!classMatch.Success)
                {
                    continue;
                }

                var candidateName = classMatch.Groups["name"].Value;
                owner = symbols.FirstOrDefault(symbol => symbol.Kind == SymbolKinds.Class && symbol.Name == candidateName && symbol.Range.StartLine == lookback + 1);
                if (owner is not null)
                {
                    break;
                }
            }

            if (owner is null)
            {
                continue;
            }

            var propertyName = propertyMatch.Groups["name"].Value;
            var propertyQualifiedName = BuildQualifiedName(owner.QualifiedName, propertyName);
            if (!propertiesByQualifiedName.Add(propertyQualifiedName))
            {
                continue;
            }

            symbols.Add(CreateSymbol(file, propertyName, propertyQualifiedName, SymbolKinds.Property, owner.Id, index + 1, line));
        }

        return symbols;
    }
}
