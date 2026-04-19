namespace CodeIndex.Core;

public static class SourceInputKindDetector
{
    public static string Detect(string path)
    {
        if (Directory.Exists(path))
        {
            return "directory";
        }

        return Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase)
            ? "solution"
            : "project";
    }

    public static bool IsDirectoryInput(string path)
    {
        return string.Equals(Detect(path), "directory", StringComparison.Ordinal);
    }
}
