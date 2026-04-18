using CodeIndex.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIndex.Roslyn;

public sealed class WorkspaceEdgeIndexBuilder
{
    public async Task<IReadOnlyList<EdgeRecord>> BuildAsync(string inputPath, bool includeGenerated = false, CancellationToken cancellationToken = default)
    {
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);
        var sourceRoot = WorkspaceLoader.GetSourceRoot(loadedWorkspace.InputPath);
        var knownSymbolIds = await CollectIndexableSymbolIdsAsync(loadedWorkspace, sourceRoot, includeGenerated, cancellationToken);

        return await BuildAsync(loadedWorkspace, sourceRoot, indexedFilePaths: null, knownSymbolIds, includeGenerated, cancellationToken);
    }

    public async Task<IReadOnlyList<EdgeRecord>> BuildAsync(
        string inputPath,
        IReadOnlyCollection<string> indexedFilePaths,
        IReadOnlySet<string> knownSymbolIds,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);
        var sourceRoot = WorkspaceLoader.GetSourceRoot(loadedWorkspace.InputPath);
        return await BuildAsync(loadedWorkspace, sourceRoot, indexedFilePaths, knownSymbolIds, includeGenerated, cancellationToken);
    }

    private static async Task<IReadOnlyList<EdgeRecord>> BuildAsync(
        LoadedWorkspace loadedWorkspace,
        string sourceRoot,
        IReadOnlyCollection<string>? indexedFilePaths,
        IReadOnlySet<string> knownSymbolIds,
        bool includeGenerated,
        CancellationToken cancellationToken)
    {
        var indexedPaths = indexedFilePaths is null
            ? null
            : new HashSet<string>(indexedFilePaths, StringComparer.Ordinal);
        var indexedSymbolsById = await CollectIndexedSymbolsAsync(loadedWorkspace, knownSymbolIds, includeGenerated, cancellationToken);
        var testTargetIdsByName = BuildTestTargetIdsByName(indexedSymbolsById.Values);
        var edges = new HashSet<EdgeRecord>();

        foreach (var project in loadedWorkspace.Projects)
        {
            var projectDirectory = project.FilePath is null ? null : Path.GetDirectoryName(project.FilePath);

            foreach (var document in project.Documents)
            {
                if (document.FilePath is null || !CSharpSourceDocumentFilter.IsRelevantSourceDocument(document.FilePath, projectDirectory, includeGenerated))
                {
                    continue;
                }

                var normalizedPath = PathNormalization.NormalizeRelativePath(sourceRoot, document.FilePath);

                if (indexedPaths is not null && !indexedPaths.Contains(normalizedPath))
                {
                    continue;
                }

                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

                if (syntaxRoot is null || semanticModel is null)
                {
                    continue;
                }

                foreach (var declaration in EnumerateDeclarations(syntaxRoot))
                {
                    var symbol = GetDeclaredSymbol(semanticModel, declaration, cancellationToken);

                    if (symbol is null || symbol.IsImplicitlyDeclared || !IsIndexableSymbol(symbol))
                    {
                        continue;
                    }

                    var fromId = DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol));

                    if (!knownSymbolIds.Contains(fromId))
                    {
                        continue;
                    }

                    AddContainsEdge(edges, fromId, symbol, knownSymbolIds);
                    AddInheritanceEdges(edges, fromId, symbol, knownSymbolIds);
                    AddImplementsEdges(edges, fromId, symbol, knownSymbolIds);
                    AddOverrideEdges(edges, fromId, symbol, knownSymbolIds);
                }

                AddCallEdges(edges, syntaxRoot, semanticModel, knownSymbolIds, cancellationToken);
                AddTestLinkEdges(edges, syntaxRoot, semanticModel, knownSymbolIds, testTargetIdsByName, cancellationToken);
            }
        }

        return edges
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<Dictionary<string, ISymbol>> CollectIndexedSymbolsAsync(
        LoadedWorkspace loadedWorkspace,
        IReadOnlySet<string> knownSymbolIds,
        bool includeGenerated,
        CancellationToken cancellationToken)
    {
        var indexedSymbols = new Dictionary<string, ISymbol>(StringComparer.Ordinal);

        foreach (var project in loadedWorkspace.Projects)
        {
            var projectDirectory = project.FilePath is null ? null : Path.GetDirectoryName(project.FilePath);

            foreach (var document in project.Documents)
            {
                if (document.FilePath is null || !CSharpSourceDocumentFilter.IsRelevantSourceDocument(document.FilePath, projectDirectory, includeGenerated))
                {
                    continue;
                }

                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

                if (syntaxRoot is null || semanticModel is null)
                {
                    continue;
                }

                foreach (var declaration in EnumerateDeclarations(syntaxRoot))
                {
                    var symbol = GetDeclaredSymbol(semanticModel, declaration, cancellationToken);

                    if (symbol is null || symbol.IsImplicitlyDeclared || !IsIndexableSymbol(symbol))
                    {
                        continue;
                    }

                    var symbolId = DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol));

                    if (knownSymbolIds.Contains(symbolId))
                    {
                        indexedSymbols.TryAdd(symbolId, symbol);
                    }
                }
            }
        }

        return indexedSymbols;
    }

    private static Dictionary<string, List<string>> BuildTestTargetIdsByName(IEnumerable<ISymbol> symbols)
    {
        var results = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            if (IsTestSymbol(symbol) || !IsTestLinkTargetSymbol(symbol) || string.IsNullOrWhiteSpace(symbol.Name))
            {
                continue;
            }

            var symbolId = DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol));

            if (!results.TryGetValue(symbol.Name, out var targetIds))
            {
                targetIds = new List<string>();
                results[symbol.Name] = targetIds;
            }

            if (!targetIds.Contains(symbolId, StringComparer.Ordinal))
            {
                targetIds.Add(symbolId);
            }
        }

        return results;
    }

    private static async Task<HashSet<string>> CollectIndexableSymbolIdsAsync(
        LoadedWorkspace loadedWorkspace,
        string sourceRoot,
        bool includeGenerated,
        CancellationToken cancellationToken)
    {
        var symbolIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in loadedWorkspace.Projects)
        {
            var projectDirectory = project.FilePath is null ? null : Path.GetDirectoryName(project.FilePath);

            foreach (var document in project.Documents)
            {
                if (document.FilePath is null || !CSharpSourceDocumentFilter.IsRelevantSourceDocument(document.FilePath, projectDirectory, includeGenerated))
                {
                    continue;
                }

                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

                if (syntaxRoot is null || semanticModel is null)
                {
                    continue;
                }

                foreach (var declaration in EnumerateDeclarations(syntaxRoot))
                {
                    var symbol = GetDeclaredSymbol(semanticModel, declaration, cancellationToken);

                    if (symbol is null || symbol.IsImplicitlyDeclared || !IsIndexableSymbol(symbol))
                    {
                        continue;
                    }

                    symbolIds.Add(DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol)));
                }
            }
        }

        return symbolIds;
    }

    private static void AddContainsEdge(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlySet<string> knownSymbolIds)
    {
        if (symbol is INamespaceSymbol)
        {
            return;
        }

        var parentId = GetIndexedSymbolId(symbol.ContainingSymbol, knownSymbolIds);

        if (parentId is not null)
        {
            edges.Add(new EdgeRecord(EdgeTypes.Contains, parentId, fromId));
        }
    }

    private static void AddInheritanceEdges(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlySet<string> knownSymbolIds)
    {
        if (symbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        if (namedType.BaseType is null)
        {
            return;
        }

        var toId = GetIndexedSymbolId(namedType.BaseType, knownSymbolIds);

        if (toId is not null)
        {
            edges.Add(new EdgeRecord(EdgeTypes.Inherits, fromId, toId));
        }
    }

    private static void AddImplementsEdges(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlySet<string> knownSymbolIds)
    {
        if (symbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        foreach (var interfaceSymbol in namedType.Interfaces)
        {
            var toId = GetIndexedSymbolId(interfaceSymbol, knownSymbolIds);

            if (toId is not null)
            {
                edges.Add(new EdgeRecord(EdgeTypes.Implements, fromId, toId));
            }
        }
    }

    private static void AddOverrideEdges(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlySet<string> knownSymbolIds)
    {
        ISymbol? overriddenSymbol = symbol switch
        {
            IMethodSymbol methodSymbol when methodSymbol.OverriddenMethod is not null => methodSymbol.OverriddenMethod,
            IPropertySymbol propertySymbol when propertySymbol.OverriddenProperty is not null => propertySymbol.OverriddenProperty,
            IEventSymbol eventSymbol when eventSymbol.OverriddenEvent is not null => eventSymbol.OverriddenEvent,
            _ => null
        };

        var toId = overriddenSymbol is null ? null : GetIndexedSymbolId(overriddenSymbol, knownSymbolIds);

        if (toId is not null)
        {
            edges.Add(new EdgeRecord(EdgeTypes.Overrides, fromId, toId));
        }
    }

    private static void AddCallEdges(HashSet<EdgeRecord> edges, SyntaxNode syntaxRoot, SemanticModel semanticModel, IReadOnlySet<string> knownSymbolIds, CancellationToken cancellationToken)
    {
        foreach (var invocation in syntaxRoot.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            AddCallEdge(edges, semanticModel, invocation, invocation.SpanStart, knownSymbolIds, cancellationToken);
        }

        foreach (var objectCreation in syntaxRoot.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            AddCallEdge(edges, semanticModel, objectCreation, objectCreation.SpanStart, knownSymbolIds, cancellationToken);
        }

        foreach (var objectCreation in syntaxRoot.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            AddCallEdge(edges, semanticModel, objectCreation, objectCreation.SpanStart, knownSymbolIds, cancellationToken);
        }

        foreach (var constructorInitializer in syntaxRoot.DescendantNodes().OfType<ConstructorInitializerSyntax>())
        {
            AddCallEdge(edges, semanticModel, constructorInitializer, constructorInitializer.SpanStart, knownSymbolIds, cancellationToken);
        }
    }

    private static void AddCallEdge(HashSet<EdgeRecord> edges, SemanticModel semanticModel, SyntaxNode node, int position, IReadOnlySet<string> knownSymbolIds, CancellationToken cancellationToken)
    {
        var callerId = GetContainingIndexedCallableSymbolId(semanticModel, position, knownSymbolIds);

        if (callerId is null)
        {
            return;
        }

        var calleeId = GetIndexedCallableTargetId(semanticModel.GetSymbolInfo(node, cancellationToken), knownSymbolIds);

        if (calleeId is not null)
        {
            edges.Add(new EdgeRecord(EdgeTypes.Calls, callerId, calleeId));
        }
    }

    private static void AddTestLinkEdges(
        HashSet<EdgeRecord> edges,
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        IReadOnlySet<string> knownSymbolIds,
        IReadOnlyDictionary<string, List<string>> testTargetIdsByName,
        CancellationToken cancellationToken)
    {
        foreach (var methodDeclaration in syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) as IMethodSymbol;

            if (methodSymbol is null || methodSymbol.IsImplicitlyDeclared || !IsTestMethod(methodSymbol))
            {
                continue;
            }

            var fromId = GetIndexedSymbolId(methodSymbol, knownSymbolIds);

            if (fromId is null)
            {
                continue;
            }

            foreach (var candidateName in GetTestCandidateNames(methodSymbol))
            {
                if (!testTargetIdsByName.TryGetValue(candidateName, out var targetIds))
                {
                    continue;
                }

                foreach (var targetId in targetIds)
                {
                    if (!string.Equals(fromId, targetId, StringComparison.Ordinal))
                    {
                        edges.Add(new EdgeRecord(EdgeTypes.Tests, fromId, targetId));
                    }
                }
            }
        }
    }

    private static string? GetContainingIndexedCallableSymbolId(SemanticModel semanticModel, int position, IReadOnlySet<string> knownSymbolIds)
    {
        var symbol = semanticModel.GetEnclosingSymbol(position);

        while (symbol is not null)
        {
            if (IsCallableSymbol(symbol))
            {
                var symbolId = DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol));

                if (knownSymbolIds.Contains(symbolId))
                {
                    return symbolId;
                }
            }

            symbol = symbol.ContainingSymbol;
        }

        return null;
    }

    private static string? GetIndexedCallableTargetId(SymbolInfo symbolInfo, IReadOnlySet<string> knownSymbolIds)
    {
        var targetSymbol = NormalizeCallableTarget(symbolInfo.Symbol)
            ?? symbolInfo.CandidateSymbols.Select(NormalizeCallableTarget).FirstOrDefault(symbol => symbol is not null);

        return targetSymbol is null ? null : GetIndexedSymbolId(targetSymbol, knownSymbolIds);
    }

    private static ISymbol? NormalizeCallableTarget(ISymbol? symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Ordinary } methodSymbol => methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition,
            IMethodSymbol { MethodKind: MethodKind.Constructor } methodSymbol => methodSymbol.OriginalDefinition,
            _ => null
        };
    }

    private static string? GetIndexedSymbolId(ISymbol? symbol, IReadOnlySet<string> knownSymbolIds)
    {
        if (symbol is null)
        {
            return null;
        }

        if (symbol is INamespaceSymbol namespaceSymbol && namespaceSymbol.IsGlobalNamespace)
        {
            return null;
        }

        var symbolId = DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol));
        return knownSymbolIds.Contains(symbolId) ? symbolId : null;
    }

    private static bool IsIndexableSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            INamespaceSymbol namespaceSymbol when !namespaceSymbol.IsGlobalNamespace => true,
            INamedTypeSymbol namedType when namedType.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Struct or TypeKind.Enum or TypeKind.Delegate => true,
            INamedTypeSymbol namedType when namedType.IsRecord => true,
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Ordinary } => true,
            IPropertySymbol => true,
            IFieldSymbol => true,
            IEventSymbol => true,
            ILocalSymbol => true,
            _ => false
        };
    }

    private static bool IsCallableSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Ordinary };
    }

    private static bool IsTestLinkTargetSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            INamespaceSymbol => false,
            ILocalSymbol => false,
            _ => true
        };
    }

    private static bool IsTestMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes().Any(static attribute => IsTestAttributeName(attribute.AttributeClass?.Name))
            || IsTestNamedSymbol(methodSymbol.ContainingType)
            || HasTestLikeFilePath(methodSymbol);
    }

    private static bool IsTestSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol methodSymbol && IsTestMethod(methodSymbol)
            || IsTestNamedSymbol(symbol)
            || HasTestLikeFilePath(symbol);
    }

    private static bool IsTestNamedSymbol(ISymbol? symbol)
    {
        while (symbol is not null)
        {
            if (!string.IsNullOrWhiteSpace(symbol.Name) &&
                (symbol.Name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
                 symbol.Name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            symbol = symbol.ContainingSymbol;
        }

        return false;
    }

    private static bool HasTestLikeFilePath(ISymbol symbol)
    {
        var filePath = symbol.Locations.FirstOrDefault(static location => location.IsInSource)?.SourceTree?.FilePath;

        return !string.IsNullOrWhiteSpace(filePath) &&
               (filePath.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
                filePath.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase) ||
                filePath.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase) ||
                filePath.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTestAttributeName(string? attributeName)
    {
        return attributeName is "FactAttribute" or
            "TheoryAttribute" or
            "TestAttribute" or
            "TestMethodAttribute" or
            "TestCaseAttribute";
    }

    private static IEnumerable<string> GetTestCandidateNames(IMethodSymbol methodSymbol)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidateNames(names, methodSymbol.Name);

        if (methodSymbol.ContainingType is not null)
        {
            AddCandidateNames(names, TrimTestSuffix(methodSymbol.ContainingType.Name));
        }

        return names;
    }

    private static void AddCandidateNames(HashSet<string> names, string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return;
        }

        names.Add(rawName);

        foreach (var segment in rawName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsMeaningfulTestToken(segment))
            {
                names.Add(segment);
            }

            foreach (var token in SplitPascalCase(segment))
            {
                if (IsMeaningfulTestToken(token))
                {
                    names.Add(token);
                }
            }
        }
    }

    private static string TrimTestSuffix(string name)
    {
        if (name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^5];
        }

        if (name.EndsWith("Test", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^4];
        }

        return name;
    }

    private static bool IsMeaningfulTestToken(string token)
    {
        return token.Length > 1 && token is not (
            "Returns" or "Return" or "Throws" or "Throw" or "Given" or "When" or "Then" or
            "Should" or "Can" or "Does" or "With" or "Without" or "And" or "Or" or "Uses" or "Using");
    }

    private static IEnumerable<string> SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var start = 0;

        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
            {
                yield return value[start..index];
                start = index;
            }
        }

        yield return value[start..];
    }

    private static IEnumerable<SyntaxNode> EnumerateDeclarations(SyntaxNode root)
    {
        foreach (var declaration in root.DescendantNodes())
        {
            switch (declaration)
            {
                case BaseNamespaceDeclarationSyntax:
                case BaseTypeDeclarationSyntax:
                case DelegateDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case MethodDeclarationSyntax:
                case PropertyDeclarationSyntax:
                case EventDeclarationSyntax:
                case VariableDeclaratorSyntax:
                    yield return declaration;
                    break;
            }
        }
    }

    private static ISymbol? GetDeclaredSymbol(SemanticModel semanticModel, SyntaxNode declaration, CancellationToken cancellationToken)
    {
        return declaration switch
        {
            BaseNamespaceDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            BaseTypeDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            DelegateDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            ConstructorDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            MethodDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            PropertyDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            EventDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            VariableDeclaratorSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            _ => null
        };
    }
}