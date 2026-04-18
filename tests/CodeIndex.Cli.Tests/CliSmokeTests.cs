using System.Text.Json;

namespace CodeIndex.Cli.Tests;

public sealed record CliInvocationResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class CliSmokeTests(IndexFixture fixture) : IClassFixture<IndexFixture>
{
    public static TheoryData<string, string, string, int, int> CommonAgentBenchmarkSearches => new()
    {
        {
            "WorkspaceSymbolIndexBuilder",
            "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder",
            "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs",
            1,
            20
        },
        {
            "CliApplication",
            "CodeIndex.Cli.CliApplication",
            "src/CodeIndex.Cli/CliApplication.cs",
            1,
            20
        },
        {
            "SqliteCodeIndexStore",
            "CodeIndex.Core.SqliteCodeIndexStore",
            "src/CodeIndex.Core/SqliteCodeIndexStore.cs",
            1,
            20
        }
    };

    [Fact]
    public async Task Build_WritesExpectedArtifacts_ForRoslynProject()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-cli-tests-{Guid.NewGuid():N}");
        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            BuildFilesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.FileRecord>>(
            [
                new global::CodeIndex.Core.FileRecord("f:WorkspaceInspection.cs", "WorkspaceInspection.cs", "CodeIndex.Roslyn", "C#", "sha256:test", "summary")
            ]),
            BuildSymbolsAsync = (_, _, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.SymbolRecord>>(Array.Empty<global::CodeIndex.Core.SymbolRecord>()),
            BuildEdgesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.EdgeRecord>>(Array.Empty<global::CodeIndex.Core.EdgeRecord>())
        };

