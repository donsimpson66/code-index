using CodeIndex.Core;

namespace CodeIndex.Core.Tests;

public class FileIndexingTests
{
    [Fact]
    public void NormalizeRelativePath_UsesForwardSlashes()
    {
        var root = Path.Combine(Path.DirectorySeparatorChar.ToString(), "repo");
        var file = Path.Combine(root, "src", "CodeIndex.Core", "FileRecord.cs");

        var normalizedPath = PathNormalization.NormalizeRelativePath(root, file);

        Assert.Equal("src/CodeIndex.Core/FileRecord.cs", normalizedPath);
    }

    [Fact]
    public void CreateSymbolId_PrefixesStableIdentifier()
    {
        var symbolId = DeterministicId.CreateSymbolId("T:CodeIndex.Core.FileRecord");

        Assert.Equal("s:T:CodeIndex.Core.FileRecord", symbolId);
    }

    [Fact]
    public async Task CodeIndexJson_RoundTripsSnapshot()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"code-index-core-tests-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(tempDirectory, "code-index.snapshot.json");

        var snapshot = new CodeIndexSnapshot(
            new CodeIndexMeta("1.0", "0.1.0", "repo", DateTimeOffset.UtcNow, "/repo", "/repo/repo.sln", "solution"),
            [new FileRecord("f:file.cs", "file.cs", "Repo", "C#", "sha256:test", "summary")],
            [new SymbolRecord("s:T:Repo.Type", "Type", "Repo.Type", "class", "f:file.cs", new TextRangeRecord(1, 1, 1, 10), "class Repo.Type", "summary", null, "public", false, false, false, false)],
            [new EdgeRecord(EdgeTypes.Contains, "s:T:Repo.Type", "s:T:Repo.Type")],
            Array.Empty<ReferenceRecord>(),
            [new EmbeddingRecord(EmbeddingItemTypes.File, "f:file.cs", [1f, 0f])]);

        try
        {
            await CodeIndexJson.WriteToFileAsync(outputPath, snapshot);

            var roundTripped = await CodeIndexJson.ReadFromFileAsync<CodeIndexSnapshot>(outputPath);

            Assert.Equal(snapshot.Meta, roundTripped.Meta);
            Assert.Equal(snapshot.Files, roundTripped.Files);
            Assert.Equal(snapshot.Symbols, roundTripped.Symbols);
            Assert.Equal(snapshot.Edges, roundTripped.Edges);
            Assert.Equal(snapshot.References, roundTripped.References);
            Assert.Single(roundTripped.Embeddings);
            Assert.Equal(snapshot.Embeddings[0].ItemType, roundTripped.Embeddings[0].ItemType);
            Assert.Equal(snapshot.Embeddings[0].ItemId, roundTripped.Embeddings[0].ItemId);
            Assert.Equal(snapshot.Embeddings[0].Vector, roundTripped.Embeddings[0].Vector);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

}