using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageUsageParser
{
    private static readonly Regex JavaMethodCallRegex = new(@"(?:(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JavaConstructorReferenceRegex = new(@"\bnew\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JavaImportRegex = new(@"^\s*import\s+(?<static>static\s+)?(?<path>[A-Za-z_][A-Za-z0-9_\.]*(?:\.\*)?)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex TypeScriptMethodCallRegex = new(@"(?:(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptConstructorReferenceRegex = new(@"\bnew\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptImportRegex = new(@"^\s*import\s+(?:(?<default>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*)?(?:\{(?<named>[^}]*)\}|\*\s+as\s+(?<namespace>[A-Za-z_][A-Za-z0-9_]*))?\s*from\s*['""](?<path>[^'""]+)['""]", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex TypeScriptFieldTypeRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptCallableParameterRegex = new(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptVariableTypeRegex = new(@"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptVariableNewRegex = new(@"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptVariableAliasRegex = new(@"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:(?<owner>this)\.)?(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*;?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFieldAssignmentNewRegex = new(@"^\s*this\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFieldAssignmentAliasRegex = new(@"^\s*this\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:(?<owner>this)\.)?(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*;?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoSelectorCallRegex = new(@"(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoCallRegex = new(@"(?<!func\s)(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoImportSpecRegex = new(@"^(?:(?<alias>[A-Za-z_][A-Za-z0-9_]*|\.|_)\s+)?""(?<path>[^""]+)""$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonCallRegex = new(@"(?:(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonImportRegex = new(@"^\s*import\s+(?<module>[A-Za-z_][A-Za-z0-9_\.]*)\s*(?:as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PythonFromImportRegex = new(@"^\s*from\s+(?<module>[A-Za-z_][A-Za-z0-9_\.]*)\s+import\s+(?<names>[^\r\n]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PythonCallableParameterRegex = new(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonAssignmentTypeRegex = new(@"^\s*(?:(?<owner>self)\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonAliasAssignmentRegex = new(@"^\s*(?:(?<owner>self)\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:(?<sourceOwner>self)\.)?(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpCallRegex = new(@"(?:(?<qualifier>\$?[A-Za-z_][A-Za-z0-9_]*)\s*(?:->|::)\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpConstructorReferenceRegex = new(@"\bnew\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpUseRegex = new(@"^\s*use\s+(?<function>function\s+)?(?<path>[A-Za-z_\\][A-Za-z0-9_\\]*)(?:\s+as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PhpCallableParameterRegex = new(@"(?<type>[A-Za-z_\\][A-Za-z0-9_\\|?]*)\s+\$(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpFieldTypeRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly)\s+)*(?<type>[A-Za-z_\\][A-Za-z0-9_\\|?]*)\s+\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]+)?;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpAssignmentNewTypeRegex = new(@"^\s*(?:(?<owner>\$this)->)?(?:(?<dollar>\$)?(?<name>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*new\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpAliasAssignmentRegex = new(@"^\s*(?:(?<owner>\$this)->)?(?:(?<dollar>\$)?(?<name>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*(?:(?<sourceOwner>\$this)->)?\$(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> IgnoredCallNames = new(StringComparer.Ordinal)
    {
        "if",
        "for",
        "switch",
        "while",
        "catch",
        "return",
        "new",
        "func"
    };

    private static readonly HashSet<string> CallableKinds = new(StringComparer.Ordinal)
    {
        SymbolKinds.Method,
        SymbolKinds.Constructor
    };

    public static async Task<IReadOnlyList<EdgeRecord>> BuildCallEdgesAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        CancellationToken cancellationToken)
    {
        var references = await BuildReferencesAsync(inputPath, files, symbols, cancellationToken);
        return references
            .Where(reference => reference.SourceSymbolId is not null)
            .Select(reference => new EdgeRecord(EdgeTypes.Calls, reference.SourceSymbolId!, reference.TargetSymbolId))
            .Distinct()
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<IReadOnlyList<ReferenceRecord>> BuildReferencesAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        CancellationToken cancellationToken)
    {
        var sourceRoot = MultiLanguageFileIndexBuilder.GetSourceRoot(inputPath);
        var references = new List<ReferenceRecord>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Language is not ("Java" or "Go" or "TypeScript" or "Python" or "PHP"))
            {
                continue;
            }

            var fullPath = Path.Combine(sourceRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var fileSymbols = symbols.Where(symbol => string.Equals(symbol.FileId, file.Id, StringComparison.Ordinal)).ToArray();
            references.AddRange(file.Language switch
            {
                "Java" => ExtractJavaReferences(file, source, fileSymbols, symbols),
                "Go" => ExtractGoReferences(file, source, fileSymbols, symbols),
                "TypeScript" => ExtractTypeScriptReferences(file, source, fileSymbols, symbols),
                "Python" => ExtractPythonReferences(file, source, fileSymbols, symbols),
                "PHP" => ExtractPhpReferences(file, source, fileSymbols, symbols),
                _ => Array.Empty<ReferenceRecord>()
            });
        }

        return references
            .Distinct()
            .OrderBy(reference => reference.TargetSymbolId, StringComparer.Ordinal)
            .ThenBy(reference => reference.FileId, StringComparer.Ordinal)
            .ThenBy(reference => reference.Range.StartLine)
            .ThenBy(reference => reference.Range.StartColumn)
            .ThenBy(reference => reference.SourceSymbolId, StringComparer.Ordinal)
            .ToArray();
    }

    private static SymbolRecord? ResolveJavaMethodTarget(
        SymbolRecord? currentType,
        string? qualifier,
        string methodName,
        string packageName,
        JavaImportInfo imports,
        IReadOnlyDictionary<string, SymbolRecord> callableLookup,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            if (currentType is not null && (string.Equals(qualifier, "this", StringComparison.Ordinal) || string.Equals(qualifier, currentType.Name, StringComparison.Ordinal)) &&
                methodLookup.TryGetValue((currentType.Id, methodName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            var qualifiedType = ResolveJavaTypeTarget(qualifier, packageName, imports, typeLookup);
            if (qualifiedType is not null && methodLookup.TryGetValue((qualifiedType.Id, methodName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, methodName), out var methods))
        {
            return methods[0];
        }

        if (ResolveJavaStaticMethodTarget(methodName, imports, callableLookup, methodLookup, typeLookup) is { } staticTarget)
        {
            return staticTarget;
        }

        return null;
    }

    private static SymbolRecord? ResolveJavaStaticMethodTarget(
        string methodName,
        JavaImportInfo imports,
        IReadOnlyDictionary<string, SymbolRecord> callableLookup,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup)
    {
        if (imports.ImportedStaticMemberAliases.TryGetValue(methodName, out var staticMemberQualifiedName) && callableLookup.TryGetValue(staticMemberQualifiedName, out var explicitStaticTarget))
        {
            return explicitStaticTarget;
        }

        foreach (var staticTypeQualifiedName in imports.ImportedStaticTypes)
        {
            var typeName = staticTypeQualifiedName.Split('.').Last();
            if (typeLookup.TryGetValue(typeName, out var staticType) && string.Equals(staticType.QualifiedName, staticTypeQualifiedName, StringComparison.Ordinal) && methodLookup.TryGetValue((staticType.Id, methodName), out var staticMethods))
            {
                return staticMethods[0];
            }
        }

        return null;
    }

    private static SymbolRecord? ResolveJavaTypeTarget(string typeName, string packageName, JavaImportInfo imports, IReadOnlyDictionary<string, SymbolRecord> typeLookup)
    {
        if (typeLookup.TryGetValue(typeName, out var localType))
        {
            return localType;
        }

        if (imports.ImportedTypeAliases.TryGetValue(typeName, out var importedTypeName))
        {
            var importedSimpleName = importedTypeName.Split('.').Last();
            if (typeLookup.TryGetValue(importedSimpleName, out var importedType) && string.Equals(importedType.QualifiedName, importedTypeName, StringComparison.Ordinal))
            {
                return importedType;
            }
        }

        foreach (var importedPackage in imports.ImportedPackages)
        {
            var importedSimpleName = typeName;
            if (typeLookup.TryGetValue(importedSimpleName, out var candidate) && string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(candidate.QualifiedName), importedPackage, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static SymbolRecord? ResolveTypeScriptCallableTarget(
        SymbolRecord? currentType,
        SymbolRecord? currentCallable,
        string? qualifier,
        string methodName,
        IReadOnlyDictionary<string, string> importedBindings,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord[]> topLevelFunctionLookup,
        IReadOnlyDictionary<(string ModuleName, string Name), SymbolRecord[]> importedModuleFunctionLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedTypeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedCallableLookup,
        IReadOnlyList<SymbolRecord> propertySymbols,
        IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup,
        IReadOnlyDictionary<(string CallableId, string Name), string> callableTypeHintLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            if (currentType is not null && (string.Equals(qualifier, "this", StringComparison.Ordinal) || string.Equals(qualifier, currentType.Name, StringComparison.Ordinal)) &&
                methodLookup.TryGetValue((currentType.Id, methodName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            if (typeLookup.TryGetValue(qualifier, out var qualifiedType) && methodLookup.TryGetValue((qualifiedType.Id, methodName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }

            if (currentType is not null && fieldTypeLookup.TryGetValue((currentType.Id, qualifier), out var fieldTypeName) &&
                (typeLookup.TryGetValue(fieldTypeName, out var fieldType) || importedTypeLookup.TryGetValue(fieldTypeName, out fieldType)) &&
                methodLookup.TryGetValue((fieldType.Id, methodName), out var fieldTypeMethods))
            {
                return fieldTypeMethods[0];
            }

            if (currentCallable is not null && callableTypeHintLookup.TryGetValue((currentCallable.Id, qualifier), out var hintedTypeName) &&
                (typeLookup.TryGetValue(hintedTypeName, out var hintedType) || importedTypeLookup.TryGetValue(hintedTypeName, out hintedType)) &&
                methodLookup.TryGetValue((hintedType.Id, methodName), out var hintedTypeMethods))
            {
                return hintedTypeMethods[0];
            }

            if (importedBindings.TryGetValue(qualifier, out var importedModuleName) &&
                importedModuleFunctionLookup.TryGetValue((importedModuleName, methodName), out var importedModuleFunctions))
            {
                return importedModuleFunctions[0];
            }

            if (importedCallableLookup.TryGetValue(methodName, out var importedCallable))
            {
                return importedCallable;
            }

            if (topLevelFunctionLookup.TryGetValue(methodName, out var qualifiedFunctions))
            {
                return qualifiedFunctions[0];
            }
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, methodName), out var methods))
        {
            return methods[0];
        }

        if (importedCallableLookup.TryGetValue(methodName, out var topLevelImportedCallable))
        {
            return topLevelImportedCallable;
        }

        return topLevelFunctionLookup.TryGetValue(methodName, out var topLevelMethods) ? topLevelMethods[0] : null;
    }

    private static SymbolRecord? ResolvePythonCallableTarget(
        SymbolRecord? currentType,
        SymbolRecord? currentCallable,
        string? qualifier,
        string callableName,
        IReadOnlyDictionary<string, string> importedBindings,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord[]> topLevelFunctionLookup,
        IReadOnlyDictionary<(string ModuleName, string Name), SymbolRecord[]> importedModuleFunctionLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedMemberLookup,
        IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup,
        IReadOnlyDictionary<(string CallableId, string Name), string> callableTypeHintLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            if (currentType is not null && (string.Equals(qualifier, "self", StringComparison.Ordinal) || string.Equals(qualifier, "cls", StringComparison.Ordinal)) &&
                methodLookup.TryGetValue((currentType.Id, callableName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            if (importedBindings.TryGetValue(qualifier, out var importedTarget))
            {
                if (importedModuleFunctionLookup.TryGetValue((importedTarget, callableName), out var importedModuleFunctions))
                {
                    return importedModuleFunctions[0];
                }

                if (importedMemberLookup.TryGetValue(qualifier, out var importedMember) && MultiLanguageSymbolParser.IsTypeSymbol(importedMember) && methodLookup.TryGetValue((importedMember.Id, callableName), out var importedTypeMethods))
                {
                    return importedTypeMethods[0];
                }
            }

            if (typeLookup.TryGetValue(qualifier, out var qualifiedType) && methodLookup.TryGetValue((qualifiedType.Id, callableName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }

            if (currentType is not null && fieldTypeLookup.TryGetValue((currentType.Id, qualifier), out var fieldTypeName) && typeLookup.TryGetValue(fieldTypeName, out var fieldType) && methodLookup.TryGetValue((fieldType.Id, callableName), out var fieldTypeMethods))
            {
                return fieldTypeMethods[0];
            }

            if (currentCallable is not null && callableTypeHintLookup.TryGetValue((currentCallable.Id, qualifier), out var localTypeName) && typeLookup.TryGetValue(localTypeName, out var localType) && methodLookup.TryGetValue((localType.Id, callableName), out var localTypeMethods))
            {
                return localTypeMethods[0];
            }

            return null;
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, callableName), out var methods))
        {
            return methods[0];
        }

        if (importedMemberLookup.TryGetValue(callableName, out var importedMemberTarget))
        {
            return importedMemberTarget;
        }

        if (topLevelFunctionLookup.TryGetValue(callableName, out var topLevelMethods))
        {
            return topLevelMethods[0];
        }

        return typeLookup.TryGetValue(callableName, out var constructorType) ? constructorType : null;
    }

    private static SymbolRecord? ResolvePhpCallableTarget(
        SymbolRecord? currentType,
        SymbolRecord? currentCallable,
        string? qualifier,
        string callableName,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord[]> topLevelFunctionLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedTypeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedFunctionLookup,
        IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup,
        IReadOnlyDictionary<(string CallableId, string Name), string> callableTypeHintLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            var normalizedQualifier = qualifier.TrimStart('$');
            if (currentType is not null && string.Equals(qualifier, "$this", StringComparison.Ordinal) && methodLookup.TryGetValue((currentType.Id, callableName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            if (ResolvePhpTypeTarget(normalizedQualifier, typeLookup, importedTypeLookup) is { } qualifiedType && methodLookup.TryGetValue((qualifiedType.Id, callableName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }

            if (currentType is not null && fieldTypeLookup.TryGetValue((currentType.Id, normalizedQualifier), out var fieldTypeName) && ResolvePhpTypeTarget(fieldTypeName, typeLookup, importedTypeLookup) is { } fieldType && methodLookup.TryGetValue((fieldType.Id, callableName), out var fieldTypeMethods))
            {
                return fieldTypeMethods[0];
            }

            if (currentCallable is not null && callableTypeHintLookup.TryGetValue((currentCallable.Id, normalizedQualifier), out var localTypeName) && ResolvePhpTypeTarget(localTypeName, typeLookup, importedTypeLookup) is { } localType && methodLookup.TryGetValue((localType.Id, callableName), out var localTypeMethods))
            {
                return localTypeMethods[0];
            }

            return null;
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, callableName), out var methods))
        {
            return methods[0];
        }

        if (importedFunctionLookup.TryGetValue(callableName, out var importedFunction))
        {
            return importedFunction;
        }

        return topLevelFunctionLookup.TryGetValue(callableName, out var topLevelMethods) ? topLevelMethods[0] : null;
    }

    private static SymbolRecord? ResolvePhpTypeTarget(string typeName, IReadOnlyDictionary<string, SymbolRecord> typeLookup, IReadOnlyDictionary<string, SymbolRecord> importedTypeLookup)
    {
        if (importedTypeLookup.TryGetValue(typeName, out var importedType))
        {
            return importedType;
        }

        return typeLookup.TryGetValue(typeName, out var localType) ? localType : null;
    }

    private static ReferenceRecord CreateReference(SymbolRecord target, string? sourceSymbolId, string fileId, int lineNumber, int startColumn, string token, string line)
    {
        return new ReferenceRecord(
            target.Id,
            sourceSymbolId,
            fileId,
            new TextRangeRecord(lineNumber, startColumn, lineNumber, startColumn + token.Length - 1),
            line.Trim());
    }


}

internal static partial class MultiLanguageSymbolParser
{
    private static readonly Regex PackageRegex = new(@"^\s*package\s+([A-Za-z_][\w\.]*)\s*;?", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PhpNamespaceRegex = new(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_\\]*)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PythonClassRegex = new(@"^(?<indent>\s*)class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonMethodRegex = new(@"^(?<indent>\s*)(?:async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex JavaOrCSharpTypeRegex = new(@"\b(class|interface|enum|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex JavaOrCSharpMethodRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|internal|static|abstract|virtual|override|sealed|async|final|synchronized|partial|new)\s+)*(?<type>[A-Za-z_][A-Za-z0-9_<>,\[\]?\.\\]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;]*\)\s*(?:\{|=>)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex TypeScriptTypeRegex = new(@"\b(class|interface|enum|type)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFunctionRegex = new(@"^(?<indent>\s*)(?:(?:export|default|async|declare)\s+)*function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptArrowFunctionRegex = new(@"^(?<indent>\s*)(?:(?:export|default|declare)\s+)*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\([^=;]*\)\s*=>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex TypeScriptMethodRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|async|readonly|get|set|override|abstract)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;]*\)\s*(?::[^\{=]+)?\s*(?:\{|;)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFieldRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|readonly|declare|abstract)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::[^=;]+)?\s*(?:=[^;]+)?;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoPackageRegex = new(@"^\s*package\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoTypeRegex = new(@"^\s*type\s+([A-Za-z_][A-Za-z0-9_]*)\s+(struct|interface|map|chan|func|\[|[A-Za-z_*])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoFuncRegex = new(@"^\s*func\s*(\((?<receiver>[^)]*)\)\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex PhpTypeRegex = new(@"\b(class|interface|trait|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex PhpFunctionRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|final|abstract)\s+)*function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpFieldRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|readonly)\s+)+(?:[A-Za-z_\\][A-Za-z0-9_\\|?]*\s+)?\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]+)?;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ControlKeywords = new(StringComparer.Ordinal)
    {
        "if",
        "for",
        "foreach",
        "while",
        "switch",
        "catch",
        "using",
        "lock",
        "return"
    };

    public static IReadOnlyList<SymbolRecord> Parse(FileRecord file, string source)
    {
        return file.Language switch
        {
            "Python" => ParsePython(file, source),
            "Go" => ParseGo(file, source),
            "TypeScript" => ParseTypeScript(file, source),
            "PHP" => ParsePhp(file, source),
            "Java" => ParseJavaOrCSharp(file, source),
            "C#" => ParseJavaOrCSharp(file, source),
            _ => Array.Empty<SymbolRecord>()
        };
    }

    private static IReadOnlyList<SymbolRecord> ParseBraceLanguage(
        FileRecord file,
        string source,
        string rootQualifier,
        Regex typeRegex,
        Regex methodRegex,
        string constructorName,
        Regex? topLevelFunctionRegex = null,
        Regex? alternateTopLevelFunctionRegex = null,
        Regex? fieldRegex = null,
        string? rootParentId = null,
        Regex? typeAliasRegex = null)
    {
        var lines = SplitLines(source);
        var symbols = new List<SymbolRecord>();
        var typeScopes = new Stack<BraceScope>();
        var braceDepth = 0;
        SymbolRecord? pendingTypeScope = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            while (typeScopes.Count > 0 && braceDepth < typeScopes.Peek().BodyDepth)
            {
                typeScopes.Pop();
            }

            var typeMatch = typeRegex.Match(line);
            SymbolRecord? declaredType = null;

            if (typeMatch.Success)
            {
                var typeKindToken = typeMatch.Groups[1].Value;
                var typeName = typeMatch.Groups[2].Value;
                var parentSymbol = typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null;
                var qualifiedName = parentSymbol is null
                    ? BuildQualifiedName(rootQualifier, typeName)
                    : BuildQualifiedName(parentSymbol.QualifiedName, typeName);
                declaredType = CreateSymbol(file, typeName, qualifiedName, MapTypeKind(typeKindToken), parentSymbol?.Id ?? rootParentId, lineNumber, line);
                symbols.Add(declaredType);
            }
            else
            {
                if (typeAliasRegex is not null)
                {
                    var aliasMatch = typeAliasRegex.Match(line);
                    if (aliasMatch.Success)
                    {
                        var aliasName = aliasMatch.Groups[2].Value;
                        var aliasQualifiedName = BuildQualifiedName(rootQualifier, aliasName);
                        symbols.Add(CreateSymbol(file, aliasName, aliasQualifiedName, SymbolKinds.TypeAlias, rootParentId, lineNumber, line));
                        continue;
                    }
                }

                var functionMatch = (typeScopes.Count == 0 ? topLevelFunctionRegex : methodRegex)?.Match(line);
                functionMatch = functionMatch is { Success: true }
                    ? functionMatch
                    : (typeScopes.Count == 0 ? alternateTopLevelFunctionRegex : null)?.Match(line);
                if (functionMatch is { Success: true })
                {
                    var methodName = functionMatch.Groups["name"].Value;

                    if (!ControlKeywords.Contains(methodName))
                    {
                        var parentSymbol = typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null;
                        var kind = parentSymbol is not null &&
                                   (string.Equals(methodName, parentSymbol.Name, StringComparison.Ordinal) ||
                                    string.Equals(methodName, constructorName, StringComparison.Ordinal))
                            ? SymbolKinds.Constructor
                            : SymbolKinds.Method;
                        var qualifiedName = parentSymbol is null
                            ? BuildQualifiedName(rootQualifier, methodName)
                            : BuildQualifiedName(parentSymbol.QualifiedName, methodName);
                        symbols.Add(CreateSymbol(file, methodName, qualifiedName, kind, parentSymbol?.Id ?? rootParentId, lineNumber, line, ExtractAccessibility(line), IsStatic(line), IsAbstract(line), false, IsOverride(line)));
                    }
                }
                else if (fieldRegex is not null && typeScopes.Count > 0 && IsTypeSymbol(typeScopes.Peek().Symbol))
                {
                    var fieldMatch = fieldRegex.Match(line);
                    if (fieldMatch.Success)
                    {
                        var fieldName = fieldMatch.Groups["name"].Value;
                        var parentSymbol = typeScopes.Peek().Symbol;
                        var qualifiedName = BuildQualifiedName(parentSymbol.QualifiedName, fieldName);
                        var memberKind = line.Contains("get ", StringComparison.Ordinal) || line.Contains("set ", StringComparison.Ordinal)
                            ? SymbolKinds.Property
                            : SymbolKinds.Field;
                        symbols.Add(CreateSymbol(file, fieldName, qualifiedName, memberKind, parentSymbol.Id, lineNumber, line, ExtractAccessibility(line), IsStatic(line)));
                    }
                }
            }

            var openBraceCount = CountOccurrences(line, '{');
            var closeBraceCount = CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (declaredType is not null && openBraceCount > closeBraceCount)
            {
                typeScopes.Push(new BraceScope(braceDepth, declaredType));
            }
            else if (declaredType is not null)
            {
                pendingTypeScope = declaredType;
            }
            else if (pendingTypeScope is not null && openBraceCount > closeBraceCount)
            {
                typeScopes.Push(new BraceScope(braceDepth, pendingTypeScope));
                pendingTypeScope = null;
            }

            if (pendingTypeScope is not null && !string.IsNullOrWhiteSpace(line) && openBraceCount == 0 && closeBraceCount == 0)
            {
                continue;
            }

            while (typeScopes.Count > 0 && braceDepth < typeScopes.Peek().BodyDepth)
            {
                typeScopes.Pop();
            }
        }

        return symbols;
    }

    private static SymbolRecord CreateContainerSymbol(FileRecord file, string name, string qualifiedName, string kind, int lineNumber)
    {
        return CreateSymbol(file, name, qualifiedName, kind, null, lineNumber, name);
    }

    private static SymbolRecord CreateSymbol(
        FileRecord file,
        string name,
        string qualifiedName,
        string kind,
        string? parentId,
        int lineNumber,
        string line,
        string accessibility = "public",
        bool isStatic = false,
        bool isAbstract = false,
        bool isVirtual = false,
        bool isOverride = false)
    {
        var startColumn = Math.Max(1, line.IndexOf(name, StringComparison.Ordinal) + 1);
        var endColumn = Math.Max(startColumn, startColumn + name.Length - 1);
        var stableId = $"text:{file.Language}:{file.Path}:{kind}:{qualifiedName}";
        return new SymbolRecord(
            DeterministicId.CreateSymbolId(stableId),
            name,
            qualifiedName,
            kind,
            file.Id,
            new TextRangeRecord(lineNumber, startColumn, lineNumber, endColumn),
            kind == SymbolKinds.Constructor ? $"{qualifiedName}()" : $"{kind} {qualifiedName}",
            $"{kind} {name} in {file.Language} source.",
            parentId,
            accessibility,
            isStatic,
            isAbstract,
            isVirtual,
            isOverride);
    }

    internal static string[] SplitLines(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    internal static string BuildLogicalModuleQualifier(string filePath)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        var withoutExtension = Path.ChangeExtension(normalizedPath, null)?.Replace('\\', '/') ?? normalizedPath;
        var segments = withoutExtension.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (segments.Count > 0 && IsCommonSourceRootSegment(segments[0]))
        {
            segments.RemoveAt(0);
        }

        if (segments.Count > 0 && string.Equals(segments[^1], "__init__", StringComparison.Ordinal))
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return segments.Count == 0 ? Path.GetFileNameWithoutExtension(filePath) : string.Join('.', segments);
    }

    private static string BuildQualifiedName(string? prefix, string name)
    {
        return string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}.{name}";
    }

    internal static string GetQualifierPrefix(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot <= 0 ? qualifiedName : qualifiedName[..lastDot];
    }

    internal static string GetLanguageFromSymbolId(string symbolId)
    {
        var parts = symbolId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : string.Empty;
    }

    internal static string GetJavaPackageName(FileRecord file, string source)
    {
        return PackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
    }

    internal static string GetGoPackageName(FileRecord file, string source)
    {
        return GoPackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
    }

    internal static string GetPhpNamespaceName(FileRecord file, string source)
    {
        return PhpNamespaceRegex.Match(source) is { Success: true } namespaceMatch
            ? namespaceMatch.Groups[1].Value.Replace('\\', '.')
            : BuildLogicalModuleQualifier(file.Path);
    }

    private static bool IsCommonSourceRootSegment(string segment)
    {
        return string.Equals(segment, "src", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "lib", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "app", StringComparison.OrdinalIgnoreCase);
    }

    internal static int CountOccurrences(string line, char value)
    {
        var count = 0;

        foreach (var character in line)
        {
            if (character == value)
            {
                count++;
            }
        }

        return count;
    }

    private static string MapTypeKind(string value)
    {
        return value switch
        {
            "class" => SymbolKinds.Class,
            "interface" => SymbolKinds.Interface,
            "enum" => SymbolKinds.Enum,
            "record" => SymbolKinds.Record,
            "struct" => SymbolKinds.Struct,
            "trait" => SymbolKinds.Class,
            _ => SymbolKinds.Class
        };
    }

    internal static bool IsTypeSymbol(SymbolRecord symbol)
    {
        return symbol.Kind is SymbolKinds.Class or SymbolKinds.Record or SymbolKinds.Interface or SymbolKinds.Struct or SymbolKinds.Enum;
    }

    private static int GetLineNumber(string source, int absoluteIndex)
    {
        var line = 1;
        for (var index = 0; index < absoluteIndex && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string ExtractAccessibility(string line)
    {
        if (line.Contains("private", StringComparison.Ordinal))
        {
            return "private";
        }

        if (line.Contains("protected", StringComparison.Ordinal))
        {
            return "protected";
        }

        if (line.Contains("internal", StringComparison.Ordinal))
        {
            return "internal";
        }

        return "public";
    }

    private static bool IsStatic(string line) => line.Contains("static", StringComparison.Ordinal);

    private static bool IsAbstract(string line) => line.Contains("abstract", StringComparison.Ordinal);

    private static bool IsOverride(string line) => line.Contains("override", StringComparison.Ordinal);

    internal static int GetIndentWidth(string line)
    {
        var indent = 0;

        foreach (var character in line)
        {
            if (character == ' ')
            {
                indent++;
                continue;
            }

            if (character == '\t')
            {
                indent += 4;
                continue;
            }

            break;
        }

        return indent;
    }

    private static string? ExtractGoReceiverType(string receiver)
    {
        if (string.IsNullOrWhiteSpace(receiver))
        {
            return null;
        }

        var parts = receiver.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var typeToken = parts.LastOrDefault();
        return typeToken?.TrimStart('*');
    }

    private sealed record BraceScope(int BodyDepth, SymbolRecord Symbol);

    private sealed record IndentScope(int Indent, SymbolRecord Symbol);
}