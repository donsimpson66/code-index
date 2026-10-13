using CodeIndex.Cli;
using CodeIndex.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace CodeIndex.Mcp;

[McpServerToolType]
public sealed class CodeIndexMcpTools(CodeIndexBuildService buildService, CodeIndexQueryService queryService)
{
    private const int DefaultFindLimit = 20;
    private const int DefaultChildrenLimit = 40;

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
        [Description("Optional directory where code-index artifacts will be written. Defaults to a .code-index directory rooted at the indexed workspace.")] string? outputDirectory = null,
        [Description("Include generated C# files such as obj outputs and *.g.cs files.")] bool includeGenerated = false,
        [Description("Include local variable symbols. Locals are excluded by default.")] bool includeLocals = false,
        [Description("Skip embedding generation.")] bool skipEmbeddings = false,
        [Description("Skip rewriting artifacts when the output directory already matches current file hashes.")] bool ifChanged = false,
        [Description("Existing code-index artifact directory to use as an incremental baseline.")] string? incrementalFromIndex = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedInputPath = Path.GetFullPath(path);
        var resolvedOutputDirectory = ResolveOutputDirectory(resolvedInputPath, outputDirectory);
        var resolvedIncrementalFromIndex = ResolveExplicitPath(incrementalFromIndex);
        var stopwatch = Stopwatch.StartNew();
        var request = new CodeIndexBuildRequest(
            resolvedInputPath,
            includeGenerated,
            resolvedIncrementalFromIndex,
            includeLocals,
            skipEmbeddings,
            ifChanged,
            resolvedOutputDirectory);
        var result = await buildService.BuildAsync(request, cancellationToken);
        CodeIndexSnapshotOutputPaths outputPaths;
        if (result.Stats.SkippedBecauseUnchanged)
        {
            outputPaths = new CodeIndexSnapshotOutputPaths(
                Path.Combine(resolvedOutputDirectory, "code-index.meta.json"),
                Path.Combine(resolvedOutputDirectory, "code-index.files.json"),
                Path.Combine(resolvedOutputDirectory, "code-index.symbols.json"),
                Path.Combine(resolvedOutputDirectory, "code-index.edges.json"),
                Path.Combine(resolvedOutputDirectory, "code-index.references.json"),
                Path.Combine(resolvedOutputDirectory, "code-index.embeddings.json"));
        }
        else
        {
            outputPaths = await buildService.WriteSnapshotAsync(result.Snapshot, resolvedOutputDirectory, cancellationToken);
        }

        stopwatch.Stop();

        return new BuildIndexToolResult(
            InputPath: result.Snapshot.Meta.InputPath,
            InputKind: result.Snapshot.Meta.InputKind,
            OutputDirectory: resolvedOutputDirectory,
            FileCount: result.Stats.FileCount,
            SymbolCount: result.Stats.SymbolCount,
            EdgeCount: result.Stats.EdgeCount,
            ReferenceCount: result.Stats.ReferenceCount,
            EmbeddingCount: result.Stats.EmbeddingCount,
            ElapsedMs: stopwatch.ElapsedMilliseconds,
            UsedIncrementalBaseline: result.Stats.UsedIncrementalBaseline,
            ReusedIncrementalBaseline: result.Stats.ReusedIncrementalBaseline,
            SkippedBecauseUnchanged: result.Stats.SkippedBecauseUnchanged,
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

    private static string ResolveOutputDirectory(string resolvedInputPath, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return ResolveExplicitPath(outputDirectory)!;
        }

        var workspaceRoot = SourceInputKindDetector.IsDirectoryInput(resolvedInputPath)
            ? resolvedInputPath
            : Path.GetDirectoryName(resolvedInputPath);

        return Path.Combine(workspaceRoot ?? Environment.CurrentDirectory, ".code-index");
    }

