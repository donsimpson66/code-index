using CodeIndex.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIndex.Roslyn;

internal static class CSharpFileSummaryGenerator
{
    public static async Task<string> CreateSummaryAsync(Document document, string normalizedPath, CancellationToken cancellationToken)
    {
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);

        if (syntaxRoot is null)
        {
            return FileSummaryGenerator.CreateSummary(normalizedPath, document.Project.Name);
        }

        var namespaceName = syntaxRoot.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(@namespace => @namespace.Name.ToString())
            .FirstOrDefault();

        var declarations = syntaxRoot.DescendantNodes()
            .Select(CreateDeclarationSummary)
            .Where(summary => summary is not null)
            .Cast<(string Kind, string Name)>()
            .ToArray();

        if (declarations.Length == 1)
        {
            var declaration = declarations[0];
            return namespaceName is null
                ? $"Defines {declaration.Kind} {declaration.Name}."
                : $"Defines {declaration.Kind} {declaration.Name} in namespace {namespaceName}.";
        }

        if (declarations.Length > 1)
        {
            var sampleNames = string.Join(", ", declarations.Take(3).Select(item => item.Name));
            return namespaceName is null
                ? $"Defines {declarations.Length} declarations including {sampleNames}."
                : $"Defines {declarations.Length} declarations in namespace {namespaceName}, including {sampleNames}.";
        }

        if (syntaxRoot is CompilationUnitSyntax compilationUnit && compilationUnit.Members.OfType<GlobalStatementSyntax>().Any())
        {
            return $"Contains top-level statements for project {document.Project.Name}.";
        }

        return FileSummaryGenerator.CreateSummary(normalizedPath, document.Project.Name);
    }

    private static (string Kind, string Name)? CreateDeclarationSummary(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax declaration when IsTopLevelDeclaration(declaration) => (SymbolKinds.Class, declaration.Identifier.ValueText),
            InterfaceDeclarationSyntax declaration when IsTopLevelDeclaration(declaration) => (SymbolKinds.Interface, declaration.Identifier.ValueText),
            StructDeclarationSyntax declaration when IsTopLevelDeclaration(declaration) => (SymbolKinds.Struct, declaration.Identifier.ValueText),
            RecordDeclarationSyntax declaration when IsTopLevelDeclaration(declaration) => (SymbolKinds.Record, declaration.Identifier.ValueText),
            EnumDeclarationSyntax declaration when IsTopLevelDeclaration(declaration) => (SymbolKinds.Enum, declaration.Identifier.ValueText),
            DelegateDeclarationSyntax declaration when IsTopLevelDeclaration(declaration) => (SymbolKinds.Delegate, declaration.Identifier.ValueText),
            _ => null
        };
    }

    private static bool IsTopLevelDeclaration(MemberDeclarationSyntax declaration)
    {
        return declaration.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax;
    }
}