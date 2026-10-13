using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageSymbolParser
{
    private static readonly Regex RustTypeRegex = new(
        @"^\s*(?:pub(?:\s*\([^)]*\))?\s+)?(?<kind>struct|enum|trait|type)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RustImplRegex = new(
        @"^\s*impl(?:\s*<[^>]*>)?\s+(?:(?<trait>[A-Za-z_][A-Za-z0-9_:]*)\s+for\s+)?(?<type>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RustFnRegex = new(
        @"^\s*(?:pub(?:\s*\([^)]*\))?\s+)?(?:async\s+)?(?:const\s+)?(?:unsafe\s+)?fn\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RustConstOrStaticRegex = new(
        @"^\s*(?:pub(?:\s*\([^)]*\))?\s+)?(?<kind>const|static)\s+(?:mut\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IReadOnlyList<SymbolRecord> ParseJavaOrCSharp(FileRecord file, string source)
    {
        var namespaceName = PackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
        return ParseBraceLanguage(file, source, namespaceName, JavaOrCSharpTypeRegex, JavaOrCSharpMethodRegex, "constructor");
    }

    private static IReadOnlyList<SymbolRecord> ParseTypeScript(FileRecord file, string source)
    {
        return ParseEcmaScript(file, source);
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

    private static IReadOnlyList<SymbolRecord> ParseRust(FileRecord file, string source)
    {
        var moduleName = BuildLogicalModuleQualifier(file.Path);
        var lines = SplitLines(source);
        var symbols = new List<SymbolRecord>();
        var typesByName = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);
        var moduleSymbol = CreateContainerSymbol(file, moduleName.Split('.').Last(), moduleName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);

        var implScopes = new Stack<BraceScope>();
        SymbolRecord? pendingImplType = null;
        var braceDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            while (implScopes.Count > 0 && braceDepth < implScopes.Peek().BodyDepth)
            {
                implScopes.Pop();
            }

            var typeMatch = RustTypeRegex.Match(line);
            if (typeMatch.Success)
            {
                var typeName = typeMatch.Groups["name"].Value;
                var typeKindToken = typeMatch.Groups["kind"].Value;
                var kind = typeKindToken switch
                {
                    "struct" => SymbolKinds.Struct,
                    "enum" => SymbolKinds.Enum,
                    "trait" => SymbolKinds.Interface,
                    "type" => SymbolKinds.TypeAlias,
                    _ => SymbolKinds.Struct
                };
                var typeQualifiedName = BuildQualifiedName(moduleName, typeName);
                var typeSymbol = CreateSymbol(
                    file,
                    typeName,
                    typeQualifiedName,
                    kind,
                    moduleSymbol.Id,
                    lineNumber,
                    line,
                    ExtractRustAccessibility(line));
                symbols.Add(typeSymbol);
                if (kind != SymbolKinds.TypeAlias)
                {
                    typesByName[typeName] = typeSymbol;
                }
            }

            var implMatch = RustImplRegex.Match(line);
            if (implMatch.Success)
            {
                var implTypeName = implMatch.Groups["type"].Value;
                pendingImplType = typesByName.TryGetValue(implTypeName, out var implType) ? implType : null;
            }

            var fnMatch = RustFnRegex.Match(line);
            if (fnMatch.Success)
            {
                var methodName = fnMatch.Groups["name"].Value;
                var parent = implScopes.Count > 0 ? implScopes.Peek().Symbol : pendingImplType;
                var methodQualifiedName = parent is null
                    ? BuildQualifiedName(moduleName, methodName)
                    : BuildQualifiedName(parent.QualifiedName, methodName);
                symbols.Add(CreateSymbol(
                    file,
                    methodName,
                    methodQualifiedName,
                    SymbolKinds.Method,
                    parent?.Id ?? moduleSymbol.Id,
                    lineNumber,
                    line,
                    ExtractRustAccessibility(line)));
            }

            var constMatch = RustConstOrStaticRegex.Match(line);
            if (constMatch.Success && implScopes.Count == 0 && pendingImplType is null)
            {
                var constName = constMatch.Groups["name"].Value;
                symbols.Add(CreateSymbol(
                    file,
                    constName,
                    BuildQualifiedName(moduleName, constName),
                    SymbolKinds.Field,
                    moduleSymbol.Id,
                    lineNumber,
                    line,
                    ExtractRustAccessibility(line),
                    isStatic: true));
            }

            var openBraceCount = CountOccurrences(line, '{');
            var closeBraceCount = CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (pendingImplType is not null && openBraceCount > closeBraceCount)
            {
                implScopes.Push(new BraceScope(braceDepth, pendingImplType));
                pendingImplType = null;
            }
            else if (pendingImplType is not null && openBraceCount == 0 && closeBraceCount == 0)
            {
                // Keep pending until the opening brace line (possibly later).
            }
            else if (pendingImplType is not null && openBraceCount <= closeBraceCount)
            {
                pendingImplType = null;
            }
        }

        return symbols;
    }

    private static string ExtractRustAccessibility(string line)
    {
        return Regex.IsMatch(line, @"\bpub\b", RegexOptions.CultureInvariant) ? "public" : "private";
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
