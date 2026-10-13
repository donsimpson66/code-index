using System.Text.Json;

namespace CodeIndex.Cli.Tests;

public sealed class TokenEfficiencyTests(IndexFixture fixture) : IClassFixture<IndexFixture>
{
    [Fact]
    public async Task FindSymbol_ReturnsCompactProjection_ByDefault()
    {
        var output = await fixture.RunCliAsync(
            "find-symbol",
            "WorkspaceSymbolIndexBuilder",
            "--index",
            fixture.IndexDirectory,
            "--kind",
            "class",
            "--limit",
            "1");

        using var document = JsonDocument.Parse(output);
        var symbol = document.RootElement[0];

        Assert.True(symbol.TryGetProperty("id", out _));
        Assert.True(symbol.TryGetProperty("name", out _));
        Assert.True(symbol.TryGetProperty("file", out _));
        Assert.True(symbol.TryGetProperty("startLine", out _));
        Assert.False(symbol.TryGetProperty("fileId", out _));
        Assert.False(symbol.TryGetProperty("isStatic", out _));
        Assert.False(symbol.TryGetProperty("parentId", out _));
        Assert.DoesNotContain("\n", output.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindSymbol_FullFlag_ReturnsFullRecord()
    {
        var output = await fixture.RunCliAsync(
            "find-symbol",
            "WorkspaceSymbolIndexBuilder",
            "--index",
            fixture.IndexDirectory,
            "--kind",
            "class",
            "--limit",
            "1",
            "--full");

        using var document = JsonDocument.Parse(output);
        var symbol = document.RootElement[0];

        Assert.True(symbol.TryGetProperty("fileId", out _));
        Assert.True(symbol.TryGetProperty("isStatic", out _));
        Assert.True(symbol.TryGetProperty("parentId", out _));
    }

    [Fact]
    public async Task FindSymbol_TextFormat_ReturnsLineOrientedOutput()
    {
        var output = await fixture.RunCliAsync(
            "find-symbol",
            "WorkspaceSymbolIndexBuilder",
            "--index",
            fixture.IndexDirectory,
            "--kind",
            "class",
            "--limit",
            "1",
            "--format",
            "text");

        Assert.Contains("class", output, StringComparison.Ordinal);
        Assert.Contains("WorkspaceSymbolIndexBuilder", output, StringComparison.Ordinal);
        Assert.DoesNotContain("[", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outline_ReturnsSymbolAndChildren()
    {
        var output = await fixture.RunCliAsync(
            "outline",
            "CodeIndex.Roslyn.WorkspaceInspector",
            "--index",
            fixture.IndexDirectory,
            "--kind",
            "method",
            "--limit",
            "5");

        using var document = JsonDocument.Parse(output);
        Assert.Equal("WorkspaceInspector", document.RootElement.GetProperty("symbol").GetProperty("name").GetString());
        Assert.NotEmpty(document.RootElement.GetProperty("children").EnumerateArray());
    }

    [Fact]
    public async Task Build_IfChanged_SkipsRewrite_WhenUnchanged()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-if-changed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await fixture.RunCliAsync("build", fixture.SampleSolutionPath, "--out", outputDirectory, "--skip-embeddings");
            var firstWrite = File.GetLastWriteTimeUtc(Path.Combine(outputDirectory, "code-index.symbols.json"));

            await Task.Delay(1100);
            var second = await fixture.RunCliAsync(
                "build",
                fixture.SampleSolutionPath,
                "--out",
                outputDirectory,
                "--skip-embeddings",
                "--if-changed");

            Assert.Contains("unchanged", second, StringComparison.OrdinalIgnoreCase);
            var secondWrite = File.GetLastWriteTimeUtc(Path.Combine(outputDirectory, "code-index.symbols.json"));
            Assert.Equal(firstWrite, secondWrite);
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
    public async Task Build_ExcludesLocals_ByDefault()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-no-locals-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await fixture.RunCliAsync("build", fixture.SampleSolutionPath, "--out", outputDirectory, "--skip-embeddings");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.symbols.json")));
            Assert.DoesNotContain(
                document.RootElement.EnumerateArray(),
                symbol => symbol.GetProperty("kind").GetString() == "local");
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
    public async Task Build_SkipEmbeddings_WritesEmptyEmbeddings()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"code-index-skip-emb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await fixture.RunCliAsync("build", fixture.SampleSolutionPath, "--out", outputDirectory, "--skip-embeddings");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "code-index.embeddings.json")));
            Assert.Empty(document.RootElement.EnumerateArray());
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
    public async Task FindSymbol_FieldsFilter_ReturnsRequestedColumnsOnly()
    {
        var output = await fixture.RunCliAsync(
            "find-symbol",
            "WorkspaceSymbolIndexBuilder",
            "--index",
            fixture.IndexDirectory,
            "--kind",
            "class",
            "--limit",
            "1",
            "--fields",
            "name,kind");

        using var document = JsonDocument.Parse(output);
        var symbol = document.RootElement[0];
        Assert.Equal(JsonValueKind.String, symbol.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.String, symbol.GetProperty("kind").ValueKind);
        Assert.False(symbol.TryGetProperty("id", out _));
        Assert.False(symbol.TryGetProperty("qualifiedName", out _));
    }
}
