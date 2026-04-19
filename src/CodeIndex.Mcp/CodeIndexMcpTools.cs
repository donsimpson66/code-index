using CodeIndex.Cli;
using CodeIndex.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace CodeIndex.Mcp;

[McpServerToolType]
public sealed class CodeIndexMcpTools(CodeIndexBuildService buildService, CodeIndexQueryService queryService)
{
    [McpServerTool(
        Name = "build_index",
        Title = "Build Code Index",
        UseStructuredContent = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Builds JSON code-index artifacts from a solution, project, or supported source directory.")]
    public async Task<BuildIndexToolResult> BuildIndexAsync(
        [Description("Path to a .sln, .csproj, or supported source directory.")] string path,
        [Description("Directory where code-index artifacts will be written.")] string outputDirectory,
        [Description("Include generated C# files such as obj outputs and *.g.cs files.")] bool includeGenerated = false,
        [Description("Existing code-index artifact directory to use as an incremental baseline.")] string? incrementalFromIndex = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = new CodeIndexBuildRequest(path, includeGenerated, incrementalFromIndex);
        var result = await buildService.BuildAsync(request, cancellationToken);
        var outputPaths = await buildService.WriteSnapshotAsync(result.Snapshot, outputDirectory, cancellationToken);
        stopwatch.Stop();

        return new BuildIndexToolResult(
            InputPath: result.Snapshot.Meta.InputPath,
            InputKind: result.Snapshot.Meta.InputKind,
            OutputDirectory: Path.GetFullPath(outputDirectory),
            FileCount: result.Stats.FileCount,
            SymbolCount: result.Stats.SymbolCount,
            EdgeCount: result.Stats.EdgeCount,
            ReferenceCount: result.Stats.ReferenceCount,
            EmbeddingCount: result.Stats.EmbeddingCount,
            ElapsedMs: stopwatch.ElapsedMilliseconds,
            UsedIncrementalBaseline: result.Stats.UsedIncrementalBaseline,
            ReusedIncrementalBaseline: result.Stats.ReusedIncrementalBaseline,
            ChangedFileCount: result.Stats.ChangedFileCount,
            RemovedFileCount: result.Stats.RemovedFileCount,
            RebuiltSymbolCount: result.Stats.RebuiltSymbolCount,
            RebuiltEdgeCount: result.Stats.RebuiltEdgeCount,
            RebuiltReferenceCount: result.Stats.RebuiltReferenceCount,
            RebuiltEdgeFileCount: result.Stats.RebuiltEdgeFileCount,
            RebuiltReferenceFileCount: result.Stats.RebuiltReferenceFileCount,
            MetaPath: outputPaths.MetaPath,
            FilesPath: outputPaths.FilesPath,
            SymbolsPath: outputPaths.SymbolsPath,
            EdgesPath: outputPaths.EdgesPath,
            ReferencesPath: outputPaths.ReferencesPath,
            EmbeddingsPath: outputPaths.EmbeddingsPath,
            Warnings: Array.Empty<string>());
    }

    [McpServerTool(
        Name = "find_symbol",
        Title = "Find Symbols",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Finds symbols by simple name or qualified name from an existing code-index directory.")]
    public Task<IReadOnlyList<SymbolRecord>> FindSymbolAsync(
        [Description("Symbol name or qualified name query.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 10,
        [Description("Optional symbol kind filter such as class, method, property, or namespace.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: ranked, name, or accessibility.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.FindSymbolsAsync(new CodeIndexFindSymbolsRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken);
    }

    [McpServerTool(
        Name = "get_symbol",
        Title = "Get Symbol",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets one symbol by symbol ID or qualified name.")]
    public Task<SymbolRecord> GetSymbolAsync(
        [Description("Symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        CancellationToken cancellationToken = default)
    {
        return queryService.GetSymbolAsync(query, indexDirectory, cancellationToken);
    }

    [McpServerTool(
        Name = "get_children",
        Title = "Get Symbol Children",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets child members for a symbol.")]
    public Task<IReadOnlyList<SymbolRecord>> GetChildrenAsync(
        [Description("Parent symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 0,
        [Description("Optional symbol kind filter such as method, property, or field.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.GetChildrenAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken);
    }

    [McpServerTool(
        Name = "find_references",
        Title = "Find References",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Finds cached reference locations for a symbol.")]
    public Task<IReadOnlyList<CodeIndexReferenceSearchResult>> FindReferencesAsync(
        [Description("Target symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return queryService.FindReferencesAsync(new CodeIndexReferenceQuery(query, indexDirectory, limit), cancellationToken);
    }

    [McpServerTool(
        Name = "semantic_search",
        Title = "Semantic Search",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Searches file and symbol embeddings using deterministic cosine similarity.")]
    public Task<IReadOnlyList<CodeIndexSemanticSearchResult>> SemanticSearchAsync(
        [Description("Semantic query text.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 10,
        [Description("Optional item type filter: file or symbol.")] string? itemType = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.SemanticSearchAsync(new CodeIndexSemanticSearchRequest(query, indexDirectory, limit, itemType), cancellationToken);
    }

    [McpServerTool(
        Name = "get_callees",
        Title = "Get Callees",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets direct call targets for a method or constructor.")]
    public Task<IReadOnlyList<SymbolRecord>> GetCalleesAsync(
        [Description("Caller symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 0,
        [Description("Optional symbol kind filter such as method or constructor.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.GetCalleesAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken);
    }

    [McpServerTool(
        Name = "get_callers",
        Title = "Get Callers",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets direct callers for a method or constructor.")]
    public Task<IReadOnlyList<SymbolRecord>> GetCallersAsync(
        [Description("Callee symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 0,
        [Description("Optional symbol kind filter such as method or constructor.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.GetCallersAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken);
    }

    [McpServerTool(
        Name = "get_tests",
        Title = "Get Tests",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets heuristic tests linked to a production symbol.")]
    public Task<IReadOnlyList<SymbolRecord>> GetTestsAsync(
        [Description("Production symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 0,
        [Description("Optional symbol kind filter such as method or class.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.GetTestsAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken);
    }

    [McpServerTool(
        Name = "get_test_targets",
        Title = "Get Test Targets",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets heuristic production targets linked to a test symbol.")]
    public Task<IReadOnlyList<SymbolRecord>> GetTestTargetsAsync(
        [Description("Test symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = 0,
        [Description("Optional symbol kind filter such as method or class.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.GetTestTargetsAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken);
    }

    [McpServerTool(
        Name = "get_excerpt",
        Title = "Get Excerpt",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets exact file lines from an indexed file path.")]
    public Task<IReadOnlyList<CodeIndexExcerptLine>> GetExcerptAsync(
        [Description("Repository-relative file path exactly as stored in the index.")] string filePath,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Inclusive starting line number.")] int startLine,
        [Description("Inclusive ending line number.")] int endLine,
        CancellationToken cancellationToken = default)
    {
        return queryService.GetExcerptAsync(new CodeIndexExcerptQuery(filePath, indexDirectory, startLine, endLine), cancellationToken);
    }
}

public sealed record BuildIndexToolResult(
    string InputPath,
    string InputKind,
    string OutputDirectory,
    int FileCount,
    int SymbolCount,
    int EdgeCount,
    int ReferenceCount,
    int EmbeddingCount,
    long ElapsedMs,
    bool UsedIncrementalBaseline,
    bool ReusedIncrementalBaseline,
    int ChangedFileCount,
    int RemovedFileCount,
    int RebuiltSymbolCount,
    int RebuiltEdgeCount,
    int RebuiltReferenceCount,
    int RebuiltEdgeFileCount,
    int RebuiltReferenceFileCount,
    string MetaPath,
    string FilesPath,
    string SymbolsPath,
    string EdgesPath,
    string ReferencesPath,
    string EmbeddingsPath,
    IReadOnlyList<string> Warnings);