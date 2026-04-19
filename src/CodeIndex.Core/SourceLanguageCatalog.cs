namespace CodeIndex.Core;

public static class SourceLanguageCatalog
{
    private static readonly Dictionary<string, string> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".java"] = "Java",
        [".go"] = "Go",
        [".ts"] = "TypeScript",
        [".py"] = "Python",
        [".php"] = "PHP"
    };

    public static bool TryGetLanguage(string path, out string language)
    {
        return ExtensionToLanguage.TryGetValue(Path.GetExtension(path), out language!);
    }

    public static string CreateFileSummary(string normalizedPath, string projectName, string language)
    {
        return $"{language} source file {Path.GetFileName(normalizedPath)} in {projectName}.";
    }
}
