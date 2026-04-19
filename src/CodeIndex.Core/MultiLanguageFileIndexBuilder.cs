namespace CodeIndex.Core;

public sealed class MultiLanguageFileIndexBuilder
{
    private static readonly string[] IgnoredDirectorySegments =
    [
        ".git",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "vendor"
    ];

    public async Task<IReadOnlyList<FileRecord>> BuildAsync(string inputPath, bool includeGenerated = false, CancellationToken cancellationToken = default)
    {
        var sourceRoot = GetSourceRoot(inputPath);
        var projectName = Path.GetFileName(sourceRoot);
        var fileRecords = new List<FileRecord>();

        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SourceLanguageCatalog.TryGetLanguage(filePath, out var language))
            {
                continue;
            }

            var normalizedPath = PathNormalization.NormalizeRelativePath(sourceRoot, filePath);

            if (IsIgnoredPath(normalizedPath, includeGenerated))
            {
                continue;
            }

            var hash = await FileHashProvider.ComputeSha256Async(filePath, cancellationToken);
            var summary = SourceLanguageCatalog.CreateFileSummary(normalizedPath, projectName, language);
            fileRecords.Add(new FileRecord(
                DeterministicId.CreateFileId(normalizedPath),
                normalizedPath,
                projectName,
                language,
                hash,
                summary));
        }

        return fileRecords
            .OrderBy(record => record.Path, StringComparer.Ordinal)
            .ToArray();
    }

    public static string GetSourceRoot(string inputPath)
    {
        var sourceRoot = Path.GetFullPath(inputPath);

        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {inputPath}");
        }

        return sourceRoot;
    }

    private static bool IsIgnoredPath(string normalizedPath, bool includeGenerated)
    {
        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathSegments.Any(segment => IgnoredDirectorySegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (includeGenerated)
        {
            return false;
        }

        var fileName = Path.GetFileName(normalizedPath);
        return fileName.Contains(".generated.", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase);
    }
}
