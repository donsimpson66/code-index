using CodeIndex.Roslyn;

namespace CodeIndex.Roslyn.Tests;

public class WorkspaceIndexBuildersTests
{
    [Fact]
    public async Task BuildAsync_GeneratesStructuredFileSummaries()
    {
        var solutionPath = GetSolutionPath();
        var builder = new WorkspaceFileIndexBuilder();

        var files = await builder.BuildAsync(solutionPath);
        var programFile = Assert.Single(files, file => file.Path == "src/CodeIndex.Cli/Program.cs");

        Assert.Contains("top-level statements", programFile.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ExtractsWorkspaceInspectorSymbol()
    {
        var solutionPath = GetSolutionPath();
        var fileBuilder = new WorkspaceFileIndexBuilder();
        var symbolBuilder = new WorkspaceSymbolIndexBuilder();

        var files = await fileBuilder.BuildAsync(solutionPath);
        var symbols = await symbolBuilder.BuildAsync(solutionPath, files);

        Assert.Contains(symbols, symbol => symbol.Name == "WorkspaceInspector" && symbol.Kind == "class");
        Assert.Contains(symbols, symbol => symbol.Name == "InspectAsync" && symbol.Kind == "method");
    }

    [Fact]
    public async Task BuildAsync_GeneratesContainsEdgesForIndexedSymbols()
    {
        var solutionPath = GetSolutionPath();
        var builder = new WorkspaceEdgeIndexBuilder();

        var edges = await builder.BuildAsync(solutionPath);

        Assert.Contains(edges, edge =>
            edge.Type == "contains" &&
            edge.From == "s:T:CodeIndex.Roslyn.WorkspaceInspector" &&
            edge.To == "s:M:CodeIndex.Roslyn.WorkspaceInspector.InspectAsync(System.String,System.Threading.CancellationToken)");

        Assert.Equal(edges.OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal), edges);
    }

    [Fact]
    public async Task BuildAsync_IncludeGenerated_AddsObjDocuments()
    {
        var solutionPath = GetSolutionPath();
        var builder = new WorkspaceFileIndexBuilder();

        var files = await builder.BuildAsync(solutionPath, includeGenerated: true);

        Assert.Contains(files, file => file.Path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetSolutionPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../code-index.sln"));
    }
}