using System.Security.Cryptography;
using System.Text.Json;

namespace CodeIndex.Core;

public static class DeterministicId
{
    public static string CreateFileId(string normalizedPath) => $"f:{normalizedPath}";

    public static string CreateSymbolId(string stableSymbolId) => $"s:{stableSymbolId}";
}

public static class PathNormalization
{
    public static string NormalizeRelativePath(string rootPath, string path)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(path));
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

public static class FileHashProvider
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return $"sha256:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
    }
}

public static class FileSummaryGenerator
{
    public static string CreateSummary(string normalizedPath, string projectName)
    {
        var fileName = Path.GetFileName(normalizedPath);
        return $"C# source file {fileName} in project {projectName}.";
    }
}

public static class CodeIndexMetaFactory
{
    public static CodeIndexMeta Create(string inputPath, string inputKind)
    {
        var fullPath = Path.GetFullPath(inputPath);
        var sourceRoot = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Could not determine source root for {fullPath}");

        return new CodeIndexMeta(
            SchemaVersion: "1.0",
            ToolVersion: "0.1.0",
            RepoName: Path.GetFileNameWithoutExtension(fullPath),
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            SourceRoot: sourceRoot,
            InputPath: fullPath,
            InputKind: inputKind);
    }
}

public static class CodeIndexJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task WriteToFileAsync<T>(string outputPath, T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
    }

    public static async Task<T> ReadFromFileAsync<T>(string inputPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(inputPath);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);

        if (value is null)
        {
            throw new InvalidOperationException($"Could not deserialize JSON from {inputPath}");
        }

        return value;
    }
}