        try
        {
            var output = await fixture.RunCliAsync(
                runtime,
                "build",
                fixture.RoslynProjectPath,
                "--out",
                outputDirectory,
                "--verbose");

            Assert.Contains("Validation passed.", output);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "code-index.meta.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "code-index.files.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "code-index.symbols.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "code-index.edges.json")));

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.meta.json")));
            var meta = document.RootElement;

            Assert.Equal("project", meta.GetProperty("inputKind").GetString());
            Assert.Equal(fixture.RoslynProjectPath, meta.GetProperty("inputPath").GetString());
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Inspect_ListsRoslynProject()
    {
        var output = await fixture.RunCliAsync("inspect", fixture.SolutionPath);

        Assert.Contains("PROJECT CodeIndex.Roslyn", output);
        Assert.Contains("WorkspaceInspection.cs", output);
    }

    [Fact]
    public async Task Inspect_ListsSampleProject()
    {
        var output = await fixture.RunCliAsync("inspect", fixture.SampleSolutionPath);

        Assert.Contains("PROJECT SampleLibrary", output);
        Assert.Contains("FriendlyGreeter.cs", output);
    }

    [Fact]
    public async Task FindSymbol_ReturnsLimitedFilteredResults()
    {
        var output = await fixture.RunCliAsync(
            "find-symbol",
            "WorkspaceInspector",
            "--index",
            fixture.IndexDirectory,
            "--kind",
            "class",
            "--accessibility",
            "public",
            "--limit",
            "1");

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement;

        Assert.Equal(JsonValueKind.Array, results.ValueKind);
        Assert.Single(results.EnumerateArray());

        var symbol = results[0];
        Assert.Equal("WorkspaceInspector", symbol.GetProperty("name").GetString());
        Assert.Equal("class", symbol.GetProperty("kind").GetString());
        Assert.Equal("public", symbol.GetProperty("accessibility").GetString());
    }

    [Fact]
    public async Task GetChildren_SupportsDeclarationSort()
    {
        var output = await fixture.RunCliAsync(
            "get-children",
            "CodeIndex.Roslyn.WorkspaceInspector",
            "--index",
            fixture.IndexDirectory,
            "--kind",
            "method",
            "--sort",
            "declaration");

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.NotEmpty(results);
        Assert.Equal("InspectAsync", results[0].GetProperty("name").GetString());
        Assert.All(results, symbol => Assert.Equal("method", symbol.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task GetSymbol_ReturnsExactSymbol()
    {
        var output = await fixture.RunCliAsync(
            "get-symbol",
            "CodeIndex.Roslyn.WorkspaceInspector",
            "--index",
            fixture.IndexDirectory);

        using var document = JsonDocument.Parse(output);
        var symbol = document.RootElement;

        Assert.Equal("WorkspaceInspector", symbol.GetProperty("name").GetString());
        Assert.Equal("CodeIndex.Roslyn.WorkspaceInspector", symbol.GetProperty("qualifiedName").GetString());
    }

    [Fact]
    public async Task GetExcerpt_ReturnsRequestedLines()
    {
        var output = await fixture.RunCliAsync(
            "get-excerpt",
            "src/CodeIndex.Roslyn/WorkspaceInspection.cs",
            "--index",
            fixture.IndexDirectory,
            "--start",
            "15",
            "--end",
            "16");

        using var document = JsonDocument.Parse(output);
        var lines = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(2, lines.Length);
        Assert.Equal(15, lines[0].GetProperty("line").GetInt32());
        Assert.Contains("WorkspaceInspector", lines[0].GetProperty("text").GetString());
        Assert.Equal(16, lines[1].GetProperty("line").GetInt32());
    }

    [Theory]
    [MemberData(nameof(CommonAgentBenchmarkSearches))]
    public async Task Benchmark_ReturnsProjectAndTargetedComparisonMetrics_ForCommonAgentSearches(
        string symbolQuery,
        string selectedQualifiedName,
        string filePath,
        int start,
        int end)
    {
        var output = await fixture.RunCliAsync(
            "benchmark",
            "--index",
            await fixture.GetSolutionIndexDirectoryAsync(),
            "--symbol",
            symbolQuery,
            "--file",
            filePath,
            "--start",
            start.ToString(),
            "--end",
            end.ToString());

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal("solution", root.GetProperty("inputKind").GetString());
        Assert.True(root.GetProperty("rawSource").GetProperty("fileCount").GetInt32() > 0);
        Assert.True(root.GetProperty("rawSource").GetProperty("totalBytes").GetInt64() > 0);
        Assert.True(root.GetProperty("indexArtifacts").GetProperty("totalBytes").GetInt64() > 0);
        Assert.Equal(symbolQuery, root.GetProperty("symbolQuery").GetProperty("query").GetString());
        Assert.Equal(selectedQualifiedName, root.GetProperty("symbolQuery").GetProperty("selectedQualifiedName").GetString());
        Assert.Equal(filePath, root.GetProperty("excerptQuery").GetProperty("file").GetString());
        Assert.True(root.GetProperty("excerptQuery").GetProperty("excerptBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstFlowBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstVsFullSourceRatio").GetDouble() > 0);
    }

    [Fact]
    public async Task FindSymbol_UsingRepositorySolutionIndex_ReturnsCliApplication()
    {
        var output = await fixture.RunCliAsync(
            "find-symbol",
            "CodeIndex.Cli.CliApplication",
            "--index",
            await fixture.GetSolutionIndexDirectoryAsync(),
            "--limit",
            "1");

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.Single(results);
        Assert.Equal("CliApplication", results[0].GetProperty("name").GetString());
        Assert.Equal("CodeIndex.Cli.CliApplication", results[0].GetProperty("qualifiedName").GetString());
    }

    [Fact]
    public async Task FindSymbol_UsingRepositorySolutionDatabase_ReturnsCliApplication()
    {
        var output = await fixture.RunCliAsync(
            "find-symbol",
            "CodeIndex.Cli.CliApplication",
            "--db",
            await fixture.GetSolutionDatabasePathAsync(),
            "--limit",
            "1");

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.Single(results);
        Assert.Equal("CliApplication", results[0].GetProperty("name").GetString());
        Assert.Equal("CodeIndex.Cli.CliApplication", results[0].GetProperty("qualifiedName").GetString());
    }

    [Theory]
    [MemberData(nameof(CommonAgentBenchmarkSearches))]
    public async Task Benchmark_UsingRepositorySolutionDatabase_ReturnsDatabaseMetrics_ForCommonAgentSearches(
        string symbolQuery,
        string selectedQualifiedName,
        string filePath,
        int start,
        int end)
    {
        var output = await fixture.RunCliAsync(
            "benchmark",
            "--db",
            await fixture.GetSolutionDatabasePathAsync(),
            "--symbol",
            symbolQuery,
            "--file",
            filePath,
            "--start",
            start.ToString(),
            "--end",
            end.ToString());

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal("solution", root.GetProperty("inputKind").GetString());
        Assert.True(root.GetProperty("database").GetProperty("totalBytes").GetInt64() > 0);
        Assert.Equal(symbolQuery, root.GetProperty("symbolQuery").GetProperty("query").GetString());
        Assert.Equal(selectedQualifiedName, root.GetProperty("symbolQuery").GetProperty("selectedQualifiedName").GetString());
        Assert.Equal(filePath, root.GetProperty("excerptQuery").GetProperty("file").GetString());
        Assert.True(root.GetProperty("indexFirstFlowBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstVsFullSourceRatio").GetDouble() > 0);
    }

    [Fact]
    public async Task Build_WritesSqliteDatabase_WhenDbOutIsProvided()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"code-index-cli-tests-{Guid.NewGuid():N}.db");
        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            BuildFilesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.FileRecord>>(
            [
                new global::CodeIndex.Core.FileRecord("f:WorkspaceInspection.cs", "WorkspaceInspection.cs", "CodeIndex.Roslyn", "C#", "sha256:test", "summary")
            ]),
            BuildSymbolsAsync = (_, _, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.SymbolRecord>>(
            [
                new global::CodeIndex.Core.SymbolRecord("s:T:CodeIndex.Roslyn.WorkspaceInspector", "WorkspaceInspector", "CodeIndex.Roslyn.WorkspaceInspector", "class", "f:WorkspaceInspection.cs", new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "class CodeIndex.Roslyn.WorkspaceInspector", "summary", null, "public", false, false, false, false)
            ]),
            BuildEdgesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.EdgeRecord>>(Array.Empty<global::CodeIndex.Core.EdgeRecord>())
        };

        try
        {
            var output = await fixture.RunCliAsync(
                runtime,
                "build",
                fixture.RoslynProjectPath,
                "--db-out",
                databasePath,
                "--verbose");

            Assert.Contains("Wrote SQLite index to", output);
            Assert.True(File.Exists(databasePath));

            var sqliteStore = new global::CodeIndex.Core.SqliteCodeIndexStore();
            var symbol = await sqliteStore.GetSymbolAsync(databasePath, "CodeIndex.Roslyn.WorkspaceInspector");

            Assert.NotNull(symbol);
            Assert.Equal("WorkspaceInspector", symbol!.Name);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task FindSymbol_WithoutIndex_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync("find-symbol", "WorkspaceInspector");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("An index directory or database is required. Pass --index <path> or --db <path>.", result.StandardError);
    }

    [Fact]
    public async Task GetSymbol_UnknownQuery_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync(
            "get-symbol",
            "CodeIndex.Roslyn.DoesNotExist",
            "--index",
            fixture.IndexDirectory);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No symbol found for query: CodeIndex.Roslyn.DoesNotExist", result.StandardError);
    }

    [Fact]
    public async Task GetExcerpt_InvalidRange_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync(
            "get-excerpt",
            "src/CodeIndex.Roslyn/WorkspaceInspection.cs",
            "--index",
            fixture.IndexDirectory,
            "--start",
            "16",
            "--end",
            "15");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Use positive line numbers and ensure --end is greater than or equal to --start.", result.StandardError);
    }

    [Fact]
    public async Task GetChildren_UnsupportedSort_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync(
            "get-children",
            "CodeIndex.Roslyn.WorkspaceInspector",
            "--index",
            fixture.IndexDirectory,
            "--sort",
            "ranked");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unsupported --sort for get-children. Use name, accessibility, or declaration.", result.StandardError);
    }

