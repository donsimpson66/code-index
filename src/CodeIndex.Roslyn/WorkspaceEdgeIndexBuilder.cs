using CodeIndex.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIndex.Roslyn;

public sealed class WorkspaceEdgeIndexBuilder
{
    public async Task<IReadOnlyList<EdgeRecord>> BuildAsync(string inputPath, bool includeGenerated = false, CancellationToken cancellationToken = default)
    {
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);
        var indexableSymbols = new Dictionary<string, ISymbol>(StringComparer.Ordinal);

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

                    indexableSymbols.TryAdd(DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol)), symbol);
                }
            }
        }

        var edges = new HashSet<EdgeRecord>();

        foreach (var pair in indexableSymbols)
        {
            var fromId = pair.Key;
            var symbol = pair.Value;

            AddContainsEdge(edges, fromId, symbol, indexableSymbols);
            AddInheritanceEdges(edges, fromId, symbol, indexableSymbols);
            AddImplementsEdges(edges, fromId, symbol, indexableSymbols);
            AddOverrideEdges(edges, fromId, symbol, indexableSymbols);
        }

        return edges
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddContainsEdge(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlyDictionary<string, ISymbol> indexableSymbols)
    {
        if (symbol is INamespaceSymbol)
        {
            return;
        }

        var parentId = GetIndexedSymbolId(symbol.ContainingSymbol, indexableSymbols);

        if (parentId is not null)
        {
            edges.Add(new EdgeRecord(EdgeTypes.Contains, parentId, fromId));
        }
    }

    private static void AddInheritanceEdges(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlyDictionary<string, ISymbol> indexableSymbols)
    {
        if (symbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        if (namedType.BaseType is null)
        {
            return;
        }

        var toId = GetIndexedSymbolId(namedType.BaseType, indexableSymbols);

        if (toId is not null)
        {
            edges.Add(new EdgeRecord(EdgeTypes.Inherits, fromId, toId));
        }
    }

    private static void AddImplementsEdges(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlyDictionary<string, ISymbol> indexableSymbols)
    {
        if (symbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        foreach (var interfaceSymbol in namedType.Interfaces)
        {
            var toId = GetIndexedSymbolId(interfaceSymbol, indexableSymbols);

            if (toId is not null)
            {
                edges.Add(new EdgeRecord(EdgeTypes.Implements, fromId, toId));
            }
        }
    }

    private static void AddOverrideEdges(HashSet<EdgeRecord> edges, string fromId, ISymbol symbol, IReadOnlyDictionary<string, ISymbol> indexableSymbols)
    {
        ISymbol? overriddenSymbol = symbol switch
        {
            IMethodSymbol methodSymbol when methodSymbol.OverriddenMethod is not null => methodSymbol.OverriddenMethod,
            IPropertySymbol propertySymbol when propertySymbol.OverriddenProperty is not null => propertySymbol.OverriddenProperty,
            IEventSymbol eventSymbol when eventSymbol.OverriddenEvent is not null => eventSymbol.OverriddenEvent,
            _ => null
        };

        var toId = overriddenSymbol is null ? null : GetIndexedSymbolId(overriddenSymbol, indexableSymbols);

        if (toId is not null)
        {
            edges.Add(new EdgeRecord(EdgeTypes.Overrides, fromId, toId));
        }
    }

    private static string? GetIndexedSymbolId(ISymbol? symbol, IReadOnlyDictionary<string, ISymbol> indexableSymbols)
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
        return indexableSymbols.ContainsKey(symbolId) ? symbolId : null;
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
            _ => false
        };
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