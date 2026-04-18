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
            BuildEdgesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.EdgeRecord>>(Array.Empty<global::CodeIndex.Core.EdgeRecord>()),
            BuildReferencesAsync = (_, _, _, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.ReferenceRecord>>(Array.Empty<global::CodeIndex.Core.ReferenceRecord>())
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
            Assert.True(File.Exists(Path.Combine(outputDirectory, "code-index.references.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "code-index.embeddings.json")));

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
    public async Task GetCallees_ReturnsCallTargets_FromIndexSnapshot()
    {
        var callerFile = new global::CodeIndex.Core.FileRecord("f:Caller.cs", "Caller.cs", "Sample", "C#", "sha256:caller", "summary");
        var calleeFile = new global::CodeIndex.Core.FileRecord("f:Callee.cs", "Callee.cs", "Sample", "C#", "sha256:callee", "summary");
        var caller = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.Caller.Run", "Run", "Sample.Caller.Run", "method", callerFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 3, 2), "string Sample.Caller.Run()", "summary", "s:T:Sample.Caller", "public", false, false, false, false);
        var callee = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.Greeter.Greet(System.String)", "Greet", "Sample.Greeter.Greet(System.String)", "method", calleeFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 2, 2), "string Sample.Greeter.Greet(string)", "summary", "s:T:Sample.Greeter", "public", false, false, false, false);
        var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "sample", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.SolutionPath, "solution"),
            [callerFile, calleeFile],
            [caller, callee],
            [new global::CodeIndex.Core.EdgeRecord(global::CodeIndex.Core.EdgeTypes.Calls, caller.Id, callee.Id)],
            Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());
        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(snapshot)
        };

        var output = await fixture.RunCliAsync(
            runtime,
            "get-callees",
            caller.QualifiedName,
            "--index",
            Path.Combine(fixture.RepoRoot, "unused-index"));

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.Single(results);
        Assert.Equal("Greet", results[0].GetProperty("name").GetString());
        Assert.Equal(callee.QualifiedName, results[0].GetProperty("qualifiedName").GetString());
    }

    [Fact]
    public async Task GetCallers_ReturnsCallers_FromSqliteStore()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"code-index-cli-callers-{Guid.NewGuid():N}.db");
        var callerFile = new global::CodeIndex.Core.FileRecord("f:Caller.cs", "Caller.cs", "Sample", "C#", "sha256:caller", "summary");
        var calleeFile = new global::CodeIndex.Core.FileRecord("f:Callee.cs", "Callee.cs", "Sample", "C#", "sha256:callee", "summary");
        var caller = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.Caller.Run", "Run", "Sample.Caller.Run", "method", callerFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 3, 2), "string Sample.Caller.Run()", "summary", "s:T:Sample.Caller", "public", false, false, false, false);
        var callee = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.Greeter.Greet(System.String)", "Greet", "Sample.Greeter.Greet(System.String)", "method", calleeFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 2, 2), "string Sample.Greeter.Greet(string)", "summary", "s:T:Sample.Greeter", "public", false, false, false, false);
        var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "sample", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.SolutionPath, "solution"),
            [callerFile, calleeFile],
            [caller, callee],
            [new global::CodeIndex.Core.EdgeRecord(global::CodeIndex.Core.EdgeTypes.Calls, caller.Id, callee.Id)],
            Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());

        try
        {
            var sqliteStore = new global::CodeIndex.Core.SqliteCodeIndexStore();
            await sqliteStore.WriteAsync(databasePath, snapshot);

            var output = await fixture.RunCliAsync(
                "get-callers",
                callee.QualifiedName,
                "--db",
                databasePath);

            using var document = JsonDocument.Parse(output);
            var results = document.RootElement.EnumerateArray().ToArray();

            Assert.Single(results);
            Assert.Equal("Run", results[0].GetProperty("name").GetString());
            Assert.Equal(caller.QualifiedName, results[0].GetProperty("qualifiedName").GetString());
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
    public async Task GetTestTargets_ReturnsTargets_FromIndexSnapshot()
    {
        var productionFile = new global::CodeIndex.Core.FileRecord("f:Greeter.cs", "Greeter.cs", "Sample", "C#", "sha256:prod", "summary");
        var testFile = new global::CodeIndex.Core.FileRecord("f:GreeterTests.cs", "GreeterTests.cs", "Sample.Tests", "C#", "sha256:test", "summary");
        var target = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.FriendlyGreeter.CreateGreeting(System.String)", "CreateGreeting", "Sample.FriendlyGreeter.CreateGreeting(System.String)", "method", productionFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 2, 2), "string Sample.FriendlyGreeter.CreateGreeting(string)", "summary", "s:T:Sample.FriendlyGreeter", "public", false, false, false, false);
        var testMethod = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting", "CreateGreeting_ReturnsGreeting", "Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting", "method", testFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 3, 2), "void Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting()", "summary", "s:T:Sample.Tests.FriendlyGreeterTests", "public", false, false, false, false);
        var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "sample", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.SolutionPath, "solution"),
            [productionFile, testFile],
            [target, testMethod],
            [new global::CodeIndex.Core.EdgeRecord(global::CodeIndex.Core.EdgeTypes.Tests, testMethod.Id, target.Id)],
            Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());
        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(snapshot)
        };

        var output = await fixture.RunCliAsync(
            runtime,
            "get-test-targets",
            testMethod.QualifiedName,
            "--index",
            Path.Combine(fixture.RepoRoot, "unused-index"));

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.Single(results);
        Assert.Equal("CreateGreeting", results[0].GetProperty("name").GetString());
        Assert.Equal(target.QualifiedName, results[0].GetProperty("qualifiedName").GetString());
    }

    [Fact]
    public async Task GetTests_ReturnsTests_FromSqliteStore()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"code-index-cli-tests-links-{Guid.NewGuid():N}.db");
        var productionFile = new global::CodeIndex.Core.FileRecord("f:Greeter.cs", "Greeter.cs", "Sample", "C#", "sha256:prod", "summary");
        var testFile = new global::CodeIndex.Core.FileRecord("f:GreeterTests.cs", "GreeterTests.cs", "Sample.Tests", "C#", "sha256:test", "summary");
        var target = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.FriendlyGreeter.CreateGreeting(System.String)", "CreateGreeting", "Sample.FriendlyGreeter.CreateGreeting(System.String)", "method", productionFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 2, 2), "string Sample.FriendlyGreeter.CreateGreeting(string)", "summary", "s:T:Sample.FriendlyGreeter", "public", false, false, false, false);
        var testMethod = new global::CodeIndex.Core.SymbolRecord("s:M:Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting", "CreateGreeting_ReturnsGreeting", "Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting", "method", testFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 3, 2), "void Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting()", "summary", "s:T:Sample.Tests.FriendlyGreeterTests", "public", false, false, false, false);
        var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "sample", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.SolutionPath, "solution"),
            [productionFile, testFile],
            [target, testMethod],
            [new global::CodeIndex.Core.EdgeRecord(global::CodeIndex.Core.EdgeTypes.Tests, testMethod.Id, target.Id)],
            Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());

        try
        {
            var sqliteStore = new global::CodeIndex.Core.SqliteCodeIndexStore();
            await sqliteStore.WriteAsync(databasePath, snapshot);

            var output = await fixture.RunCliAsync(
                "get-tests",
                target.QualifiedName,
                "--db",
                databasePath);

            using var document = JsonDocument.Parse(output);
            var results = document.RootElement.EnumerateArray().ToArray();

            Assert.Single(results);
            Assert.Equal("CreateGreeting_ReturnsGreeting", results[0].GetProperty("name").GetString());
            Assert.Equal(testMethod.QualifiedName, results[0].GetProperty("qualifiedName").GetString());
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
    public async Task SemanticSearch_ReturnsRankedSymbols_FromIndexSnapshot()
    {
        var file = new global::CodeIndex.Core.FileRecord("f:Inspector.cs", "Inspector.cs", "Sample", "C#", "sha256:file", "workspace inspector utilities");
        var symbol = new global::CodeIndex.Core.SymbolRecord("s:T:Sample.WorkspaceInspector", "WorkspaceInspector", "Sample.WorkspaceInspector", "class", file.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 10, 1), "class Sample.WorkspaceInspector", "inspects workspaces and symbols", null, "public", false, false, false, false);
        var embeddings = new global::CodeIndex.Core.SemanticEmbeddingIndexBuilder().Build([file], [symbol]);
        var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "sample", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.SolutionPath, "solution"),
            [file],
            [symbol],
            Array.Empty<global::CodeIndex.Core.EdgeRecord>(),
            Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            embeddings);
        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(snapshot)
        };

        var output = await fixture.RunCliAsync(
            runtime,
            "semantic-search",
            "workspace inspector",
            "--index",
            Path.Combine(fixture.RepoRoot, "unused-index"),
            "--type",
            "symbol",
            "--limit",
            "1");

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.Single(results);
        Assert.Equal("symbol", results[0].GetProperty("itemType").GetString());
        Assert.Equal(symbol.QualifiedName, results[0].GetProperty("qualifiedName").GetString());
    }

    [Fact]
    public async Task SemanticSearch_ReturnsRankedFiles_FromSqliteStore()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"code-index-cli-semantic-{Guid.NewGuid():N}.db");
        var file = new global::CodeIndex.Core.FileRecord("f:FriendlyGreeter.cs", "FriendlyGreeter.cs", "Sample", "C#", "sha256:file", "friendly greeter implementation");
        var symbol = new global::CodeIndex.Core.SymbolRecord("s:T:Sample.FriendlyGreeter", "FriendlyGreeter", "Sample.FriendlyGreeter", "class", file.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 10, 1), "class Sample.FriendlyGreeter", "creates friendly greetings", null, "public", false, false, false, false);
        var embeddings = new global::CodeIndex.Core.SemanticEmbeddingIndexBuilder().Build([file], [symbol]);
        var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "sample", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.SolutionPath, "solution"),
            [file],
            [symbol],
            Array.Empty<global::CodeIndex.Core.EdgeRecord>(),
            Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            embeddings);

        try
        {
            var sqliteStore = new global::CodeIndex.Core.SqliteCodeIndexStore();
            await sqliteStore.WriteAsync(databasePath, snapshot);

            var output = await fixture.RunCliAsync(
                "semantic-search",
                "friendly greeter implementation",
                "--db",
                databasePath,
                "--type",
                "file",
                "--limit",
                "1");

            using var document = JsonDocument.Parse(output);
            var results = document.RootElement.EnumerateArray().ToArray();

            Assert.Single(results);
            Assert.Equal("file", results[0].GetProperty("itemType").GetString());
            Assert.Equal(file.Path, results[0].GetProperty("path").GetString());
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
        Assert.True(root.GetProperty("rawSource").GetProperty("elapsedMs").GetInt64() >= 0);
        Assert.True(root.GetProperty("rawSource").GetProperty("totalBytes").GetInt64() > 0);
        Assert.True(root.GetProperty("rawSource").GetProperty("totalEstimatedTokens").GetInt32() > 0);
        Assert.True(root.GetProperty("indexArtifacts").GetProperty("totalBytes").GetInt64() > 0);
        Assert.True(root.GetProperty("indexArtifacts").GetProperty("referencesBytes").GetInt64() >= 0);
        Assert.True(root.GetProperty("indexArtifacts").GetProperty("embeddingsBytes").GetInt64() >= 0);
        Assert.True(root.GetProperty("indexArtifacts").GetProperty("totalEstimatedTokens").GetInt32() > 0);
        Assert.Equal(symbolQuery, root.GetProperty("symbolQuery").GetProperty("query").GetString());
        Assert.Equal(selectedQualifiedName, root.GetProperty("symbolQuery").GetProperty("selectedQualifiedName").GetString());
        Assert.True(root.GetProperty("symbolQuery").GetProperty("elapsedMs").GetInt64() >= 0);
        Assert.True(root.GetProperty("symbolQuery").GetProperty("findSymbolEstimatedTokens").GetInt32() > 0);
        Assert.Equal(filePath, root.GetProperty("excerptQuery").GetProperty("file").GetString());
        Assert.True(root.GetProperty("excerptQuery").GetProperty("elapsedMs").GetInt64() >= 0);
        Assert.True(root.GetProperty("excerptQuery").GetProperty("excerptBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("excerptQuery").GetProperty("excerptEstimatedTokens").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstFlowBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstFlowEstimatedTokens").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstFlowElapsedMs").GetInt64() >= 0);
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
        Assert.True(root.GetProperty("database").GetProperty("loadElapsedMs").GetInt64() >= 0);
        Assert.True(root.GetProperty("rawSource").GetProperty("totalEstimatedTokens").GetInt32() > 0);
        Assert.Equal(symbolQuery, root.GetProperty("symbolQuery").GetProperty("query").GetString());
        Assert.Equal(selectedQualifiedName, root.GetProperty("symbolQuery").GetProperty("selectedQualifiedName").GetString());
        Assert.True(root.GetProperty("symbolQuery").GetProperty("elapsedMs").GetInt64() >= 0);
        Assert.Equal(filePath, root.GetProperty("excerptQuery").GetProperty("file").GetString());
        Assert.True(root.GetProperty("indexFirstFlowBytes").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstFlowEstimatedTokens").GetInt32() > 0);
        Assert.True(root.GetProperty("indexFirstFlowElapsedMs").GetInt64() >= 0);
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
            BuildEdgesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.EdgeRecord>>(Array.Empty<global::CodeIndex.Core.EdgeRecord>()),
            BuildReferencesAsync = (_, _, _, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.ReferenceRecord>>(Array.Empty<global::CodeIndex.Core.ReferenceRecord>())
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
    public async Task FindReferences_UsingRepositorySolutionIndex_ReturnsReferenceLocations()
    {
        var output = await fixture.RunCliAsync(
            "find-references",
            "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder",
            "--index",
            await fixture.GetSolutionIndexDirectoryAsync(),
            "--limit",
            "5");

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal("CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder", result.GetProperty("targetQualifiedName").GetString()));
        Assert.Contains(results, result => result.GetProperty("file").GetString()!.Contains("WorkspaceIndexBuildersTests.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindReferences_UsingRepositorySolutionDatabase_ReturnsReferenceLocations()
    {
        var output = await fixture.RunCliAsync(
            "find-references",
            "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder",
            "--db",
            await fixture.GetSolutionDatabasePathAsync(),
            "--limit",
            "5");

        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal("CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder", result.GetProperty("targetQualifiedName").GetString()));
        Assert.Contains(results, result => result.GetProperty("lineText")!.GetString()!.Contains("WorkspaceSymbolIndexBuilder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Build_WithIncrementalFromIndex_AndUnchangedFiles_ReusesSymbolsEdgesAndReferences()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-cli-tests-{Guid.NewGuid():N}");
        var file = new global::CodeIndex.Core.FileRecord("f:WorkspaceInspection.cs", "WorkspaceInspection.cs", "CodeIndex.Roslyn", "C#", "sha256:test", "summary");
        var symbol = new global::CodeIndex.Core.SymbolRecord("s:T:CodeIndex.Roslyn.WorkspaceInspector", "WorkspaceInspector", "CodeIndex.Roslyn.WorkspaceInspector", "class", file.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "class CodeIndex.Roslyn.WorkspaceInspector", "summary", null, "public", false, false, false, false);
        var reference = new global::CodeIndex.Core.ReferenceRecord(symbol.Id, symbol.Id, file.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "WorkspaceInspector");
        var baseline = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "code-index", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.RoslynProjectPath, "project"),
            [file],
            [symbol],
            Array.Empty<global::CodeIndex.Core.EdgeRecord>(),
            [reference],
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(baseline),
            BuildFilesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.FileRecord>>([file]),
            BuildSymbolsAsync = (_, _, _, _) => throw new InvalidOperationException("BuildSymbolsAsync should not run when the incremental baseline is unchanged."),
            BuildEdgesAsync = (_, _, _) => throw new InvalidOperationException("BuildEdgesAsync should not run when the incremental baseline is unchanged."),
            BuildReferencesAsync = (_, _, _, _, _) => throw new InvalidOperationException("BuildReferencesAsync should not run when the incremental baseline is unchanged.")
        };

        try
        {
            var output = await fixture.RunCliAsync(
                runtime,
                "build",
                fixture.RoslynProjectPath,
                "--out",
                outputDirectory,
                "--incremental-from-index",
                Path.Combine(fixture.RepoRoot, "baseline-index"),
                "--verbose");

            Assert.Contains("No file changes detected against the incremental baseline. Reusing symbols, edges, references, and embeddings.", output);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = document.RootElement.EnumerateArray().ToArray();

            Assert.Single(symbols);
            Assert.Equal("CodeIndex.Roslyn.WorkspaceInspector", symbols[0].GetProperty("qualifiedName").GetString());

            using var referencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.references.json")));
            Assert.Single(referencesDocument.RootElement.EnumerateArray());
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
    public async Task Build_WithIncrementalFromDb_AndUnchangedFiles_ReusesSymbolsEdgesAndReferences()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-cli-tests-{Guid.NewGuid():N}");
        var file = new global::CodeIndex.Core.FileRecord("f:WorkspaceInspection.cs", "WorkspaceInspection.cs", "CodeIndex.Roslyn", "C#", "sha256:test", "summary");
        var symbol = new global::CodeIndex.Core.SymbolRecord("s:T:CodeIndex.Roslyn.WorkspaceInspector", "WorkspaceInspector", "CodeIndex.Roslyn.WorkspaceInspector", "class", file.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "class CodeIndex.Roslyn.WorkspaceInspector", "summary", null, "public", false, false, false, false);
        var reference = new global::CodeIndex.Core.ReferenceRecord(symbol.Id, symbol.Id, file.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "WorkspaceInspector");
        var baseline = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "code-index", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.RoslynProjectPath, "project"),
            [file],
            [symbol],
            Array.Empty<global::CodeIndex.Core.EdgeRecord>(),
            [reference],
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadDatabaseSnapshotAsync = (_, _) => Task.FromResult(baseline),
            BuildFilesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.FileRecord>>([file]),
            BuildSymbolsAsync = (_, _, _, _) => throw new InvalidOperationException("BuildSymbolsAsync should not run when the incremental database baseline is unchanged."),
            BuildEdgesAsync = (_, _, _) => throw new InvalidOperationException("BuildEdgesAsync should not run when the incremental database baseline is unchanged."),
            BuildReferencesAsync = (_, _, _, _, _) => throw new InvalidOperationException("BuildReferencesAsync should not run when the incremental database baseline is unchanged.")
        };

        try
        {
            var output = await fixture.RunCliAsync(
                runtime,
                "build",
                fixture.RoslynProjectPath,
                "--out",
                outputDirectory,
                "--incremental-from-db",
                Path.Combine(fixture.RepoRoot, "baseline.db"),
                "--verbose");

            Assert.Contains("No file changes detected against the incremental baseline. Reusing symbols, edges, references, and embeddings.", output);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = document.RootElement.EnumerateArray().ToArray();

            Assert.Single(symbols);
            Assert.Equal("CodeIndex.Roslyn.WorkspaceInspector", symbols[0].GetProperty("qualifiedName").GetString());
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
    public async Task Build_WithIncrementalFromIndex_AndChangedFiles_MergesRebuiltSymbols_RecomputesImpactedEdges_AndRefreshesReferences()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-cli-tests-{Guid.NewGuid():N}");
        var changedFile = new global::CodeIndex.Core.FileRecord("f:A.cs", "A.cs", "CodeIndex.Core", "C#", "sha256:new", "summary");
        var unchangedFile = new global::CodeIndex.Core.FileRecord("f:B.cs", "B.cs", "CodeIndex.Core", "C#", "sha256:stable", "summary");
        var oldBase = new global::CodeIndex.Core.SymbolRecord("s:T:OldBase", "OldBase", "Example.OldBase", "class", changedFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "class Example.OldBase", "summary", null, "public", false, false, false, false);
        var derived = new global::CodeIndex.Core.SymbolRecord("s:T:Derived", "Derived", "Example.Derived", "class", unchangedFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "class Example.Derived", "summary", null, "public", false, false, false, false);
        var newBase = new global::CodeIndex.Core.SymbolRecord("s:T:NewBase", "NewBase", "Example.NewBase", "class", changedFile.Id, new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 10), "class Example.NewBase", "summary", null, "public", false, false, false, false);
        var baseline = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta("1.0", "test", "code-index", DateTimeOffset.UtcNow, fixture.RepoRoot, fixture.RoslynProjectPath, "project"),
            [
                new global::CodeIndex.Core.FileRecord(changedFile.Id, changedFile.Path, changedFile.ProjectName, changedFile.Language, "sha256:old", changedFile.Summary),
                unchangedFile
            ],
            [oldBase, derived],
            [new global::CodeIndex.Core.EdgeRecord(global::CodeIndex.Core.EdgeTypes.Inherits, derived.Id, oldBase.Id)],
            [new global::CodeIndex.Core.ReferenceRecord(oldBase.Id, derived.Id, unchangedFile.Id, new global::CodeIndex.Core.TextRangeRecord(2, 5, 2, 12), "class Derived : OldBase")],
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(baseline),
            BuildFilesAsync = (_, _, _) => Task.FromResult<IReadOnlyList<global::CodeIndex.Core.FileRecord>>([changedFile, unchangedFile]),
            BuildSymbolsAsync = (_, _, _, _) => throw new InvalidOperationException("Full symbol rebuild should not run for incremental changes."),
            BuildEdgesAsync = (_, _, _) => throw new InvalidOperationException("Full edge rebuild should not run for incremental changes."),
            BuildSymbolsForFilesAsync = (_, allFiles, indexedFilePaths, _, _) =>
            {
                Assert.Equal(2, allFiles.Count);
                Assert.Equal([changedFile.Path], indexedFilePaths);
                return Task.FromResult<IReadOnlyList<global::CodeIndex.Core.SymbolRecord>>([newBase]);
            },
            BuildEdgesForFilesAsync = (_, indexedFilePaths, knownSymbolIds, _, _) =>
            {
                Assert.Equal(2, indexedFilePaths.Count);
                Assert.Contains(changedFile.Path, indexedFilePaths);
                Assert.Contains(unchangedFile.Path, indexedFilePaths);
                Assert.Contains(newBase.Id, knownSymbolIds);
                Assert.Contains(derived.Id, knownSymbolIds);
                return Task.FromResult<IReadOnlyList<global::CodeIndex.Core.EdgeRecord>>([
                    new global::CodeIndex.Core.EdgeRecord(global::CodeIndex.Core.EdgeTypes.Inherits, derived.Id, newBase.Id)
                ]);
            },
            BuildReferencesForFilesAsync = (_, allFiles, allSymbols, indexedFilePaths, _, _) =>
            {
                Assert.Equal(2, allFiles.Count);
                Assert.Equal(2, allSymbols.Count);
                Assert.Equal(2, indexedFilePaths.Count);
                Assert.Contains(changedFile.Path, indexedFilePaths);
                Assert.Contains(unchangedFile.Path, indexedFilePaths);
                return Task.FromResult<IReadOnlyList<global::CodeIndex.Core.ReferenceRecord>>([
                    new global::CodeIndex.Core.ReferenceRecord(newBase.Id, derived.Id, unchangedFile.Id, new global::CodeIndex.Core.TextRangeRecord(2, 5, 2, 12), "class Derived : NewBase")
                ]);
            }
        };

        try
        {
            var output = await fixture.RunCliAsync(
                runtime,
                "build",
                fixture.RoslynProjectPath,
                "--out",
                outputDirectory,
                "--incremental-from-index",
                Path.Combine(fixture.RepoRoot, "baseline-index"),
                "--verbose");

            Assert.Contains("Detected 1 changed files and 0 removed files against the incremental baseline.", output);
            Assert.Contains("Rebuilt 1 symbols across 1 changed files.", output);
            Assert.Contains("Rebuilt 1 edges across 2 impacted files.", output);
            Assert.Contains("Rebuilt 1 references across 2 impacted files.", output);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            Assert.Equal(2, symbols.Length);
            Assert.DoesNotContain(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "Example.OldBase");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "Example.NewBase");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "Example.Derived");

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();

            Assert.Single(edges);
            Assert.Equal(newBase.Id, edges[0].GetProperty("to").GetString());

            using var referencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.references.json")));
            var references = referencesDocument.RootElement.EnumerateArray().ToArray();

            Assert.Single(references);
            Assert.Equal(newBase.Id, references[0].GetProperty("targetSymbolId").GetString());
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
    public async Task Build_WithBothIncrementalSources_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync(
            "build",
            fixture.RoslynProjectPath,
            "--out",
            Path.Combine(Path.GetTempPath(), $"code-index-cli-tests-{Guid.NewGuid():N}"),
            "--incremental-from-index",
            Path.Combine(fixture.RepoRoot, "baseline-index"),
            "--incremental-from-db",
            Path.Combine(fixture.RepoRoot, "baseline.db"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Pass either --incremental-from-index <path> or --incremental-from-db <path>, but not both.", result.StandardError);
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
            var referenceBuilder = new global::CodeIndex.Roslyn.WorkspaceReferenceIndexBuilder();
            var embeddingBuilder = new global::CodeIndex.Core.SemanticEmbeddingIndexBuilder();
            var files = await fileBuilder.BuildAsync(SolutionPath);
            var symbols = await symbolBuilder.BuildAsync(SolutionPath, files);
            var edges = await edgeBuilder.BuildAsync(SolutionPath);
            var references = await referenceBuilder.BuildAsync(SolutionPath, files, symbols);
            var embeddings = embeddingBuilder.Build(files, symbols);
            var meta = global::CodeIndex.Core.CodeIndexMetaFactory.Create(SolutionPath, "solution");
            var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(meta, files, symbols, edges, references, embeddings);
            var validator = new global::CodeIndex.Core.CodeIndexValidator();
            validator.ValidateOrThrow(snapshot);

            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.meta.json"), meta);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.files.json"), files);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.symbols.json"), symbols);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.edges.json"), edges);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.references.json"), references);
            await global::CodeIndex.Core.CodeIndexJson.WriteToFileAsync(Path.Combine(solutionIndexDirectory, "code-index.embeddings.json"), embeddings);

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
            var referenceBuilder = new global::CodeIndex.Roslyn.WorkspaceReferenceIndexBuilder();
            var embeddingBuilder = new global::CodeIndex.Core.SemanticEmbeddingIndexBuilder();
            var files = await fileBuilder.BuildAsync(SolutionPath);
            var symbols = await symbolBuilder.BuildAsync(SolutionPath, files);
            var edges = await edgeBuilder.BuildAsync(SolutionPath);
            var references = await referenceBuilder.BuildAsync(SolutionPath, files, symbols);
            var embeddings = embeddingBuilder.Build(files, symbols);
            var meta = global::CodeIndex.Core.CodeIndexMetaFactory.Create(SolutionPath, "solution");
            var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(meta, files, symbols, edges, references, embeddings);
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