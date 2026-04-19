using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageUsageParser
{
    private static IReadOnlyDictionary<(string CallableId, string Name), string> ParseTypeScriptCallableTypeHints(string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var callableByLine = fileSymbols
            .Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor)
            .ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var typeHints = new Dictionary<(string CallableId, string Name), string>();

        foreach (var entry in callableByLine)
        {
            var declarationLine = lines[entry.Key - 1];
            var openParen = declarationLine.IndexOf('(');
            var closeParen = declarationLine.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen)
            {
                continue;
            }

            var parameterList = declarationLine[(openParen + 1)..closeParen];
            foreach (Match match in TypeScriptCallableParameterRegex.Matches(parameterList))
            {
                typeHints[(entry.Value.Id, match.Groups["name"].Value)] = match.Groups["type"].Value;
            }
        }

        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.TypeScriptTypeRegex, MultiLanguageSymbolParser.TypeScriptMethodRegex);
        foreach (var context in contexts)
        {
            if (context.Callable is null)
            {
                continue;
            }

            var typedVariableMatch = TypeScriptVariableTypeRegex.Match(context.Line);
            if (typedVariableMatch.Success)
            {
                typeHints[(context.Callable.Id, typedVariableMatch.Groups["name"].Value)] = typedVariableMatch.Groups["type"].Value;
            }

            var newVariableMatch = TypeScriptVariableNewRegex.Match(context.Line);
            if (newVariableMatch.Success)
            {
                typeHints[(context.Callable.Id, newVariableMatch.Groups["name"].Value)] = newVariableMatch.Groups["type"].Value;
            }

            var aliasMatch = TypeScriptVariableAliasRegex.Match(context.Line);
            if (aliasMatch.Success)
            {
                var sourceName = aliasMatch.Groups["source"].Value;
                string? sourceType = null;
                if (context.Type is not null && aliasMatch.Groups["owner"].Success)
                {
                    fieldTypeLookup.TryGetValue((context.Type.Id, sourceName), out sourceType);
                }
                else
                {
                    typeHints.TryGetValue((context.Callable.Id, sourceName), out sourceType);
                }

                if (!string.IsNullOrWhiteSpace(sourceType))
                {
                    typeHints[(context.Callable.Id, aliasMatch.Groups["name"].Value)] = sourceType;
                }
            }
        }

        return typeHints;
    }

    private static IReadOnlyDictionary<string, SymbolRecord> ResolveImportedTypeLookup(IReadOnlyDictionary<string, string> importedBindings, IReadOnlyList<SymbolRecord> candidateSymbols)
    {
        var lookup = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (var binding in importedBindings)
        {
            var target = candidateSymbols.FirstOrDefault(symbol =>
                MultiLanguageSymbolParser.IsTypeSymbol(symbol) &&
                string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), binding.Value, StringComparison.Ordinal) &&
                string.Equals(symbol.Name, binding.Key, StringComparison.Ordinal));

            if (target is not null)
            {
                lookup[binding.Key] = target;
            }
        }

        return lookup;
    }

    private static IReadOnlyDictionary<string, SymbolRecord> ResolveImportedCallableLookup(IReadOnlyDictionary<string, string> importedBindings, IReadOnlyList<SymbolRecord> candidateSymbols)
    {
        var lookup = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (var binding in importedBindings)
        {
            var target = candidateSymbols.FirstOrDefault(symbol =>
                CallableKinds.Contains(symbol.Kind) &&
                string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), binding.Value, StringComparison.Ordinal) &&
                string.Equals(symbol.Name, binding.Key, StringComparison.Ordinal));

            if (target is not null)
            {
                lookup[binding.Key] = target;
            }
        }

        return lookup;
    }

    private static IReadOnlyDictionary<string, SymbolRecord> ResolveImportedMemberLookup(IReadOnlyDictionary<string, string> importedBindings, IReadOnlyList<SymbolRecord> candidateSymbols)
    {
        var lookup = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (var binding in importedBindings)
        {
            var target = candidateSymbols.FirstOrDefault(symbol => string.Equals(symbol.QualifiedName, binding.Value, StringComparison.Ordinal));
            if (target is not null)
            {
                lookup[binding.Key] = target;
            }
        }

        return lookup;
    }

    private static IReadOnlyDictionary<(string OwnerId, string Name), string> ParsePythonFieldTypes(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var contexts = BuildPythonContexts(source, fileSymbols);
        var fieldTypes = new Dictionary<(string OwnerId, string Name), string>();

        foreach (var context in contexts)
        {
            if (context.Type is null)
            {
                continue;
            }

            var assignmentMatch = PythonAssignmentTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && assignmentMatch.Groups["owner"].Success)
            {
                fieldTypes[(context.Type.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PythonAliasAssignmentRegex.Match(context.Line);
            if (aliasMatch.Success && aliasMatch.Groups["owner"].Success)
            {
                if (aliasMatch.Groups["sourceOwner"].Success && fieldTypes.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out var fieldType))
                {
                    fieldTypes[(context.Type.Id, aliasMatch.Groups["name"].Value)] = fieldType;
                }
            }
        }

        return fieldTypes;
    }

    private static IReadOnlyDictionary<(string CallableId, string Name), string> ParsePythonCallableTypeHints(string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup)
    {
        var typeHints = new Dictionary<(string CallableId, string Name), string>();
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);

        foreach (var entry in callableByLine)
        {
            var declarationLine = lines[entry.Key - 1];
            var openParen = declarationLine.IndexOf('(');
            var closeParen = declarationLine.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen)
            {
                continue;
            }

            foreach (Match match in PythonCallableParameterRegex.Matches(declarationLine[(openParen + 1)..closeParen]))
            {
                typeHints[(entry.Value.Id, match.Groups["name"].Value)] = match.Groups["type"].Value;
            }
        }

        foreach (var context in BuildPythonContexts(source, fileSymbols))
        {
            if (context.Callable is null)
            {
                continue;
            }

            var assignmentMatch = PythonAssignmentTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && !assignmentMatch.Groups["owner"].Success)
            {
                typeHints[(context.Callable.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PythonAliasAssignmentRegex.Match(context.Line);
            if (!aliasMatch.Success || aliasMatch.Groups["owner"].Success)
            {
                continue;
            }

            string? sourceType = null;
            if (context.Type is not null && aliasMatch.Groups["sourceOwner"].Success)
            {
                fieldTypeLookup.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }
            else
            {
                typeHints.TryGetValue((context.Callable.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                typeHints[(context.Callable.Id, aliasMatch.Groups["name"].Value)] = sourceType;
            }
        }

        return typeHints;
    }

    private static IReadOnlyDictionary<(string OwnerId, string Name), string> ParsePhpFieldTypes(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var fieldTypes = new Dictionary<(string OwnerId, string Name), string>();

        foreach (var context in BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.PhpTypeRegex, MultiLanguageSymbolParser.PhpFunctionRegex))
        {
            if (context.Type is null)
            {
                continue;
            }

            var declaredFieldMatch = PhpFieldTypeRegex.Match(context.Line);
            if (declaredFieldMatch.Success)
            {
                fieldTypes[(context.Type.Id, declaredFieldMatch.Groups["name"].Value)] = NormalizePhpTypeName(declaredFieldMatch.Groups["type"].Value);
                continue;
            }

            var assignmentMatch = PhpAssignmentNewTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && assignmentMatch.Groups["owner"].Success)
            {
                fieldTypes[(context.Type.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PhpAliasAssignmentRegex.Match(context.Line);
            if (aliasMatch.Success && aliasMatch.Groups["owner"].Success && aliasMatch.Groups["sourceOwner"].Success && fieldTypes.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out var fieldType))
            {
                fieldTypes[(context.Type.Id, aliasMatch.Groups["name"].Value)] = fieldType;
            }
        }

        return fieldTypes;
    }

    private static IReadOnlyDictionary<(string CallableId, string Name), string> ParsePhpCallableTypeHints(string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup)
    {
        var typeHints = new Dictionary<(string CallableId, string Name), string>();
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);

        foreach (var entry in callableByLine)
        {
            var declarationLine = lines[entry.Key - 1];
            var openParen = declarationLine.IndexOf('(');
            var closeParen = declarationLine.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen)
            {
                continue;
            }

            foreach (Match match in PhpCallableParameterRegex.Matches(declarationLine[(openParen + 1)..closeParen]))
            {
                typeHints[(entry.Value.Id, match.Groups["name"].Value)] = NormalizePhpTypeName(match.Groups["type"].Value);
            }
        }

        foreach (var context in BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.PhpTypeRegex, MultiLanguageSymbolParser.PhpFunctionRegex))
        {
            if (context.Callable is null)
            {
                continue;
            }

            var assignmentMatch = PhpAssignmentNewTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && !assignmentMatch.Groups["owner"].Success)
            {
                typeHints[(context.Callable.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PhpAliasAssignmentRegex.Match(context.Line);
            if (!aliasMatch.Success || aliasMatch.Groups["owner"].Success)
            {
                continue;
            }

            string? sourceType = null;
            if (context.Type is not null && aliasMatch.Groups["sourceOwner"].Success)
            {
                fieldTypeLookup.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }
            else
            {
                typeHints.TryGetValue((context.Callable.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                typeHints[(context.Callable.Id, aliasMatch.Groups["name"].Value)] = sourceType;
            }
        }

        return typeHints;
    }

    private static string NormalizePhpTypeName(string value)
    {
        return value.TrimStart('?', '\\').Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0].Split('\\').Last();
    }

    private static IReadOnlyDictionary<(string OwnerId, string Name), string> ParseTypeScriptFieldTypes(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var ownerByLine = fileSymbols
            .Where(MultiLanguageSymbolParser.IsTypeSymbol)
            .ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var fieldTypes = new Dictionary<(string OwnerId, string Name), string>();
        SymbolRecord? currentOwner = null;
        var braceDepth = 0;
        SymbolRecord? pendingType = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (ownerByLine.TryGetValue(lineNumber, out var declaredType))
            {
                pendingType = declaredType;
            }

            var fieldMatch = TypeScriptFieldTypeRegex.Match(line);
            if (currentOwner is not null && fieldMatch.Success)
            {
                fieldTypes[(currentOwner.Id, fieldMatch.Groups["name"].Value)] = fieldMatch.Groups["type"].Value;
            }

            if (currentOwner is not null)
            {
                var assignmentMatch = TypeScriptFieldAssignmentNewRegex.Match(line);
                if (assignmentMatch.Success)
                {
                    fieldTypes[(currentOwner.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                }

                var aliasMatch = TypeScriptFieldAssignmentAliasRegex.Match(line);
                if (aliasMatch.Success)
                {
                    var sourceName = aliasMatch.Groups["source"].Value;
                    string? sourceType = null;
                    if (aliasMatch.Groups["owner"].Success)
                    {
                        fieldTypes.TryGetValue((currentOwner.Id, sourceName), out sourceType);
                    }

                    if (!string.IsNullOrWhiteSpace(sourceType))
                    {
                        fieldTypes[(currentOwner.Id, aliasMatch.Groups["name"].Value)] = sourceType;
                    }
                }
            }

            var openBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '{');
            var closeBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (pendingType is not null && openBraceCount > closeBraceCount)
            {
                currentOwner = pendingType;
                pendingType = null;
            }

            if (currentOwner is not null && braceDepth <= 0)
            {
                currentOwner = null;
            }
        }

        return fieldTypes;
    }
}