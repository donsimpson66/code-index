using System.Text.Json;
using ModelContextProtocol.Client;

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
        }
    };

    [Fact]
    public async Task McpServer_ListsTools_AndFindsSymbols() 
    {
        await using var client = await CreateMcpClientAsync();

        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name == "build_index");
        Assert.Contains(tools, tool => tool.Name == "find_symbol");

        var result = await client.CallToolAsync(
            "find_symbol",
            new Dictionary<string, object?>
            {
                ["query"] = "WorkspaceSymbolIndexBuilder",
                ["indexDirectory"] = fixture.IndexDirectory,
                ["limit"] = 5
            });

        Assert.False(result.IsError ?? false);
        Assert.NotNull(result.StructuredContent);

        var symbols = ExtractStructuredArray(result.StructuredContent.Value);

        Assert.Contains(
            symbols.EnumerateArray(),
            symbol => symbol.GetProperty("qualifiedName").GetString() == "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder");
    }

    [Fact]
    public async Task McpServer_BuildIndex_UsesDefaultWorkspaceArtifactDirectory()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-mcp-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "greeter.ts"),
                "export class Greeter {\n    greet(): string {\n        return 'hi';\n    }\n}\n");

            await using var client = await CreateMcpClientAsync();

            var result = await client.CallToolAsync(
                "build_index",
                new Dictionary<string, object?>
                {
                    ["path"] = sourceDirectory
                });

            Assert.False(result.IsError ?? false);
            Assert.NotNull(result.StructuredContent);

            var buildResult = ExtractStructuredObject(result.StructuredContent.Value, "outputDirectory");
            var outputDirectory = buildResult.GetProperty("outputDirectory").GetString();
            var metaPath = buildResult.GetProperty("metaPath").GetString();

            Assert.Equal(Path.Combine(sourceDirectory, ".code-index"), outputDirectory);
            Assert.Equal("directory", buildResult.GetProperty("inputKind").GetString());
            Assert.True(File.Exists(metaPath));
            Assert.True(File.Exists(Path.Combine(outputDirectory!, "code-index.symbols.json")));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task McpServer_LargeResultQueries_HonorPredictableLimits_OnBuiltWorkspace()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-mcp-large-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "ui"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "helper.ts"),
                "export class Helper {\n    log(): string {\n        return 'hi';\n    }\n}\n\nexport function boot(): Helper {\n    return new Helper();\n}\n");

            for (var index = 1; index <= 12; index++)
            {
                var suffix = index.ToString("00");
                await File.WriteAllTextAsync(
                    Path.Combine(sourceDirectory, "src", "ui", $"greeter{suffix}.ts"),
                    $"import {{ boot }} from './helper';\n\nexport class Greeter{suffix} {{\n    greet(): string {{\n        return boot().log();\n    }}\n}}\n");
            }

            await using var client = await CreateMcpClientAsync();

            var build = await client.CallToolAsync(
                "build_index",
                new Dictionary<string, object?>
                {
                    ["path"] = sourceDirectory
                });

            Assert.False(build.IsError ?? false);
            Assert.NotNull(build.StructuredContent);

            var buildResult = ExtractStructuredObject(build.StructuredContent.Value, "outputDirectory");
            var indexDirectory = buildResult.GetProperty("outputDirectory").GetString();
            Assert.NotNull(indexDirectory);

            var findSymbol = await client.CallToolAsync(
                "find_symbol",
                new Dictionary<string, object?>
                {
                    ["query"] = "Greeter",
                    ["indexDirectory"] = indexDirectory,
                    ["kind"] = "class",
                    ["sort"] = "name",
                    ["limit"] = 5
                });

            Assert.False(findSymbol.IsError ?? false);
            Assert.NotNull(findSymbol.StructuredContent);

            var symbolResults = ExtractStructuredArray(findSymbol.StructuredContent.Value).EnumerateArray().ToArray();
            Assert.Equal(5, symbolResults.Length);
            Assert.Equal("ui.greeter01.Greeter01", symbolResults[0].GetProperty("qualifiedName").GetString());
            Assert.Equal("ui.greeter05.Greeter05", symbolResults[4].GetProperty("qualifiedName").GetString());

            var findReferences = await client.CallToolAsync(
                "find_references",
                new Dictionary<string, object?>
                {
                    ["query"] = "ui.helper.boot",
                    ["indexDirectory"] = indexDirectory,
                    ["limit"] = 5
                });

            Assert.False(findReferences.IsError ?? false);
            Assert.NotNull(findReferences.StructuredContent);

            var referenceResults = ExtractStructuredArray(findReferences.StructuredContent.Value).EnumerateArray().ToArray();
            Assert.Equal(5, referenceResults.Length);
            Assert.Equal("src/ui/greeter01.ts", referenceResults[0].GetProperty("file").GetString());
            Assert.Equal("src/ui/greeter05.ts", referenceResults[4].GetProperty("file").GetString());

            var semanticSearch = await client.CallToolAsync(
                "semantic_search",
                new Dictionary<string, object?>
                {
                    ["query"] = "ui",
                    ["indexDirectory"] = indexDirectory,
                    ["itemType"] = "file"
                });

            Assert.False(semanticSearch.IsError ?? false);
            Assert.NotNull(semanticSearch.StructuredContent);

            var semanticResults = ExtractStructuredArray(semanticSearch.StructuredContent.Value).EnumerateArray().ToArray();
            Assert.Equal(10, semanticResults.Length);
            Assert.All(semanticResults, result => Assert.Equal("file", result.GetProperty("itemType").GetString()));
            Assert.All(semanticResults, result => Assert.Contains("src/ui/", result.GetProperty("path").GetString()!, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task McpServer_FindSymbol_AllowsRelativeIndexDirectory()
    {
        await using var client = await CreateMcpClientAsync();

        var result = await client.CallToolAsync(
            "find_symbol",
            new Dictionary<string, object?>
            {
                ["query"] = "WorkspaceSymbolIndexBuilder",
                ["indexDirectory"] = Path.GetRelativePath(fixture.RepoRoot, fixture.IndexDirectory),
                ["limit"] = 5
            });

        Assert.False(result.IsError ?? false);
        Assert.NotNull(result.StructuredContent);

        var symbols = ExtractStructuredArray(result.StructuredContent.Value);

        Assert.Contains(
            symbols.EnumerateArray(),
            symbol => symbol.GetProperty("qualifiedName").GetString() == "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder");
    }

    [Fact]
    public async Task McpServer_BuildIndex_ReturnsCleanError_ForMissingSourcePath()
    {
        await using var client = await CreateMcpClientAsync();

        var result = await client.CallToolAsync(
            "build_index",
            new Dictionary<string, object?>
            {
                ["path"] = "does-not-exist/code-index.sln"
            });

        Assert.True(result.IsError ?? false);
    }

    [Fact]
    public async Task McpServer_GetSymbol_ReturnsCleanError_ForMissingIndexDirectory()
    {
        await using var client = await CreateMcpClientAsync();

        var result = await client.CallToolAsync(
            "get_symbol",
            new Dictionary<string, object?>
            {
                ["query"] = "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder",
                ["indexDirectory"] = "missing-index"
            });

        Assert.True(result.IsError ?? false);
    }

    [Fact]
    public async Task McpServer_GetSymbol_ReturnsCleanError_ForUnknownSymbol()
    {
        await using var client = await CreateMcpClientAsync();

        var result = await client.CallToolAsync(
            "get_symbol",
            new Dictionary<string, object?>
            {
                ["query"] = "CodeIndex.UnknownSymbol",
                ["indexDirectory"] = fixture.IndexDirectory
            });

        Assert.True(result.IsError ?? false);
    }

    [Fact]
    public async Task McpServer_GetExcerpt_ReturnsCleanError_ForInvalidLineRange()
    {
        await using var client = await CreateMcpClientAsync();

        var result = await client.CallToolAsync(
            "get_excerpt",
            new Dictionary<string, object?>
            {
                ["filePath"] = "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs",
                ["indexDirectory"] = fixture.IndexDirectory,
                ["startLine"] = 0,
                ["endLine"] = 1
            });

        Assert.True(result.IsError ?? false);
    }

    [Fact]
    public async Task McpServer_RemainsUsable_AfterToolFailure_AndKeepsDiagnosticsOffProtocol()
    {
        var standardErrorLines = new List<string>();
        await using var client = await CreateMcpClientAsync(standardErrorLines);

        var failure = await client.CallToolAsync(
            "get_symbol",
            new Dictionary<string, object?>
            {
                ["query"] = "CodeIndex.UnknownSymbol",
                ["indexDirectory"] = fixture.IndexDirectory
            });

        Assert.True(failure.IsError ?? false);

        var success = await client.CallToolAsync(
            "find_symbol",
            new Dictionary<string, object?>
            {
                ["query"] = "WorkspaceSymbolIndexBuilder",
                ["indexDirectory"] = fixture.IndexDirectory,
                ["limit"] = 5
            });

        Assert.False(success.IsError ?? false);
        Assert.NotNull(success.StructuredContent);

        var symbols = ExtractStructuredArray(success.StructuredContent.Value);
        Assert.Contains(
            symbols.EnumerateArray(),
            symbol => symbol.GetProperty("qualifiedName").GetString() == "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder");

        Assert.DoesNotContain(standardErrorLines, line => line.Contains("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(standardErrorLines, line => line.Contains("\"jsonrpc\"", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildService_StopsWhenCanceledAfterFilesAreLoaded()
    {
        var buildSymbolsCallCount = 0;
        var buildEdgesCallCount = 0;
        var buildReferencesCallCount = 0;
        using var cancellationTokenSource = new CancellationTokenSource();

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            BuildFilesAsync = (_, _, _) =>
            {
                cancellationTokenSource.Cancel();
                return Task.FromResult<IReadOnlyList<global::CodeIndex.Core.FileRecord>>(
                [
                    new global::CodeIndex.Core.FileRecord("f:sample.ts", "sample.ts", "sample", "TypeScript", "sha256:test", "summary")
                ]);
            },
            BuildSymbolsAsync = (_, _, _, _) =>
            {
                buildSymbolsCallCount++;
                return Task.FromResult<IReadOnlyList<global::CodeIndex.Core.SymbolRecord>>(Array.Empty<global::CodeIndex.Core.SymbolRecord>());
            },
            BuildEdgesAsync = (_, _, _) =>
            {
                buildEdgesCallCount++;
                return Task.FromResult<IReadOnlyList<global::CodeIndex.Core.EdgeRecord>>(Array.Empty<global::CodeIndex.Core.EdgeRecord>());
            },
            BuildReferencesAsync = (_, _, _, _, _) =>
            {
                buildReferencesCallCount++;
                return Task.FromResult<IReadOnlyList<global::CodeIndex.Core.ReferenceRecord>>(Array.Empty<global::CodeIndex.Core.ReferenceRecord>());
            },
            ValidateSnapshot = _ => throw new Xunit.Sdk.XunitException("Validation should not run after cancellation.")
        };

        var service = new global::CodeIndex.Cli.CodeIndexBuildService(runtime);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.BuildAsync(new global::CodeIndex.Cli.CodeIndexBuildRequest(fixture.RepoRoot, false, null), cancellationTokenSource.Token));

        Assert.Equal(0, buildSymbolsCallCount);
        Assert.Equal(0, buildEdgesCallCount);
        Assert.Equal(0, buildReferencesCallCount);
    }

    [Fact]
    public async Task QueryService_StopsWhenCanceledAfterSnapshotLoads()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var snapshot = new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta(
                "1",
                "test",
                "code-index",
                DateTimeOffset.UtcNow,
                fixture.RepoRoot,
                fixture.RepoRoot,
                "directory"),
            Array.Empty<global::CodeIndex.Core.FileRecord>(),
            Array.Empty<global::CodeIndex.Core.SymbolRecord>(),
            Array.Empty<global::CodeIndex.Core.EdgeRecord>(),
            Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) =>
            {
                cancellationTokenSource.Cancel();
                return Task.FromResult(snapshot);
            }
        };

        var service = new global::CodeIndex.Cli.CodeIndexQueryService(runtime);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetSymbolAsync("CodeIndex.UnknownSymbol", fixture.IndexDirectory, cancellationTokenSource.Token));
    }

    [Fact]
    public async Task QueryService_FindSymbols_UsesPredictableOrdering_AndHonorsLimits()
    {
        var snapshot = CreateSnapshot(
            files:
            [
                CreateFileRecord("f:alpha", "src/Alpha.cs"),
                CreateFileRecord("f:beta", "src/Beta.cs")
            ],
            symbols:
            [
                CreateSymbolRecord("s:3", "WidgetHelper", "Demo.WidgetHelper", "class", "f:alpha"),
                CreateSymbolRecord("s:1", "Widget", "Demo.A.Widget", "class", "f:alpha"),
                CreateSymbolRecord("s:4", "WidgetTools", "Demo.WidgetTools", "class", "f:beta"),
                CreateSymbolRecord("s:2", "Widget", "Demo.B.Widget", "class", "f:beta")
            ]);

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(snapshot)
        };

        var service = new global::CodeIndex.Cli.CodeIndexQueryService(runtime);

        var results = await service.FindSymbolsAsync(
            new global::CodeIndex.Cli.CodeIndexFindSymbolsRequest("Widget", fixture.IndexDirectory, 2, null, null, "name"),
            CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("Demo.A.Widget", results[0].QualifiedName);
        Assert.Equal("Demo.B.Widget", results[1].QualifiedName);
    }

    [Fact]
    public async Task QueryService_FindReferences_UsesPredictableOrdering_AndHonorsLimits()
    {
        var snapshot = CreateSnapshot(
            files:
            [
                CreateFileRecord("f:zeta", "src/Zeta.cs"),
                CreateFileRecord("f:alpha", "src/Alpha.cs")
            ],
            symbols:
            [
                CreateSymbolRecord("s:target", "Target", "Demo.Target", "method", "f:alpha"),
                CreateSymbolRecord("s:caller1", "CallerOne", "Demo.CallerOne", "method", "f:alpha"),
                CreateSymbolRecord("s:caller2", "CallerTwo", "Demo.CallerTwo", "method", "f:zeta")
            ],
            references:
            [
                new global::CodeIndex.Core.ReferenceRecord("s:target", "s:caller2", "f:zeta", new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 8), "Target();"),
                new global::CodeIndex.Core.ReferenceRecord("s:target", "s:caller1", "f:alpha", new global::CodeIndex.Core.TextRangeRecord(10, 4, 10, 11), "Target();"),
                new global::CodeIndex.Core.ReferenceRecord("s:target", "s:caller1", "f:alpha", new global::CodeIndex.Core.TextRangeRecord(2, 2, 2, 9), "Target();"),
                new global::CodeIndex.Core.ReferenceRecord("s:target", "s:caller2", "f:zeta", new global::CodeIndex.Core.TextRangeRecord(3, 1, 3, 8), "Target();")
            ]);

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(snapshot)
        };

        var service = new global::CodeIndex.Cli.CodeIndexQueryService(runtime);

        var results = await service.FindReferencesAsync(
            new global::CodeIndex.Cli.CodeIndexReferenceQuery("Demo.Target", fixture.IndexDirectory, 2),
            CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("src/Alpha.cs", results[0].File);
        Assert.Equal(2, results[0].Range.StartLine);
        Assert.Equal("src/Alpha.cs", results[1].File);
        Assert.Equal(10, results[1].Range.StartLine);
    }

    [Fact]
    public async Task QueryService_SemanticSearch_DefaultsToTenResults_ForBroadMatches()
    {
        var files = Enumerable.Range(1, 12)
            .Select(index => CreateFileRecord($"f:{index:00}", "src/Greeter.cs", summary: "greeter summary"))
            .ToArray();
        var embeddingBuilder = new global::CodeIndex.Core.SemanticEmbeddingIndexBuilder();
        var snapshot = CreateSnapshot(
            files: files,
            embeddings: embeddingBuilder.Build(files, Array.Empty<global::CodeIndex.Core.SymbolRecord>()));

        var runtime = new global::CodeIndex.Cli.CliRuntime
        {
            ReadSnapshotAsync = (_, _) => Task.FromResult(snapshot)
        };

        var service = new global::CodeIndex.Cli.CodeIndexQueryService(runtime);

        var results = await service.SemanticSearchAsync(
            new global::CodeIndex.Cli.CodeIndexSemanticSearchRequest("greeter", fixture.IndexDirectory, 0, global::CodeIndex.Core.EmbeddingItemTypes.File),
            CancellationToken.None);

        Assert.Equal(10, results.Count);
        Assert.All(results, result => Assert.Equal(global::CodeIndex.Core.EmbeddingItemTypes.File, result.ItemType));
        Assert.Equal("f:01", results[0].ItemId);
        Assert.Equal("f:10", results[9].ItemId);
    }

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

    private static JsonElement ExtractStructuredArray(JsonElement structuredContent)
    {
        if (structuredContent.ValueKind == JsonValueKind.Array)
        {
            return structuredContent;
        }

        if (structuredContent.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in structuredContent.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected structuredContent to contain an array payload but got: {structuredContent.GetRawText()}");
    }

    private static JsonElement ExtractStructuredObject(JsonElement structuredContent, string requiredProperty)
    {
        if (structuredContent.ValueKind == JsonValueKind.Object && structuredContent.TryGetProperty(requiredProperty, out _))
        {
            return structuredContent;
        }

        if (structuredContent.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in structuredContent.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty(requiredProperty, out _))
                {
                    return property.Value;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected structuredContent to contain an object payload with property '{requiredProperty}' but got: {structuredContent.GetRawText()}");
    }

    private Task<McpClient> CreateMcpClientAsync(ICollection<string>? standardErrorLines = null)
    {
        return McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = [fixture.McpServerDllPath],
                WorkingDirectory = fixture.RepoRoot,
                Name = "code-index-mcp-test",
                StandardErrorLines = line =>
                {
                    if (standardErrorLines is not null)
                    {
                        standardErrorLines.Add(line);
                    }
                }
            }));
    }

    private global::CodeIndex.Core.CodeIndexSnapshot CreateSnapshot(
        IReadOnlyList<global::CodeIndex.Core.FileRecord>? files = null,
        IReadOnlyList<global::CodeIndex.Core.SymbolRecord>? symbols = null,
        IReadOnlyList<global::CodeIndex.Core.EdgeRecord>? edges = null,
        IReadOnlyList<global::CodeIndex.Core.ReferenceRecord>? references = null,
        IReadOnlyList<global::CodeIndex.Core.EmbeddingRecord>? embeddings = null)
    {
        return new global::CodeIndex.Core.CodeIndexSnapshot(
            new global::CodeIndex.Core.CodeIndexMeta(
                "1",
                "test",
                "code-index",
                DateTimeOffset.UtcNow,
                fixture.RepoRoot,
                fixture.RepoRoot,
                "directory"),
            files ?? Array.Empty<global::CodeIndex.Core.FileRecord>(),
            symbols ?? Array.Empty<global::CodeIndex.Core.SymbolRecord>(),
            edges ?? Array.Empty<global::CodeIndex.Core.EdgeRecord>(),
            references ?? Array.Empty<global::CodeIndex.Core.ReferenceRecord>(),
            embeddings ?? Array.Empty<global::CodeIndex.Core.EmbeddingRecord>());
    }

    private static global::CodeIndex.Core.FileRecord CreateFileRecord(
        string id,
        string path,
        string projectName = "CodeIndex.Tests",
        string language = "C#",
        string hash = "sha256:test",
        string summary = "summary")
    {
        return new global::CodeIndex.Core.FileRecord(id, path, projectName, language, hash, summary);
    }

    private static global::CodeIndex.Core.SymbolRecord CreateSymbolRecord(
        string id,
        string name,
        string qualifiedName,
        string kind,
        string fileId,
        string accessibility = "public")
    {
        return new global::CodeIndex.Core.SymbolRecord(
            id,
            name,
            qualifiedName,
            kind,
            fileId,
            new global::CodeIndex.Core.TextRangeRecord(1, 1, 1, 1),
            qualifiedName,
            "summary",
            null,
            accessibility,
            false,
            false,
            false,
            false);
    }

    [Fact]
    public async Task Build_WritesExpectedArtifacts_ForMixedLanguageDirectory()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-multi-lang-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-multi-lang-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src"));

        try
        {
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "src", "Greeter.java"), "package demo;\npublic class Greeter\n{\n    public void greet() {}\n}\n");
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "src", "greeter.go"), "package demo\n\ntype Greeter struct {}\n\nfunc (g *Greeter) Greet() {}\nfunc Run() {}\n");
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "src", "greeter.ts"), "export class Greeter {\n    greet(): void {}\n}\n\nexport function run() {}\n");
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "src", "greeter.py"), "class Greeter:\n    def __init__(self):\n        pass\n\n    def greet(self):\n        return 'hi'\n\ndef run():\n    return Greeter()\n");
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "src", "greeter.php"), "<?php\nnamespace Demo;\nclass Greeter\n{\n    public function greet() {}\n}\nfunction run() {}\n");

            var output = await fixture.RunCliAsync(
                "build",
                sourceDirectory,
                "--out",
                outputDirectory,
                "--verbose");

            Assert.Contains("Indexed 5 files.", output);
            Assert.Contains("Validation passed.", output);

            using var metaDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.meta.json")));
            Assert.Equal("directory", metaDocument.RootElement.GetProperty("inputKind").GetString());

            using var filesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.files.json")));
            Assert.Equal(5, filesDocument.RootElement.GetArrayLength());

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbolNames = symbolsDocument.RootElement
                .EnumerateArray()
                .Select(symbol => symbol.GetProperty("qualifiedName").GetString())
                .Where(name => name is not null)
                .ToArray();

            Assert.Contains("demo.Greeter", symbolNames);
            Assert.Contains("demo.Greeter.Greet", symbolNames);
            Assert.Contains("greeter.Greeter", symbolNames);
            Assert.Contains("greeter.Greeter.__init__", symbolNames);
            Assert.Contains("Demo.Greeter", symbolNames);

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            Assert.True(edgesDocument.RootElement.GetArrayLength() >= 4);
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_TightensTypeScriptPythonAndPhpSymbolFidelity_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-symbol-fidelity-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-symbol-fidelity-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "ui", "widgets"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "pkg", "services"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "Domain"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "widgets", "greeter.ts"),
                "export class Greeter {\n    private message: string;\n    constructor(message: string) {\n        this.message = message;\n    }\n\n    greet(): string {\n        return this.message;\n    }\n}\n\nexport const boot = async () => new Greeter('hi');\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "pkg", "services", "greeter.py"),
                "class Greeter:\n    def __init__(self):\n        self.message = 'hi'\n\n    async def greet(self):\n        return self.message\n\nasync def boot():\n    return Greeter()\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Domain", "Greeter.php"),
                "<?php\nnamespace Demo\\Domain;\nclass Greeter\n{\n    private string $message;\n\n    public function __construct() {}\n\n    public function greet() {}\n}\n");

            await fixture.RunCliAsync(
                "build",
                sourceDirectory,
                "--out",
                outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "ui.widgets.greeter.Greeter");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "ui.widgets.greeter" && symbol.GetProperty("kind").GetString() == "module");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "ui.widgets.greeter.Greeter.message" && symbol.GetProperty("kind").GetString() == "field" && symbol.GetProperty("accessibility").GetString() == "private");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "ui.widgets.greeter.boot" && symbol.GetProperty("kind").GetString() == "method");

            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "pkg.services.greeter" && symbol.GetProperty("kind").GetString() == "module");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "pkg.services.greeter.Greeter.__init__" && symbol.GetProperty("kind").GetString() == "constructor");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "pkg.services.greeter.Greeter.greet");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "pkg.services.greeter.boot");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "pkg.services.greeter.Greeter.message" && symbol.GetProperty("kind").GetString() == "property");

            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "Demo.Domain" && symbol.GetProperty("kind").GetString() == "namespace");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "Demo.Domain.Greeter");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "Demo.Domain.Greeter.message" && symbol.GetProperty("kind").GetString() == "field" && symbol.GetProperty("accessibility").GetString() == "private");
            Assert.Contains(symbols, symbol => symbol.GetProperty("qualifiedName").GetString() == "Demo.Domain.Greeter.__construct" && symbol.GetProperty("kind").GetString() == "constructor");
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_ExtractsJavaAndGoCallEdgesAndReferences_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-go-graph-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-go-graph-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.java"),
                "package demo;\npublic class Greeter\n{\n    public void greet()\n    {\n        helper();\n        new Greeter();\n    }\n\n    private void helper() {}\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "greeter.go"),
                "package demo\n\ntype Greeter struct{}\n\nfunc (g *Greeter) Greet() {\n    helper()\n    g.log()\n    _ = Greeter{}\n}\n\nfunc (g *Greeter) log() {}\n\nfunc helper() {}\n");

            await fixture.RunCliAsync(
                "build",
                sourceDirectory,
                "--out",
                outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();
            var javaFileId = "f:src/Greeter.java";
            var goFileId = "f:src/greeter.go";

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();

            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greeter.greet", javaFileId) && edge.GetProperty("to").GetString() == GetSymbolId("demo.Greeter.helper", javaFileId));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greeter.Greet", goFileId) && edge.GetProperty("to").GetString() == GetSymbolId("demo.helper", goFileId));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greeter.Greet", goFileId) && edge.GetProperty("to").GetString() == GetSymbolId("demo.Greeter.log", goFileId));

            using var referencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.references.json")));
            var references = referencesDocument.RootElement.EnumerateArray().ToArray();

            Assert.Contains(references, reference => reference.GetProperty("targetSymbolId").GetString() == GetSymbolId("demo.Greeter.helper", javaFileId) && reference.GetProperty("sourceSymbolId").GetString() == GetSymbolId("demo.Greeter.greet", javaFileId));
            Assert.Contains(references, reference => reference.GetProperty("targetSymbolId").GetString() == GetSymbolId("demo.Greeter", javaFileId) && reference.GetProperty("sourceSymbolId").GetString() == GetSymbolId("demo.Greeter.greet", javaFileId));
            Assert.Contains(references, reference => reference.GetProperty("targetSymbolId").GetString() == GetSymbolId("demo.Greeter.log", goFileId) && reference.GetProperty("sourceSymbolId").GetString() == GetSymbolId("demo.Greeter.Greet", goFileId));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_ResolvesJavaAndGoReferencesAcrossFiles_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-go-cross-file-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-go-cross-file-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Helpers.java"),
                "package demo;\npublic class Helpers\n{\n    public static void log() {}\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.java"),
                "package demo;\npublic class Greeter\n{\n    public void greet()\n    {\n        Helpers.log();\n    }\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "helpers.go"),
                "package demo\n\nfunc helper() {}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "greeter.go"),
                "package demo\n\ntype Greeter struct{}\n\nfunc (g *Greeter) Greet() {\n    helper()\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greeter.greet", "f:src/Greeter.java") && edge.GetProperty("to").GetString() == GetSymbolId("demo.Helpers.log", "f:src/Helpers.java"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greeter.Greet", "f:src/greeter.go") && edge.GetProperty("to").GetString() == GetSymbolId("demo.helper", "f:src/helpers.go"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_ResolvesJavaImportsAcrossPackages_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-imports-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-imports-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Helpers.java"),
                "package util;\npublic class Helpers\n{\n    public static void log() {}\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.java"),
                "package demo;\nimport util.Helpers;\npublic class Greeter\n{\n    public void greet()\n    {\n        Helpers.log();\n    }\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greeter.greet", "f:src/Greeter.java") && edge.GetProperty("to").GetString() == GetSymbolId("util.Helpers.log", "f:src/Helpers.java"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_ResolvesJavaStaticImportsAndGoImportedPackages_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-go-imports-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-java-go-imports-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "helper"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "demo"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Helpers.java"),
                "package util;\npublic class Helpers\n{\n    public static void log() {}\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.java"),
                "package demo;\nimport static util.Helpers.log;\npublic class Greeter\n{\n    public void greet()\n    {\n        log();\n    }\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "helper", "helper.go"),
                "package helper\n\nfunc Log() {}\n\ntype Greeter struct{}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "demo", "greeter.go"),
                "package demo\n\nimport helper \"example/helper\"\n\nfunc Greet() {\n    helper.Log()\n    _ = helper.Greeter{}\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greeter.greet", "f:src/Greeter.java") && edge.GetProperty("to").GetString() == GetSymbolId("util.Helpers.log", "f:src/Helpers.java"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("demo.Greet", "f:src/demo/greeter.go") && edge.GetProperty("to").GetString() == GetSymbolId("helper.Log", "f:src/helper/helper.go"));

            using var referencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.references.json")));
            var references = referencesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(references, reference => reference.GetProperty("targetSymbolId").GetString() == GetSymbolId("helper.Greeter", "f:src/helper/helper.go") && reference.GetProperty("sourceSymbolId").GetString() == GetSymbolId("demo.Greet", "f:src/demo/greeter.go"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_ExtractsTypeScriptCallEdgesAndReferences_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-typescript-graph-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-typescript-graph-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "ui"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "helper.ts"),
                "export class Helper {\n    log(): string {\n        return 'ok';\n    }\n}\n\nexport function boot(): Helper {\n    return new Helper();\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "greeter.ts"),
                "import { Helper, boot } from './helper';\n\nexport class Greeter {\n    private helper: Helper;\n\n    constructor() {\n        this.helper = boot();\n    }\n\n    greet(): string {\n        return this.helper.log();\n    }\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("ui.greeter.Greeter.constructor", "f:src/ui/greeter.ts") && edge.GetProperty("to").GetString() == GetSymbolId("ui.helper.boot", "f:src/ui/helper.ts"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("ui.greeter.Greeter.greet", "f:src/ui/greeter.ts") && edge.GetProperty("to").GetString() == GetSymbolId("ui.helper.Helper.log", "f:src/ui/helper.ts"));

            using var referencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.references.json")));
            var references = referencesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(references, reference => reference.GetProperty("targetSymbolId").GetString() == GetSymbolId("ui.helper.boot", "f:src/ui/helper.ts") && reference.GetProperty("sourceSymbolId").GetString() == GetSymbolId("ui.greeter.Greeter.constructor", "f:src/ui/greeter.ts"));
            Assert.Contains(references, reference => reference.GetProperty("targetSymbolId").GetString() == GetSymbolId("ui.helper.Helper.log", "f:src/ui/helper.ts") && reference.GetProperty("sourceSymbolId").GetString() == GetSymbolId("ui.greeter.Greeter.greet", "f:src/ui/greeter.ts"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_TracksTypeScriptLocalAndParameterTypes_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-typescript-types-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-typescript-types-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "ui"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "helper.ts"),
                "export class Helper {\n    log(): string {\n        return 'ok';\n    }\n}\n\nexport function boot(): Helper {\n    return new Helper();\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "greeter.ts"),
                "import { Helper, boot } from './helper';\n\nexport class Greeter {\n    greet(): string {\n        const helper: Helper = boot();\n        return helper.log();\n    }\n\n    render(helper: Helper): string {\n        return helper.log();\n    }\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("ui.greeter.Greeter.greet", "f:src/ui/greeter.ts") && edge.GetProperty("to").GetString() == GetSymbolId("ui.helper.Helper.log", "f:src/ui/helper.ts"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("ui.greeter.Greeter.render", "f:src/ui/greeter.ts") && edge.GetProperty("to").GetString() == GetSymbolId("ui.helper.Helper.log", "f:src/ui/helper.ts"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_TracksTypeScriptFlowAssignments_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-typescript-flow-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-typescript-flow-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "ui"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "helper.ts"),
                "export class Helper {\n    log(): string {\n        return 'ok';\n    }\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "greeter.ts"),
                "import { Helper } from './helper';\n\nexport class Greeter {\n    private helper;\n\n    constructor() {\n        this.helper = new Helper();\n    }\n\n    greet(): string {\n        const alias = this.helper;\n        return alias.log();\n    }\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("ui.greeter.Greeter.greet", "f:src/ui/greeter.ts") && edge.GetProperty("to").GetString() == GetSymbolId("ui.helper.Helper.log", "f:src/ui/helper.ts"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_ExtractsPythonAndPhpCallEdgesAndReferences_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-python-php-graph-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-python-php-graph-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "pkg"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "pkg", "helper.py"),
                "class Helper:\n    def log(self):\n        return 'ok'\n\ndef boot():\n    return Helper()\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "pkg", "greeter.py"),
                "from pkg.helper import Helper, boot\n\nclass Greeter:\n    def greet(self):\n        return boot()\n\n    def build(self):\n        return Helper()\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Helper.php"),
                "<?php\nnamespace Demo\\Support;\n\nclass Helper\n{\n    public static function log() {}\n}\n\nfunction boot() {}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.php"),
                "<?php\nnamespace Demo;\n\nuse Demo\\Support\\Helper;\nuse function Demo\\Support\\boot;\n\nclass Greeter\n{\n    public function greet()\n    {\n        boot();\n        Helper::log();\n        $this->format();\n    }\n\n    private function format()\n    {\n    }\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("pkg.greeter.Greeter.greet", "f:src/pkg/greeter.py") && edge.GetProperty("to").GetString() == GetSymbolId("pkg.helper.boot", "f:src/pkg/helper.py"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("Demo.Greeter.greet", "f:src/Greeter.php") && edge.GetProperty("to").GetString() == GetSymbolId("Demo.Support.boot", "f:src/Helper.php"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("Demo.Greeter.greet", "f:src/Greeter.php") && edge.GetProperty("to").GetString() == GetSymbolId("Demo.Support.Helper.log", "f:src/Helper.php"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("Demo.Greeter.greet", "f:src/Greeter.php") && edge.GetProperty("to").GetString() == GetSymbolId("Demo.Greeter.format", "f:src/Greeter.php"));

            using var referencesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.references.json")));
            var references = referencesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(references, reference => reference.GetProperty("targetSymbolId").GetString() == GetSymbolId("pkg.helper.Helper", "f:src/pkg/helper.py") && reference.GetProperty("sourceSymbolId").GetString() == GetSymbolId("pkg.greeter.Greeter.build", "f:src/pkg/greeter.py"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Build_TracksPythonAndPhpAssignedTypes_ForDirectoryInput()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"code-index-python-php-types-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-python-php-types-out-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "pkg"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "pkg", "helper.py"),
                "class Helper:\n    def log(self):\n        return 'ok'\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "pkg", "greeter.py"),
                "from pkg.helper import Helper\n\nclass Greeter:\n    def __init__(self):\n        self.helper = Helper()\n\n    def greet(self):\n        helper = self.helper\n        return helper.log()\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Helper.php"),
                "<?php\nnamespace Demo\\Support;\n\nclass Helper\n{\n    public function log() {}\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.php"),
                "<?php\nnamespace Demo;\n\nuse Demo\\Support\\Helper;\n\nclass Greeter\n{\n    private $helper;\n\n    public function __construct()\n    {\n        $this->helper = new Helper();\n    }\n\n    public function greet()\n    {\n        $helper = $this->helper;\n        $helper->log();\n    }\n}\n");

            await fixture.RunCliAsync("build", sourceDirectory, "--out", outputDirectory);

            using var symbolsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            var symbols = symbolsDocument.RootElement.EnumerateArray().ToArray();

            string GetSymbolId(string qualifiedName, string fileId)
            {
                return symbols
                    .Single(symbol =>
                        symbol.GetProperty("qualifiedName").GetString() == qualifiedName &&
                        symbol.GetProperty("fileId").GetString() == fileId)
                    .GetProperty("id")
                    .GetString()!;
            }

            using var edgesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.edges.json")));
            var edges = edgesDocument.RootElement.EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("pkg.greeter.Greeter.greet", "f:src/pkg/greeter.py") && edge.GetProperty("to").GetString() == GetSymbolId("pkg.helper.Helper.log", "f:src/pkg/helper.py"));
            Assert.Contains(edges, edge => edge.GetProperty("type").GetString() == "calls" && edge.GetProperty("from").GetString() == GetSymbolId("Demo.Greeter.greet", "f:src/Greeter.php") && edge.GetProperty("to").GetString() == GetSymbolId("Demo.Support.Helper.log", "f:src/Helper.php"));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

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
    public async Task FindSymbol_WithoutIndex_ReturnsClearError()
    {
        var result = await fixture.RunCliExpectFailureAsync("find-symbol", "WorkspaceInspector");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("An index directory is required. Pass --index <path>.", result.StandardError);
    }
}

