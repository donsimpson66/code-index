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
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);
        var sourceRoot = WorkspaceLoader.GetSourceRoot(loadedWorkspace.InputPath);
        var fileIdByPath = files.ToDictionary(
            record => Path.GetFullPath(Path.Combine(sourceRoot, record.Path)),
            record => record.Id,
            StringComparer.OrdinalIgnoreCase);

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

                if (!fileIdByPath.TryGetValue(Path.GetFullPath(document.FilePath), out var fileId))
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

                    if (!TryCreateSymbolRecord(symbol, declaration, fileId, fileIdByPath, out var record))
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
        var sourceLocation = GetCanonicalSourceLocation(symbol, declaration, fileId, fileIdByPath);

        var parentId = GetParentId(symbol, symbol.ContainingSymbol);
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

                var fullPath = Path.GetFullPath(syntaxPath);

                if (!fileIdByPath.TryGetValue(fullPath, out var referenceFileId))
                {
                    return null;
                }

                return new
                {
                    FullPath = fullPath,
                    FileId = referenceFileId,
                    LineSpan = reference.SyntaxTree.GetLineSpan(reference.Span)
                };
            })
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
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
            _ => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
        };
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
            INamespaceSymbol namespaceSymbol when !namespaceSymbol.IsGlobalNamespace => "namespace",
            INamedTypeSymbol namedType when namedType.IsRecord => "record",
            INamedTypeSymbol { TypeKind: TypeKind.Class } => "class",
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => "interface",
            INamedTypeSymbol { TypeKind: TypeKind.Struct } => "struct",
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => "enum",
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => "delegate",
            IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
            IMethodSymbol { MethodKind: MethodKind.Ordinary } => "method",
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            _ => null
        };
    }
}