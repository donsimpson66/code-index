using Microsoft.CodeAnalysis;

namespace CodeIndex.Roslyn;

internal static class SymbolIdentity
{
    public static string CreateStableId(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol localSymbol => CreateLocalStableId(localSymbol),
            _ => symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };
    }

    private static string CreateLocalStableId(ILocalSymbol localSymbol)
    {
        var containingSymbolId = localSymbol.ContainingSymbol is null
            ? "global"
            : CreateStableId(localSymbol.ContainingSymbol);
        var declaration = localSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        var spanStart = declaration?.Span.Start ?? localSymbol.Locations.FirstOrDefault(static location => location.IsInSource)?.SourceSpan.Start ?? -1;
        return $"local:{containingSymbolId}:{localSymbol.Name}:{spanStart}";
    }
}