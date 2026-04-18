using CodeIndex.Core;

namespace CodeIndex.Core.Tests;

public class CodeIndexValidationTests
{
    [Fact]
    public void ValidateOrThrow_AllowsConsistentSnapshot()
    {
        var snapshot = new CodeIndexSnapshot(
            new CodeIndexMeta("1.0", "0.1.0", "repo", DateTimeOffset.UtcNow, "/repo", "/repo/repo.sln", "solution"),
            new[] { new FileRecord("f:file.cs", "file.cs", "Repo", "C#", "sha256:test", "summary") },
            new[] { new SymbolRecord("s:T:Repo.Type", "Type", "Repo.Type", "class", "f:file.cs", new TextRangeRecord(1, 1, 1, 10), "Repo.Type", "summary", null, "public", false, false, false, false) },
            Array.Empty<EdgeRecord>(),
            Array.Empty<ReferenceRecord>(),
            new[] { new EmbeddingRecord(EmbeddingItemTypes.File, "f:file.cs", [1f, 0f]) });

        var validator = new CodeIndexValidator();

        validator.ValidateOrThrow(snapshot);
    }

    [Fact]
    public void Validate_FindsMissingFileReference()
    {
        var snapshot = new CodeIndexSnapshot(
            new CodeIndexMeta("1.0", "0.1.0", "repo", DateTimeOffset.UtcNow, "/repo", "/repo/repo.sln", "solution"),
            Array.Empty<FileRecord>(),
            new[] { new SymbolRecord("s:T:Repo.Type", "Type", "Repo.Type", "class", "f:file.cs", new TextRangeRecord(1, 1, 1, 10), "Repo.Type", "summary", null, "public", false, false, false, false) },
            Array.Empty<EdgeRecord>(),
            Array.Empty<ReferenceRecord>(),
            Array.Empty<EmbeddingRecord>());

        var validator = new CodeIndexValidator();
        var issues = validator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Code == "symbol-file-missing");
    }

    [Fact]
    public void Validate_FindsNonNormalizedFilePath_And_InvalidContainsEdge()
    {
        var snapshot = new CodeIndexSnapshot(
            new CodeIndexMeta("1.0", "0.1.0", "repo", DateTimeOffset.UtcNow, "/repo", "/repo/repo.sln", "solution"),
            new[]
            {
                new FileRecord("f:src\\File.cs", "src\\File.cs", "Repo", "C#", "hash", "summary")
            },
            new[]
            {
                new SymbolRecord("s:T:Repo.Parent", "Parent", "Repo.Parent", "class", "f:src\\File.cs", new TextRangeRecord(1, 1, 1, 5), "Repo.Parent", "summary", null, "public", false, false, false, false),
                new SymbolRecord("s:M:Repo.Parent.Child()", "Child", "Repo.Parent.Child", "method", "f:src\\File.cs", new TextRangeRecord(2, 1, 2, 5), "Repo.Parent.Child()", "summary", null, "public", false, false, false, false)
            },
            new[]
            {
                new EdgeRecord(EdgeTypes.Contains, "s:T:Repo.Parent", "s:M:Repo.Parent.Child()")
            },
            Array.Empty<ReferenceRecord>(),
            Array.Empty<EmbeddingRecord>());

        var validator = new CodeIndexValidator();
        var issues = validator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Code == "file-path-not-normalized");
        Assert.Contains(issues, issue => issue.Code == "file-hash-invalid");
        Assert.Contains(issues, issue => issue.Code == "edge-contains-inconsistent");
    }

    [Fact]
    public void Validate_FindsNonUtcTimestamp_And_UnsortedArtifacts()
    {
        var snapshot = new CodeIndexSnapshot(
            new CodeIndexMeta("1.0", "0.1.0", "repo", new DateTimeOffset(2026, 4, 18, 10, 0, 0, TimeSpan.FromHours(2)), "/repo", "/repo/repo.sln", "solution"),
            new[]
            {
                new FileRecord("f:z.cs", "z.cs", "Repo", "C#", "sha256:test", "summary"),
                new FileRecord("f:a.cs", "a.cs", "Repo", "C#", "sha256:test", "summary")
            },
            new[]
            {
                new SymbolRecord("s:T:Repo.Zed", "Zed", "Repo.Zed", "class", "f:z.cs", new TextRangeRecord(1, 1, 1, 5), "Repo.Zed", "summary", null, "public", false, false, false, false),
                new SymbolRecord("s:T:Repo.Alpha", "Alpha", "Repo.Alpha", "class", "f:a.cs", new TextRangeRecord(1, 1, 1, 5), "Repo.Alpha", "summary", null, "public", false, false, false, false)
            },
            new[]
            {
                new EdgeRecord(EdgeTypes.Contains, "s:T:Repo.Zed", "s:T:Repo.Alpha"),
                new EdgeRecord(EdgeTypes.Contains, "s:T:Repo.Alpha", "s:T:Repo.Zed")
            },
            Array.Empty<ReferenceRecord>(),
            Array.Empty<EmbeddingRecord>());

        var validator = new CodeIndexValidator();
        var issues = validator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Code == "meta-generated-at-not-utc");
        Assert.Contains(issues, issue => issue.Code == "files-not-sorted");
        Assert.Contains(issues, issue => issue.Code == "symbols-not-sorted");
        Assert.Contains(issues, issue => issue.Code == "edges-not-sorted");
    }

    [Fact]
    public void Validate_FindsMissingEmbeddingItem()
    {
        var snapshot = new CodeIndexSnapshot(
            new CodeIndexMeta("1.0", "0.1.0", "repo", DateTimeOffset.UtcNow, "/repo", "/repo/repo.sln", "solution"),
            Array.Empty<FileRecord>(),
            Array.Empty<SymbolRecord>(),
            Array.Empty<EdgeRecord>(),
            Array.Empty<ReferenceRecord>(),
            new[] { new EmbeddingRecord(EmbeddingItemTypes.Symbol, "s:T:Repo.Missing", [1f]) });

        var validator = new CodeIndexValidator();
        var issues = validator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Code == "embedding-item-missing");
    }
}