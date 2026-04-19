using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageUsageParser
{
    private static IReadOnlyList<ReferenceRecord> ExtractJavaReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var packageName = MultiLanguageSymbolParser.GetJavaPackageName(file, source);
        var imports = ParseJavaImports(source);
        var packageSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Java", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, packageName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(packageName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Java", StringComparison.Ordinal) &&
                (imports.ImportedPackages.Any(package => string.Equals(symbol.QualifiedName, package, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(package + ".", StringComparison.Ordinal)) ||
                 imports.ImportedTypes.Any(typeName => string.Equals(symbol.QualifiedName, typeName, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(typeName + ".", StringComparison.Ordinal)) ||
                 imports.ImportedStaticTypes.Any(typeName => string.Equals(symbol.QualifiedName, typeName, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(typeName + ".", StringComparison.Ordinal)) ||
                 imports.ImportedStaticMembers.Any(memberName => string.Equals(symbol.QualifiedName, memberName, StringComparison.Ordinal))))
            .ToArray();
        var candidateSymbols = packageSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeSymbols = candidateSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToArray();
        var callableSymbols = candidateSymbols.Where(symbol => CallableKinds.Contains(symbol.Kind)).ToArray();
        var callableLookup = callableSymbols.ToDictionary(symbol => symbol.QualifiedName, StringComparer.Ordinal);
        var methodLookup = callableSymbols
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var typeLookup = typeSymbols
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.JavaOrCSharpTypeRegex, MultiLanguageSymbolParser.JavaOrCSharpMethodRegex);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in JavaMethodCallRegex.Matches(context.Line))
            {
                var callName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(callName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolveJavaMethodTarget(context.Type, qualifier, callName, packageName, imports, callableLookup, methodLookup, typeLookup);
                if (target is null)
                {
                    continue;
                }

                references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, callName, context.Line));
            }

            foreach (Match match in JavaConstructorReferenceRegex.Matches(context.Line))
            {
                var typeName = match.Groups["name"].Value;
                var targetType = ResolveJavaTypeTarget(typeName, packageName, imports, typeLookup);
                if (targetType is not null)
                {
                    references.Add(CreateReference(targetType, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, typeName, context.Line));
                }
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractGoReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var packageName = MultiLanguageSymbolParser.GetGoPackageName(file, source);
        var imports = ParseGoImports(source);
        var packageSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Go", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, packageName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(packageName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Go", StringComparison.Ordinal) &&
                imports.ImportedPackages.Any(importedPackage =>
                    string.Equals(symbol.QualifiedName, importedPackage, StringComparison.Ordinal) ||
                    symbol.QualifiedName.StartsWith(importedPackage + ".", StringComparison.Ordinal)))
            .ToArray();
        var candidateSymbols = packageSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeSymbols = candidateSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToArray();
        var callableSymbols = candidateSymbols.Where(symbol => CallableKinds.Contains(symbol.Kind)).ToArray();
        var packageFunctionLookup = packageSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || allSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedPackageFunctionLookup = callableSymbols
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => (MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var receiverMethodLookup = callableSymbols
            .Where(symbol => symbol.ParentId is not null)
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var typeLookup = typeSymbols
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var contexts = BuildGoContexts(source, fileSymbols);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in GoSelectorCallRegex.Matches(context.Line))
            {
                var methodName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(methodName))
                {
                    continue;
                }

                SymbolRecord? target = null;
                var qualifier = match.Groups["qualifier"].Value;

                if (imports.Bindings.TryGetValue(qualifier, out var importedPackageName) && importedPackageFunctionLookup.TryGetValue((importedPackageName, methodName), out var importedFunctions))
                {
                    target = importedFunctions[0];
                }
                else if (context.Type is not null && receiverMethodLookup.TryGetValue((context.Type.Id, methodName), out var methods))
                {
                    target = methods[0];
                }

                if (target is not null)
                {
                    references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, methodName, context.Line));
                }
            }

            foreach (Match match in GoCallRegex.Matches(context.Line))
            {
                var functionName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(functionName) || context.Line.Contains($".{functionName}(", StringComparison.Ordinal))
                {
                    continue;
                }

                if (packageFunctionLookup.TryGetValue(functionName, out var functions))
                {
                    references.Add(CreateReference(functions[0], context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, functionName, context.Line));
                }
            }

            foreach (var typeSymbol in typeLookup.Values)
            {
                var compositeLiteralToken = $"{typeSymbol.Name}{{";
                var index = context.Line.IndexOf(compositeLiteralToken, StringComparison.Ordinal);
                if (index >= 0)
                {
                    references.Add(CreateReference(typeSymbol, context.Callable.Id, file.Id, context.LineNumber, index + 1, typeSymbol.Name, context.Line));
                }
            }

            foreach (var binding in imports.Bindings)
            {
                foreach (var typeSymbol in typeSymbols.Where(symbol => string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), binding.Value, StringComparison.Ordinal)))
                {
                    var compositeLiteralToken = $"{binding.Key}.{typeSymbol.Name}{{";
                    var index = context.Line.IndexOf(compositeLiteralToken, StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        references.Add(CreateReference(typeSymbol, context.Callable.Id, file.Id, context.LineNumber, index + 1, typeSymbol.Name, context.Line));
                    }
                }
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractTypeScriptReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var moduleName = MultiLanguageSymbolParser.BuildLogicalModuleQualifier(file.Path);
        var moduleSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "TypeScript", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, moduleName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(moduleName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedModules = ParseTypeScriptImports(file.Path, source);
        var importedBindings = ParseTypeScriptImportBindings(file.Path, source);
        var importedSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "TypeScript", StringComparison.Ordinal) &&
            importedModules.Any(module =>
                string.Equals(symbol.QualifiedName, module, StringComparison.Ordinal) ||
                symbol.QualifiedName.StartsWith(module + ".", StringComparison.Ordinal)))
            .ToArray();
        var candidateSymbols = moduleSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeSymbols = candidateSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToArray();
        var callableSymbols = candidateSymbols.Where(symbol => CallableKinds.Contains(symbol.Kind)).ToArray();
        var propertySymbols = candidateSymbols.Where(symbol => symbol.Kind is SymbolKinds.Field or SymbolKinds.Property).ToArray();
        var typeLookup = typeSymbols
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var importedTypeLookup = ResolveImportedTypeLookup(importedBindings, candidateSymbols);
        var importedCallableLookup = ResolveImportedCallableLookup(importedBindings, candidateSymbols);
        var moduleFunctionLookup = callableSymbols.GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol).ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var topLevelFunctionLookup = callableSymbols
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedModuleFunctionLookup = callableSymbols
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => (MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var fieldTypeLookup = ParseTypeScriptFieldTypes(source, fileSymbols);
        var callableTypeHintLookup = ParseTypeScriptCallableTypeHints(source, fileSymbols, fieldTypeLookup);
        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.TypeScriptTypeRegex, MultiLanguageSymbolParser.TypeScriptMethodRegex);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in TypeScriptMethodCallRegex.Matches(context.Line))
            {
                var methodName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(methodName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolveTypeScriptCallableTarget(context.Type, context.Callable, qualifier, methodName, importedBindings, moduleFunctionLookup, topLevelFunctionLookup, importedModuleFunctionLookup, typeLookup, importedTypeLookup, importedCallableLookup, propertySymbols, fieldTypeLookup, callableTypeHintLookup);
                if (target is not null)
                {
                    references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, methodName, context.Line));
                }
            }

            foreach (Match match in TypeScriptConstructorReferenceRegex.Matches(context.Line))
            {
                var typeName = match.Groups["name"].Value;
                if (typeLookup.TryGetValue(typeName, out var targetType) || importedTypeLookup.TryGetValue(typeName, out targetType))
                {
                    references.Add(CreateReference(targetType, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, typeName, context.Line));
                }
            }

            foreach (var propertySymbol in propertySymbols)
            {
                var token = $".{propertySymbol.Name}";
                var index = context.Line.IndexOf(token, StringComparison.Ordinal);
                if (index >= 0 && (context.Type is null || string.Equals(propertySymbol.ParentId, context.Type.Id, StringComparison.Ordinal)))
                {
                    references.Add(CreateReference(propertySymbol, context.Callable.Id, file.Id, context.LineNumber, index + 2, propertySymbol.Name, context.Line));
                }
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractPythonReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var moduleName = MultiLanguageSymbolParser.BuildLogicalModuleQualifier(file.Path);
        var imports = ParsePythonImports(source);
        var moduleSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Python", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, moduleName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(moduleName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Python", StringComparison.Ordinal) &&
                imports.Bindings.Values.Any(target =>
                    string.Equals(symbol.QualifiedName, target, StringComparison.Ordinal) ||
                    symbol.QualifiedName.StartsWith(target + ".", StringComparison.Ordinal)))
            .ToArray();
        var candidateSymbols = moduleSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeLookup = candidateSymbols
            .Where(MultiLanguageSymbolParser.IsTypeSymbol)
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var methodLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var topLevelFunctionLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedModuleFunctionLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => (MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var importedMemberLookup = ResolveImportedMemberLookup(imports.Bindings, candidateSymbols);
        var fieldTypeLookup = ParsePythonFieldTypes(source, fileSymbols);
        var callableTypeHintLookup = ParsePythonCallableTypeHints(source, fileSymbols, fieldTypeLookup);
        var contexts = BuildPythonContexts(source, fileSymbols);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in PythonCallRegex.Matches(context.Line))
            {
                var callableName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(callableName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolvePythonCallableTarget(context.Type, context.Callable, qualifier, callableName, imports.Bindings, methodLookup, topLevelFunctionLookup, importedModuleFunctionLookup, typeLookup, importedMemberLookup, fieldTypeLookup, callableTypeHintLookup);
                if (target is null)
                {
                    continue;
                }

                references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, callableName, context.Line));
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractPhpReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var namespaceName = MultiLanguageSymbolParser.GetPhpNamespaceName(file, source);
        var imports = ParsePhpImports(source);
        var namespaceSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "PHP", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, namespaceName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(namespaceName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "PHP", StringComparison.Ordinal) &&
                (imports.TypeBindings.Values.Any(target => string.Equals(symbol.QualifiedName, target, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(target + ".", StringComparison.Ordinal)) ||
                 imports.FunctionBindings.Values.Any(target => string.Equals(symbol.QualifiedName, target, StringComparison.Ordinal))))
            .ToArray();
        var candidateSymbols = namespaceSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeLookup = candidateSymbols
            .Where(MultiLanguageSymbolParser.IsTypeSymbol)
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var methodLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var topLevelFunctionLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Namespace))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedFunctionLookup = ResolveImportedMemberLookup(imports.FunctionBindings, candidateSymbols);
        var importedTypeLookup = ResolveImportedMemberLookup(imports.TypeBindings, candidateSymbols);
        var fieldTypeLookup = ParsePhpFieldTypes(source, fileSymbols);
        var callableTypeHintLookup = ParsePhpCallableTypeHints(source, fileSymbols, fieldTypeLookup);
        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.PhpTypeRegex, MultiLanguageSymbolParser.PhpFunctionRegex);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in PhpCallRegex.Matches(context.Line))
            {
                var callableName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(callableName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolvePhpCallableTarget(context.Type, context.Callable, qualifier, callableName, methodLookup, topLevelFunctionLookup, typeLookup, importedTypeLookup, importedFunctionLookup, fieldTypeLookup, callableTypeHintLookup);
                if (target is null)
                {
                    continue;
                }

                references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, callableName, context.Line));
            }

            foreach (Match match in PhpConstructorReferenceRegex.Matches(context.Line))
            {
                var typeName = match.Groups["name"].Value;
                if (ResolvePhpTypeTarget(typeName, typeLookup, importedTypeLookup) is { } targetType)
                {
                    references.Add(CreateReference(targetType, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, typeName, context.Line));
                }
            }
        }

        return references;
    }
}