    [Fact]
    public async Task Benchmark_WithExcerptRangeButNoFile_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync(
            "benchmark",
            "--index",
            fixture.IndexDirectory,
            "--start",
            "1",
            "--end",
            "20");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Pass --file when using --start or --end with benchmark.", result.StandardError);
    }

    [Fact]
    public async Task FindSymbol_WithIndexAndDbTogether_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync(
            "find-symbol",
            "WorkspaceInspector",
            "--index",
            fixture.IndexDirectory,
            "--db",
            await fixture.GetSolutionDatabasePathAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Pass either --index <path> or --db <path>, but not both.", result.StandardError);
    }
}

public sealed class IndexFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim consoleLock = new(1, 1);
    private readonly SemaphoreSlim solutionIndexLock = new(1, 1);
    private readonly SemaphoreSlim solutionDatabaseLock = new(1, 1);
    private string? solutionIndexDirectory;
    private string? solutionDatabasePath;

    public string RepoRoot { get; } = FindRepoRoot();

    public string SolutionPath => Path.Combine(RepoRoot, "code-index.sln");

    public string SampleSolutionPath => Path.Combine(RepoRoot, "samples", "SampleSolution", "SampleSolution.sln");

    public string RoslynProjectPath => Path.Combine(RepoRoot, "src", "CodeIndex.Roslyn", "CodeIndex.Roslyn.csproj");

    public string IndexDirectory { get; } = Path.Combine(FindRepoRoot(), "artifacts", "code-index");

    public Task InitializeAsync()
    {
        if (!Directory.Exists(IndexDirectory))
        {
            throw new InvalidOperationException($"Index directory not found at {IndexDirectory}");
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        consoleLock.Dispose();
        solutionIndexLock.Dispose();
        solutionDatabaseLock.Dispose();

        if (!string.IsNullOrWhiteSpace(solutionIndexDirectory) && Directory.Exists(solutionIndexDirectory))
        {
            Directory.Delete(solutionIndexDirectory, recursive: true);
        }

        if (!string.IsNullOrWhiteSpace(solutionDatabasePath) && File.Exists(solutionDatabasePath))
        {
            File.Delete(solutionDatabasePath);
        }

        return Task.CompletedTask;
    }

    public async Task<string> GetSolutionIndexDirectoryAsync()
    {
        if (!string.IsNullOrWhiteSpace(solutionIndexDirectory) && Directory.Exists(solutionIndexDirectory))
        {
            return solutionIndexDirectory;
        }

        await solutionIndexLock.WaitAsync();

        try
        {
            if (!string.IsNullOrWhiteSpace(solutionIndexDirectory) && Directory.Exists(solutionIndexDirectory))
            {
                return solutionIndexDirectory;
            }

            solutionIndexDirectory = Path.Combine(Path.GetTempPath(), $"code-index-cli-solution-{Guid.NewGuid():N}");
            var fileBuilder = new global::CodeIndex.Roslyn.WorkspaceFileIndexBuilder();
            var symbolBuilder = new global::CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder();
            var edgeBuilder = new global::CodeIndex.Roslyn.WorkspaceEdgeIndexBuilder();
            var files = await fileBuilder.BuildAsync(SolutionPath);
            var symbols = await symbolBuilder.BuildAsync(SolutionPath, files);
            var edges = await edgeBuilder.BuildAsync(SolutionPath);
            var meta = global::CodeIndex.Core.CodeIndexMetaFactory.Create(SolutionPath, "solution");
            var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(meta, files, symbols, edges);
            var validator = new global::CodeIndex.Core.CodeIndexValidator();
            validator.ValidateOrThrow(snapshot);

            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.meta.json"), meta);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.files.json"), files);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.symbols.json"), symbols);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.edges.json"), edges);

            return solutionIndexDirectory;
        }
        finally
        {
            solutionIndexLock.Release();
        }
    }

    public async Task<string> GetSolutionDatabasePathAsync()
    {
        if (!string.IsNullOrWhiteSpace(solutionDatabasePath) && File.Exists(solutionDatabasePath))
        {
            return solutionDatabasePath;
        }

        await solutionDatabaseLock.WaitAsync();

        try
        {
            if (!string.IsNullOrWhiteSpace(solutionDatabasePath) && File.Exists(solutionDatabasePath))
            {
                return solutionDatabasePath;
            }

            solutionDatabasePath = Path.Combine(Path.GetTempPath(), $"code-index-cli-solution-{Guid.NewGuid():N}.db");
            var fileBuilder = new global::CodeIndex.Roslyn.WorkspaceFileIndexBuilder();
            var symbolBuilder = new global::CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder();
            var edgeBuilder = new global::CodeIndex.Roslyn.WorkspaceEdgeIndexBuilder();
            var files = await fileBuilder.BuildAsync(SolutionPath);
            var symbols = await symbolBuilder.BuildAsync(SolutionPath, files);
            var edges = await edgeBuilder.BuildAsync(SolutionPath);
            var meta = global::CodeIndex.Core.CodeIndexMetaFactory.Create(SolutionPath, "solution");
            var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(meta, files, symbols, edges);
            var validator = new global::CodeIndex.Core.CodeIndexValidator();
            validator.ValidateOrThrow(snapshot);
            var sqliteStore = new global::CodeIndex.Core.SqliteCodeIndexStore();
            await sqliteStore.WriteAsync(solutionDatabasePath, snapshot);

            return solutionDatabasePath;
        }
        finally
        {
            solutionDatabaseLock.Release();
        }
    }

    public async Task<string> RunCliAsync(params string[] arguments)
    {
        var result = await InvokeCliAsync(null, arguments);

        if (result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"CLI exited with code {result.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
        }

        return result.StandardOutput;
    }

    public async Task<string> RunCliAsync(global::CodeIndex.Cli.CliRuntime runtime, params string[] arguments)
    {
        var result = await InvokeCliAsync(runtime, arguments);

        if (result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"CLI exited with code {result.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
        }

        return result.StandardOutput;
    }

    public async Task<CliInvocationResult> RunCliExpectFailureAsync(params string[] arguments)
    {
        var result = await InvokeCliAsync(null, arguments);

        if (result.ExitCode == 0)
        {
            throw new Xunit.Sdk.XunitException($"CLI unexpectedly succeeded.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
        }

        return result;
    }

    private async Task<CliInvocationResult> InvokeCliAsync(global::CodeIndex.Cli.CliRuntime? runtime, string[] arguments)
    {
        await consoleLock.WaitAsync();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        try
        {
            Console.SetOut(outputWriter);
            Console.SetError(errorWriter);

            var exitCode = await global::CodeIndex.Cli.CliApplication.RunAsync(arguments, runtime);
            return new CliInvocationResult(exitCode, outputWriter.ToString(), errorWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            consoleLock.Release();
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "code-index.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}