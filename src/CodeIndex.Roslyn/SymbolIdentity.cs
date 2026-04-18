using Microsoft.CodeAnalysis;

namespace CodeIndex.Roslyn;

internal static class SymbolIdentity
{
    public static string CreateStableId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}