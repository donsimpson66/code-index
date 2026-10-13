using CodeIndex.Core;

namespace CodeIndex.Core.Tests;

public sealed class SymbolPresentationTests
{
    [Fact]
    public void IsBoilerplateSummary_DetectsFallbackPatterns()
    {
        Assert.True(SymbolPresentation.IsBoilerplateSummary("method BuildAsync.", "method", "BuildAsync"));
        Assert.True(SymbolPresentation.IsBoilerplateSummary("method greet in TypeScript source.", "method", "greet"));
        Assert.False(SymbolPresentation.IsBoilerplateSummary("Builds the workspace symbol index.", "method", "BuildAsync"));
    }

    [Fact]
    public void ShortenSignature_StripsCommonNamespaces()
    {
        var shortened = SymbolPresentation.ShortenSignature(
            "System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<CodeIndex.Core.SymbolRecord>> CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder.BuildAsync(string)");

        Assert.DoesNotContain("System.Threading.Tasks", shortened);
        Assert.DoesNotContain("System.Collections.Generic", shortened);
        Assert.Contains("Task<", shortened);
        Assert.Contains("BuildAsync", shortened);
    }

    [Fact]
    public void FilterLocals_RemovesLocalSymbolsAndRelatedGraph()
    {
        var type = new SymbolRecord("s:T:A.Foo", "Foo", "A.Foo", SymbolKinds.Class, "f:a.cs", new TextRangeRecord(1, 1, 10, 2), "class A.Foo", "", null, "public", false, false, false, false);
        var local = new SymbolRecord("s:local:x", "x", "x", SymbolKinds.Local, "f:a.cs", new TextRangeRecord(2, 1, 2, 2), "int x", "", type.Id, "local", false, false, false, false);
        var edge = new EdgeRecord(EdgeTypes.Contains, type.Id, local.Id);
        var reference = new ReferenceRecord(local.Id, type.Id, "f:a.cs", new TextRangeRecord(2, 1, 2, 2), "var x = 1;");
        var embedding = new EmbeddingRecord(EmbeddingItemTypes.Symbol, local.Id, [1f]);

        var (symbols, edges, references, embeddings) = SymbolPresentation.FilterLocals(
            [type, local],
            [edge],
            [reference],
            [embedding],
            includeLocals: false);

        Assert.Single(symbols);
        Assert.Equal(type.Id, symbols[0].Id);
        Assert.Empty(edges);
        Assert.Empty(references);
        Assert.Empty(embeddings);
    }
}
