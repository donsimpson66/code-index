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
}