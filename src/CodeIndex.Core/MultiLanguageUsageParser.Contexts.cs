using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageUsageParser
{
    private static IReadOnlyList<ScopedLineContext> BuildBraceContexts(string source, IReadOnlyList<SymbolRecord> fileSymbols, Regex typeRegex, Regex callableRegex)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var results = new List<ScopedLineContext>(lines.Length);
        var typeByLine = fileSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var typeScopes = new Stack<ScopedSymbol>();
        ScopedSymbol? callableScope = null;
        SymbolRecord? pendingType = null;
        SymbolRecord? pendingCallable = null;
        var braceDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            while (typeScopes.Count > 0 && braceDepth < typeScopes.Peek().BodyDepth)
            {
                typeScopes.Pop();
            }

            if (callableScope is not null && braceDepth < callableScope.BodyDepth)
            {
                callableScope = null;
            }

            results.Add(new ScopedLineContext(lineNumber, line, typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null, callableScope?.Symbol));

            typeByLine.TryGetValue(lineNumber, out var declaredType);
            callableByLine.TryGetValue(lineNumber, out var declaredCallable);
            var openBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '{');
            var closeBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (declaredType is not null)
            {
                if (openBraceCount > closeBraceCount)
                {
                    typeScopes.Push(new ScopedSymbol(braceDepth, declaredType));
                }
                else
                {
                    pendingType = declaredType;
                }
            }
            else if (pendingType is not null && openBraceCount > closeBraceCount)
            {
                typeScopes.Push(new ScopedSymbol(braceDepth, pendingType));
                pendingType = null;
            }

            if (declaredCallable is not null)
            {
                if (openBraceCount > closeBraceCount)
                {
                    callableScope = new ScopedSymbol(braceDepth, declaredCallable);
                }
                else
                {
                    pendingCallable = declaredCallable;
                }
            }
            else if (pendingCallable is not null && openBraceCount > closeBraceCount)
            {
                callableScope = new ScopedSymbol(braceDepth, pendingCallable);
                pendingCallable = null;
            }
        }

        return results;
    }

    private static IReadOnlyList<ScopedLineContext> BuildGoContexts(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var results = new List<ScopedLineContext>(lines.Length);
        var typeByLine = fileSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableScope = default(ScopedSymbol);
        var currentType = default(SymbolRecord);
        SymbolRecord? pendingCallable = null;
        var braceDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (callableScope is not null && braceDepth < callableScope.BodyDepth)
            {
                callableScope = null;
                currentType = null;
            }

            results.Add(new ScopedLineContext(lineNumber, line, currentType, callableScope?.Symbol));

            callableByLine.TryGetValue(lineNumber, out var declaredCallable);
            var openBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '{');
            var closeBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (declaredCallable is not null)
            {
                if (openBraceCount > closeBraceCount)
                {
                    callableScope = new ScopedSymbol(braceDepth, declaredCallable);
                    currentType = declaredCallable.ParentId is not null && typeByLine.Values.FirstOrDefault(symbol => string.Equals(symbol.Id, declaredCallable.ParentId, StringComparison.Ordinal)) is { } parentType
                        ? parentType
                        : null;
                }
                else
                {
                    pendingCallable = declaredCallable;
                }
            }
            else if (pendingCallable is not null && openBraceCount > closeBraceCount)
            {
                callableScope = new ScopedSymbol(braceDepth, pendingCallable);
                currentType = pendingCallable.ParentId is not null && typeByLine.Values.FirstOrDefault(symbol => string.Equals(symbol.Id, pendingCallable.ParentId, StringComparison.Ordinal)) is { } parentType
                    ? parentType
                    : null;
                pendingCallable = null;
            }
        }

        return results;
    }

    private static IReadOnlyList<ScopedLineContext> BuildPythonContexts(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var results = new List<ScopedLineContext>(lines.Length);
        var typeByLine = fileSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var typeScopes = new Stack<IndentScope>();
        var callableScopes = new Stack<IndentScope>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            var trimmedLine = line.TrimStart();
            var indent = MultiLanguageSymbolParser.GetIndentWidth(line);

            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("#", StringComparison.Ordinal))
            {
                while (callableScopes.Count > 0 && indent <= callableScopes.Peek().Indent)
                {
                    callableScopes.Pop();
                }

                while (typeScopes.Count > 0 && indent <= typeScopes.Peek().Indent)
                {
                    typeScopes.Pop();
                }
            }

            results.Add(new ScopedLineContext(lineNumber, line, typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null, callableScopes.Count > 0 ? callableScopes.Peek().Symbol : null));

            if (typeByLine.TryGetValue(lineNumber, out var declaredType))
            {
                typeScopes.Push(new IndentScope(indent, declaredType));
            }

            if (callableByLine.TryGetValue(lineNumber, out var declaredCallable))
            {
                callableScopes.Push(new IndentScope(indent, declaredCallable));
            }
        }

        return results;
    }

    private sealed record ScopedLineContext(int LineNumber, string Line, SymbolRecord? Type, SymbolRecord? Callable);

    private sealed record ScopedSymbol(int BodyDepth, SymbolRecord Symbol);

    private sealed record IndentScope(int Indent, SymbolRecord Symbol);
}