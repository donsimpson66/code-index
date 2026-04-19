using CodeIndex.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml.Linq;

namespace CodeIndex.Roslyn;

public sealed class WorkspaceSymbolIndexBuilder
{
    private static readonly SymbolDisplayFormat TypeNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat MethodSignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeName |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut |
                          SymbolDisplayParameterOptions.IncludeOptionalBrackets,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat ConstructorSignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeName |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut |
                          SymbolDisplayParameterOptions.IncludeOptionalBrackets,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat PropertySignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeName |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut |
                          SymbolDisplayParameterOptions.IncludeOptionalBrackets,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat FieldSignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public async Task<IReadOnlyList<SymbolRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        return await BuildAsync(inputPath, files, indexedFilePaths: null, includeGenerated, cancellationToken);
    }

    public async Task<IReadOnlyList<SymbolRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyCollection<string>? indexedFilePaths,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);
        var sourceRoot = WorkspaceLoader.GetSourceRoot(loadedWorkspace.InputPath);
        var fileIdByPath = files.ToDictionary(
            record => record.Path,
            record => record.Id,
            StringComparer.Ordinal);
        var indexedPaths = indexedFilePaths is null
            ? null
            : new HashSet<string>(indexedFilePaths, StringComparer.Ordinal);

        var symbolRecords = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

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

                if (!fileIdByPath.TryGetValue(normalizedPath, out var fileId))
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

                    if (symbol is null || symbol.IsImplicitlyDeclared)
                    {
                        continue;
                    }

                    if (!TryCreateSymbolRecord(symbol, declaration, fileId, sourceRoot, fileIdByPath, out var record))
                    {
                        continue;
                    }

                    symbolRecords.TryAdd(record.Id, record);
                }
            }
        }

        return symbolRecords.Values
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ToArray();
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

    private static bool TryCreateSymbolRecord(
        ISymbol symbol,
        SyntaxNode declaration,
        string fileId,
        string sourceRoot,
        IReadOnlyDictionary<string, string> fileIdByPath,
        out SymbolRecord record)
    {
        var symbolKind = GetSymbolKind(symbol);

        if (symbolKind is null)
        {
            record = default!;
            return false;
        }

        var stableId = SymbolIdentity.CreateStableId(symbol);
        var sourceLocation = GetCanonicalSourceLocation(symbol, declaration, fileId, sourceRoot, fileIdByPath);

        var parentId = symbol is ILocalSymbol && declaration.Ancestors().OfType<GlobalStatementSyntax>().Any()
            ? null
            : GetParentId(symbol, symbol.ContainingSymbol);
        var signature = FormatSignature(symbol, symbolKind);
        var qualifiedName = symbol.ToDisplayString(QualifiedNameFormat);

        record = new SymbolRecord(
            DeterministicId.CreateSymbolId(stableId),
            symbol.Name,
            qualifiedName,
            symbolKind,
            sourceLocation.FileId,
            new TextRangeRecord(
                sourceLocation.LineSpan.StartLinePosition.Line + 1,
                sourceLocation.LineSpan.StartLinePosition.Character + 1,
                sourceLocation.LineSpan.EndLinePosition.Line + 1,
                sourceLocation.LineSpan.EndLinePosition.Character + 1),
            signature,
            GetSummary(symbol, symbolKind),
            parentId,
            GetAccessibility(symbol),
            symbol.IsStatic,
            symbol.IsAbstract,
            symbol.IsVirtual,
            symbol.IsOverride);

        return true;
    }

    private static (string FileId, FileLinePositionSpan LineSpan) GetCanonicalSourceLocation(
        ISymbol symbol,
        SyntaxNode declaration,
        string currentFileId,
        string sourceRoot,
        IReadOnlyDictionary<string, string> fileIdByPath)
    {
        var candidates = symbol.DeclaringSyntaxReferences
            .Select(reference =>
            {
                var syntaxPath = reference.SyntaxTree.FilePath;

                if (string.IsNullOrWhiteSpace(syntaxPath))
                {
                    return null;
                }

                var normalizedPath = PathNormalization.NormalizeRelativePath(sourceRoot, syntaxPath);

                if (!fileIdByPath.TryGetValue(normalizedPath, out var referenceFileId))
                {
                    return null;
                }

                return new
                {
                    Path = normalizedPath,
                    FileId = referenceFileId,
                    LineSpan = reference.SyntaxTree.GetLineSpan(reference.Span)
                };
            })
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.LineSpan.StartLinePosition.Line)
            .ThenBy(candidate => candidate.LineSpan.StartLinePosition.Character)
            .FirstOrDefault();

        if (candidates is not null)
        {
            return (candidates.FileId, candidates.LineSpan);
        }

        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? declaration.GetLocation();
        return (currentFileId, location.GetLineSpan());
    }

    private static string FormatSignature(ISymbol symbol, string symbolKind)
    {
        return symbol switch
        {
            INamespaceSymbol => symbol.ToDisplayString(TypeNameFormat),
            INamedTypeSymbol namedType when namedType.TypeKind == TypeKind.Delegate => FormatDelegateSignature(namedType),
            INamedTypeSymbol => $"{symbolKind} {symbol.ToDisplayString(TypeNameFormat)}",
            IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => FormatConstructorSignature(constructor),
            IMethodSymbol => symbol.ToDisplayString(MethodSignatureFormat),
            IPropertySymbol => symbol.ToDisplayString(PropertySignatureFormat),
            IFieldSymbol => symbol.ToDisplayString(FieldSignatureFormat),
            IEventSymbol => symbol.ToDisplayString(FieldSignatureFormat),
            ILocalSymbol localSymbol => FormatLocalSignature(localSymbol),
            _ => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
        };
    }

    private static string FormatLocalSignature(ILocalSymbol localSymbol)
    {
        var containingSymbol = localSymbol.ContainingSymbol is null
            ? string.Empty
            : localSymbol.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                ? FormatConstructorSignature(constructor)
                : localSymbol.ContainingSymbol.ToDisplayString(QualifiedNameFormat);
        return string.IsNullOrWhiteSpace(containingSymbol)
            ? $"{localSymbol.Type.ToDisplayString(TypeNameFormat)} {localSymbol.Name}"
            : $"{localSymbol.Type.ToDisplayString(TypeNameFormat)} {containingSymbol}.{localSymbol.Name}";
    }

    private static string FormatConstructorSignature(IMethodSymbol constructor)
    {
        var containingType = constructor.ContainingType?.ToDisplayString(TypeNameFormat) ?? constructor.ContainingSymbol.ToDisplayString(TypeNameFormat);
        var parameters = string.Join(", ", constructor.Parameters.Select(FormatParameter));
        return $"{containingType}({parameters})";
    }

    private static string FormatDelegateSignature(INamedTypeSymbol delegateSymbol)
    {
        if (delegateSymbol.DelegateInvokeMethod is null)
        {
            return $"delegate {delegateSymbol.ToDisplayString(TypeNameFormat)}";
        }

        var returnType = delegateSymbol.DelegateInvokeMethod.ReturnType.ToDisplayString(TypeNameFormat);
        var parameters = string.Join(", ",
            delegateSymbol.DelegateInvokeMethod.Parameters.Select(FormatParameter));

        return $"delegate {returnType} {delegateSymbol.ToDisplayString(TypeNameFormat)}({parameters})";
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        var prefix = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => parameter.IsParams ? "params " : string.Empty
        };

        return $"{prefix}{parameter.Type.ToDisplayString(TypeNameFormat)} {parameter.Name}";
    }

    private static string? GetParentId(ISymbol symbol, ISymbol? containingSymbol)
    {
        if (containingSymbol is null)
        {
            return null;
        }

        if (containingSymbol.IsImplicitlyDeclared)
        {
            return null;
        }

        if (symbol is ILocalSymbol && !CanIndexAsLocalParent(containingSymbol))
        {
            return null;
        }

        if (symbol is INamespaceSymbol)
        {
            return null;
        }

        if (containingSymbol is INamespaceSymbol namespaceSymbol && namespaceSymbol.IsGlobalNamespace)
        {
            return null;
        }

        var kind = GetSymbolKind(containingSymbol);

        if (kind is null)
        {
            return null;
        }

        var stableId = SymbolIdentity.CreateStableId(containingSymbol);
        return DeterministicId.CreateSymbolId(stableId);
    }

    private static bool CanIndexAsLocalParent(ISymbol containingSymbol)
    {
        return containingSymbol is IMethodSymbol
        {
            MethodKind: MethodKind.Ordinary or MethodKind.Constructor,
            ContainingType.IsImplicitlyDeclared: false
        };
    }

    private static string GetSummary(ISymbol symbol, string symbolKind)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: default);

        if (!string.IsNullOrWhiteSpace(xml))
        {
            try
            {
                var summary = XDocument.Parse(xml)
                    .Descendants("summary")
                    .Select(node => string.Join(" ", node.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    return summary;
                }
            }
            catch
            {
            }
        }

        var displayName = symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } && symbol.ContainingType is not null
            ? symbol.ContainingType.Name
            : symbol.Name;

        return $"{symbolKind} {displayName}.";
    }

    private static string GetAccessibility(ISymbol symbol)
    {
        if (symbol is ILocalSymbol)
        {
            return "local";
        }

        return symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "not applicable"
        };
    }

    private static string? GetSymbolKind(ISymbol symbol)
    {
        return symbol switch
        {
            INamespaceSymbol namespaceSymbol when !namespaceSymbol.IsGlobalNamespace => SymbolKinds.Namespace,
            INamedTypeSymbol namedType when namedType.IsRecord => SymbolKinds.Record,
            INamedTypeSymbol { TypeKind: TypeKind.Class } => SymbolKinds.Class,
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => SymbolKinds.Interface,
            INamedTypeSymbol { TypeKind: TypeKind.Struct } => SymbolKinds.Struct,
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => SymbolKinds.Enum,
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => SymbolKinds.Delegate,
            IMethodSymbol { MethodKind: MethodKind.Constructor } => SymbolKinds.Constructor,
            IMethodSymbol { MethodKind: MethodKind.Ordinary } => SymbolKinds.Method,
            IPropertySymbol => SymbolKinds.Property,
            IFieldSymbol => SymbolKinds.Field,
            IEventSymbol => SymbolKinds.Event,
            ILocalSymbol => SymbolKinds.Local,
            _ => null
        };
    }
}