    private static string? ResolveExplicitPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, Environment.CurrentDirectory);
    }

    [McpServerTool(
        Name = "find_symbol",
        Title = "Find Symbols",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Finds symbols by simple name or qualified name from an existing code-index directory. Returns compact list projections by default.")]
    public async Task<IReadOnlyList<SymbolListItem>> FindSymbolAsync(
        [Description("Symbol name or qualified name query.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultFindLimit,
        [Description("Optional symbol kind filter such as class, method, property, or namespace.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: ranked, name, or accessibility.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedIndex = ResolveRequiredPath(indexDirectory, "An index directory is required.");
        var snapshot = await queryService.ReadSnapshotAsync(resolvedIndex, cancellationToken);
        var matches = await queryService.FindSymbolsAsync(new CodeIndexFindSymbolsRequest(query, resolvedIndex, limit, kind, accessibility, sort), cancellationToken);
        return QueryResultFormatter.ProjectSymbols(matches, snapshot);
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
        return queryService.GetSymbolAsync(query, ResolveRequiredPath(indexDirectory, "An index directory is required."), cancellationToken);
    }

    [McpServerTool(
        Name = "get_children",
        Title = "Get Symbol Children",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Gets child members for a symbol as compact list projections.")]
    public Task<IReadOnlyList<SymbolListItem>> GetChildrenAsync(
        [Description("Parent symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultChildrenLimit,
        [Description("Optional symbol kind filter such as method, property, or field.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return ProjectChildrenAsync(query, indexDirectory, limit, kind, accessibility, sort, queryService.GetChildrenAsync, cancellationToken);
    }

    [McpServerTool(
        Name = "outline",
        Title = "Outline Symbol",
        UseStructuredContent = true,
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Returns a compact symbol outline with the parent symbol and its children in one call.")]
    public async Task<OutlineToolResult> OutlineAsync(
        [Description("Parent symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of child results to return. Use 0 or less for no limit.")] int limit = DefaultChildrenLimit,
        [Description("Optional child kind filter such as method, property, or field.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = "declaration",
        CancellationToken cancellationToken = default)
    {
        var resolvedIndex = ResolveRequiredPath(indexDirectory, "An index directory is required.");
        var snapshot = await queryService.ReadSnapshotAsync(resolvedIndex, cancellationToken);
        var (symbol, children) = await queryService.GetOutlineAsync(
            new CodeIndexChildQueryRequest(query, resolvedIndex, limit, kind, accessibility, sort),
            cancellationToken);
        return new OutlineToolResult(
            QueryResultFormatter.ProjectSymbol(symbol, snapshot),
            QueryResultFormatter.ProjectSymbols(children, snapshot));
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
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultFindLimit,
        CancellationToken cancellationToken = default)
    {
        return queryService.FindReferencesAsync(new CodeIndexReferenceQuery(query, ResolveRequiredPath(indexDirectory, "An index directory is required."), limit), cancellationToken);
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
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultFindLimit,
        [Description("Optional item type filter: file or symbol.")] string? itemType = null,
        CancellationToken cancellationToken = default)
    {
        return queryService.SemanticSearchAsync(new CodeIndexSemanticSearchRequest(query, ResolveRequiredPath(indexDirectory, "An index directory is required."), limit, itemType), cancellationToken);
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
    public Task<IReadOnlyList<SymbolListItem>> GetCalleesAsync(
        [Description("Caller symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultChildrenLimit,
        [Description("Optional symbol kind filter such as method or constructor.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return ProjectChildrenAsync(query, indexDirectory, limit, kind, accessibility, sort, queryService.GetCalleesAsync, cancellationToken);
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
    public Task<IReadOnlyList<SymbolListItem>> GetCallersAsync(
        [Description("Callee symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultChildrenLimit,
        [Description("Optional symbol kind filter such as method or constructor.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return ProjectChildrenAsync(query, indexDirectory, limit, kind, accessibility, sort, queryService.GetCallersAsync, cancellationToken);
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
    public Task<IReadOnlyList<SymbolListItem>> GetTestsAsync(
        [Description("Production symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultChildrenLimit,
        [Description("Optional symbol kind filter such as method or class.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return ProjectChildrenAsync(query, indexDirectory, limit, kind, accessibility, sort, queryService.GetTestsAsync, cancellationToken);
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
    public Task<IReadOnlyList<SymbolListItem>> GetTestTargetsAsync(
        [Description("Test symbol ID or qualified name.")] string query,
        [Description("Path to the code-index artifact directory.")] string indexDirectory,
        [Description("Maximum number of results to return. Use 0 or less for no limit.")] int limit = DefaultChildrenLimit,
        [Description("Optional symbol kind filter such as method or class.")] string? kind = null,
        [Description("Optional accessibility filter such as public, internal, or private.")] string? accessibility = null,
        [Description("Sort order: name, accessibility, or declaration.")] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        return ProjectChildrenAsync(query, indexDirectory, limit, kind, accessibility, sort, queryService.GetTestTargetsAsync, cancellationToken);
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
        return queryService.GetExcerptAsync(new CodeIndexExcerptQuery(filePath, ResolveRequiredPath(indexDirectory, "An index directory is required."), startLine, endLine), cancellationToken);
    }

    private async Task<IReadOnlyList<SymbolListItem>> ProjectChildrenAsync(
        string query,
        string indexDirectory,
        int limit,
        string? kind,
        string? accessibility,
        string? sort,
        Func<CodeIndexChildQueryRequest, CancellationToken, Task<IReadOnlyList<SymbolRecord>>> execute,
        CancellationToken cancellationToken)
    {
        var resolvedIndex = ResolveRequiredPath(indexDirectory, "An index directory is required.");
        var snapshot = await queryService.ReadSnapshotAsync(resolvedIndex, cancellationToken);
        var results = await execute(new CodeIndexChildQueryRequest(query, resolvedIndex, limit, kind, accessibility, sort), cancellationToken);
        return QueryResultFormatter.ProjectSymbols(results, snapshot);
    }

    private static string ResolveRequiredPath(string path, string errorMessage)
    {
        return ResolveExplicitPath(path) ?? throw new InvalidOperationException(errorMessage);
    }
}

public sealed record OutlineToolResult(SymbolListItem Symbol, IReadOnlyList<SymbolListItem> Children);

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
    bool SkippedBecauseUnchanged,
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