public sealed class IndexFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim consoleLock = new(1, 1);
    private readonly SemaphoreSlim solutionIndexLock = new(1, 1);
    private string? solutionIndexDirectory;

    public string RepoRoot { get; } = FindRepoRoot();

    public string SolutionPath => Path.Combine(RepoRoot, "code-index.sln");

    public string SampleSolutionPath => Path.Combine(RepoRoot, "samples", "SampleSolution", "SampleSolution.sln");

    public string RoslynProjectPath => Path.Combine(RepoRoot, "src", "CodeIndex.Roslyn", "CodeIndex.Roslyn.csproj");

    public string IndexDirectory { get; } = Path.Combine(FindRepoRoot(), "artifacts", "code-index");

    public string McpServerDllPath
    {
        get
        {
            var debugPath = Path.Combine(RepoRoot, "src", "CodeIndex.Mcp", "bin", "Debug", "net10.0", "CodeIndex.Mcp.dll");
            if (File.Exists(debugPath))
            {
                return debugPath;
            }

            var releasePath = Path.Combine(RepoRoot, "src", "CodeIndex.Mcp", "bin", "Release", "net10.0", "CodeIndex.Mcp.dll");
            if (File.Exists(releasePath))
            {
                return releasePath;
            }

            throw new InvalidOperationException("Could not locate the built CodeIndex.Mcp server assembly.");
        }
    }

    public Task InitializeAsync()
    {
        if (!Directory.Exists(IndexDirectory))
        {
            throw new InvalidOperationException($"Index directory not found at {IndexDirectory}");
        }

        _ = McpServerDllPath;

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        consoleLock.Dispose();
        solutionIndexLock.Dispose();

        if (!string.IsNullOrWhiteSpace(solutionIndexDirectory) && Directory.Exists(solutionIndexDirectory))
        {
            Directory.Delete(solutionIndexDirectory, recursive: true);
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