using CodeIndex.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace CodeIndex.Roslyn;

public sealed class WorkspaceReferenceIndexBuilder
{
    public async Task<IReadOnlyList<ReferenceRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        return await BuildAsync(inputPath, files, symbols, indexedFilePaths: null, includeGenerated, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyCollection<string>? indexedFilePaths,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);
        var sourceRoot = WorkspaceLoader.GetSourceRoot(loadedWorkspace.InputPath);
        var fileIdByPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var symbolIds = symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var indexedPaths = indexedFilePaths is null ? null : new HashSet<string>(indexedFilePaths, StringComparer.Ordinal);
        var indexedSymbols = await CollectIndexedSymbolsAsync(loadedWorkspace, symbolIds, includeGenerated, cancellationToken);

        var records = new HashSet<ReferenceRecord>();
        var textByDocumentId = new Dictionary<DocumentId, SourceText>();
        var semanticModelByDocumentId = new Dictionary<DocumentId, SemanticModel?>();

        foreach (var indexedSymbol in indexedSymbols.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var referencedSymbols = await SymbolFinder.FindReferencesAsync(indexedSymbol.Value, loadedWorkspace.Workspace.CurrentSolution, cancellationToken);

            foreach (var referencedSymbol in referencedSymbols)
            {
                foreach (var location in referencedSymbol.Locations)
                {
                    if (!location.Location.IsInSource)
                    {
                        continue;
                    }

                    var document = loadedWorkspace.Workspace.CurrentSolution.GetDocument(location.Document.Id);

                    if (document?.FilePath is null)
                    {
                        continue;
                    }

                    var projectDirectory = document.Project.FilePath is null ? null : Path.GetDirectoryName(document.Project.FilePath);

                    if (!CSharpSourceDocumentFilter.IsRelevantSourceDocument(document.FilePath, projectDirectory, includeGenerated))
                    {
                        continue;
                    }

                    var normalizedPath = PathNormalization.NormalizeRelativePath(sourceRoot, document.FilePath);

                    if (indexedPaths is not null && !indexedPaths.Contains(normalizedPath))
                    {
                        continue;
                    }

                    if (!fileIdByPath.TryGetValue(normalizedPath, out var file))
                    {
                        continue;
                    }

                    if (!textByDocumentId.TryGetValue(document.Id, out var sourceText))
                    {
                        sourceText = await document.GetTextAsync(cancellationToken);
                        textByDocumentId[document.Id] = sourceText;
                    }

                    if (!semanticModelByDocumentId.TryGetValue(document.Id, out var semanticModel))
                    {
                        semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                        semanticModelByDocumentId[document.Id] = semanticModel;
                    }

                    var lineSpan = location.Location.GetLineSpan();
                    var startLine = lineSpan.StartLinePosition.Line;
                    var sourceLine = startLine >= 0 && startLine < sourceText.Lines.Count
                        ? sourceText.Lines[startLine].ToString().TrimEnd('\r', '\n')
                        : string.Empty;

                    records.Add(new ReferenceRecord(
                        indexedSymbol.Key,
                        GetContainingIndexedSymbolId(semanticModel, location.Location.SourceSpan.Start, symbolIds),
                        file.Id,
                        new TextRangeRecord(
                            lineSpan.StartLinePosition.Line + 1,
                            lineSpan.StartLinePosition.Character + 1,
                            lineSpan.EndLinePosition.Line + 1,
                            lineSpan.EndLinePosition.Character + 1),
                        sourceLine));
                }
            }
        }

        return records
            .OrderBy(reference => reference.TargetSymbolId, StringComparer.Ordinal)
            .ThenBy(reference => reference.FileId, StringComparer.Ordinal)
            .ThenBy(reference => reference.Range.StartLine)
            .ThenBy(reference => reference.Range.StartColumn)
            .ThenBy(reference => reference.SourceSymbolId, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<Dictionary<string, ISymbol>> CollectIndexedSymbolsAsync(
        LoadedWorkspace loadedWorkspace,
        IReadOnlySet<string> symbolIds,
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

                    if (symbolIds.Contains(symbolId))
                    {
                        indexedSymbols.TryAdd(symbolId, symbol);
                    }
                }
            }
        }

        return indexedSymbols;
    }

    private static IEnumerable<SyntaxNode> EnumerateDeclarations(SyntaxNode root)
    {
        foreach (var declaration in root.DescendantNodes())
        {
            switch (declaration)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax:
                    yield return declaration;
                    break;
            }
        }
    }

    private static ISymbol? GetDeclaredSymbol(SemanticModel semanticModel, SyntaxNode declaration, CancellationToken cancellationToken)
    {
        return declaration switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax node => semanticModel.GetDeclaredSymbol(node, cancellationToken),
            _ => null
        };
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

    private static string? GetContainingIndexedSymbolId(SemanticModel? semanticModel, int position, IReadOnlySet<string> symbolIds)
    {
        var symbol = semanticModel?.GetEnclosingSymbol(position);

        while (symbol is not null)
        {
            var symbolId = DeterministicId.CreateSymbolId(SymbolIdentity.CreateStableId(symbol));

            if (symbolIds.Contains(symbolId))
            {
                return symbolId;
            }

            symbol = symbol.ContainingSymbol;
        }

        return null;
    }
}