using System.Text.RegularExpressions;

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

public sealed class MultiLanguageSymbolIndexBuilder
{
    public Task<IReadOnlyList<SymbolRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        return BuildAsync(inputPath, files, indexedFilePaths: null, includeGenerated, cancellationToken);
    }

    public async Task<IReadOnlyList<SymbolRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyCollection<string>? indexedFilePaths,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = MultiLanguageFileIndexBuilder.GetSourceRoot(inputPath);
        var indexedPaths = indexedFilePaths is null
            ? null
            : new HashSet<string>(indexedFilePaths, StringComparer.Ordinal);
        var symbolRecords = new List<SymbolRecord>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (indexedPaths is not null && !indexedPaths.Contains(file.Path))
            {
                continue;
            }

            var fullPath = Path.Combine(sourceRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                continue;
            }

            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            symbolRecords.AddRange(MultiLanguageSymbolParser.Parse(file, source));
        }

        return symbolRecords
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.FileId, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Range.StartLine)
            .ThenBy(symbol => symbol.Range.StartColumn)
            .ToArray();
    }
}

public sealed class MultiLanguageEdgeIndexBuilder
{
    public async Task<IReadOnlyList<EdgeRecord>> BuildAsync(string inputPath, bool includeGenerated = false, CancellationToken cancellationToken = default)
    {
        var fileBuilder = new MultiLanguageFileIndexBuilder();
        var files = await fileBuilder.BuildAsync(inputPath, includeGenerated, cancellationToken);
        return await BuildForFilesCoreAsync(inputPath, files, cancellationToken);
    }

    public Task<IReadOnlyList<EdgeRecord>> BuildAsync(
        string inputPath,
        IReadOnlyCollection<string> indexedFilePaths,
        IReadOnlySet<string> knownSymbolIds,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = MultiLanguageFileIndexBuilder.GetSourceRoot(inputPath);
        var fileRecords = indexedFilePaths
            .Select(path => new FileRecord(DeterministicId.CreateFileId(path), path, Path.GetFileName(sourceRoot), InferLanguage(path), string.Empty, string.Empty))
            .ToArray();

        return BuildForFilesCoreAsync(inputPath, fileRecords, cancellationToken);
    }

    public Task<IReadOnlyList<EdgeRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyCollection<string>? indexedFilePaths,
        CancellationToken cancellationToken = default)
    {
        var selectedFiles = indexedFilePaths is null
            ? files
            : files.Where(file => indexedFilePaths.Contains(file.Path)).ToArray();

        return BuildForFilesCoreAsync(inputPath, selectedFiles, cancellationToken);
    }

    public IReadOnlyList<EdgeRecord> BuildFromSymbols(IReadOnlyList<SymbolRecord> symbols)
    {
        return symbols
            .Where(symbol => symbol.ParentId is not null)
            .Select(symbol => new EdgeRecord(EdgeTypes.Contains, symbol.ParentId!, symbol.Id))
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<EdgeRecord>> BuildForFilesCoreAsync(string inputPath, IReadOnlyList<FileRecord> files, CancellationToken cancellationToken)
    {
        var symbolBuilder = new MultiLanguageSymbolIndexBuilder();
        var symbols = await symbolBuilder.BuildAsync(inputPath, files, includeGenerated: true, cancellationToken: cancellationToken);
        var containsEdges = BuildFromSymbols(symbols);
        var callEdges = await MultiLanguageUsageParser.BuildCallEdgesAsync(inputPath, files, symbols, cancellationToken);
        return containsEdges
            .Concat(callEdges)
            .Distinct()
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static string InferLanguage(string path)
    {
        return SourceLanguageCatalog.TryGetLanguage(path, out var language) ? language : "Unknown";
    }
}

public sealed class MultiLanguageReferenceIndexBuilder
{
    public async Task<IReadOnlyList<ReferenceRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        return await MultiLanguageUsageParser.BuildReferencesAsync(inputPath, files, symbols, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceRecord>> BuildAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyCollection<string> indexedFilePaths,
        bool includeGenerated = false,
        CancellationToken cancellationToken = default)
    {
        var selectedFiles = files.Where(file => indexedFilePaths.Contains(file.Path)).ToArray();
        return await MultiLanguageUsageParser.BuildReferencesAsync(inputPath, selectedFiles, symbols, cancellationToken);
    }
}

internal static class MultiLanguageUsageParser
{
    private static readonly Regex JavaMethodCallRegex = new(@"(?:(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JavaConstructorReferenceRegex = new(@"\bnew\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JavaImportRegex = new(@"^\s*import\s+(?<static>static\s+)?(?<path>[A-Za-z_][A-Za-z0-9_\.]*(?:\.\*)?)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex TypeScriptMethodCallRegex = new(@"(?:(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptConstructorReferenceRegex = new(@"\bnew\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptImportRegex = new(@"^\s*import\s+(?:(?<default>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*)?(?:\{(?<named>[^}]*)\}|\*\s+as\s+(?<namespace>[A-Za-z_][A-Za-z0-9_]*))?\s*from\s*['""](?<path>[^'""]+)['""]", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex TypeScriptFieldTypeRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptCallableParameterRegex = new(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptVariableTypeRegex = new(@"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptVariableNewRegex = new(@"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptVariableAliasRegex = new(@"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:(?<owner>this)\.)?(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*;?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFieldAssignmentNewRegex = new(@"^\s*this\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFieldAssignmentAliasRegex = new(@"^\s*this\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:(?<owner>this)\.)?(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*;?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoSelectorCallRegex = new(@"(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoCallRegex = new(@"(?<!func\s)(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoImportSpecRegex = new(@"^(?:(?<alias>[A-Za-z_][A-Za-z0-9_]*|\.|_)\s+)?""(?<path>[^""]+)""$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonCallRegex = new(@"(?:(?<qualifier>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonImportRegex = new(@"^\s*import\s+(?<module>[A-Za-z_][A-Za-z0-9_\.]*)\s*(?:as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PythonFromImportRegex = new(@"^\s*from\s+(?<module>[A-Za-z_][A-Za-z0-9_\.]*)\s+import\s+(?<names>[^\r\n]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PythonCallableParameterRegex = new(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonAssignmentTypeRegex = new(@"^\s*(?:(?<owner>self)\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonAliasAssignmentRegex = new(@"^\s*(?:(?<owner>self)\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:(?<sourceOwner>self)\.)?(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpCallRegex = new(@"(?:(?<qualifier>\$?[A-Za-z_][A-Za-z0-9_]*)\s*(?:->|::)\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpConstructorReferenceRegex = new(@"\bnew\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpUseRegex = new(@"^\s*use\s+(?<function>function\s+)?(?<path>[A-Za-z_\\][A-Za-z0-9_\\]*)(?:\s+as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PhpCallableParameterRegex = new(@"(?<type>[A-Za-z_\\][A-Za-z0-9_\\|?]*)\s+\$(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpFieldTypeRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly)\s+)*(?<type>[A-Za-z_\\][A-Za-z0-9_\\|?]*)\s+\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]+)?;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpAssignmentNewTypeRegex = new(@"^\s*(?:(?<owner>\$this)->)?(?:(?<dollar>\$)?(?<name>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*new\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpAliasAssignmentRegex = new(@"^\s*(?:(?<owner>\$this)->)?(?:(?<dollar>\$)?(?<name>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*(?:(?<sourceOwner>\$this)->)?\$(?<source>[A-Za-z_][A-Za-z0-9_]*)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> IgnoredCallNames = new(StringComparer.Ordinal)
    {
        "if",
        "for",
        "switch",
        "while",
        "catch",
        "return",
        "new",
        "func"
    };

    private static readonly HashSet<string> CallableKinds = new(StringComparer.Ordinal)
    {
        SymbolKinds.Method,
        SymbolKinds.Constructor
    };

    public static async Task<IReadOnlyList<EdgeRecord>> BuildCallEdgesAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        CancellationToken cancellationToken)
    {
        var references = await BuildReferencesAsync(inputPath, files, symbols, cancellationToken);
        return references
            .Where(reference => reference.SourceSymbolId is not null)
            .Select(reference => new EdgeRecord(EdgeTypes.Calls, reference.SourceSymbolId!, reference.TargetSymbolId))
            .Distinct()
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<IReadOnlyList<ReferenceRecord>> BuildReferencesAsync(
        string inputPath,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        CancellationToken cancellationToken)
    {
        var sourceRoot = MultiLanguageFileIndexBuilder.GetSourceRoot(inputPath);
        var references = new List<ReferenceRecord>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Language is not ("Java" or "Go" or "TypeScript" or "Python" or "PHP"))
            {
                continue;
            }

            var fullPath = Path.Combine(sourceRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var fileSymbols = symbols.Where(symbol => string.Equals(symbol.FileId, file.Id, StringComparison.Ordinal)).ToArray();
            references.AddRange(file.Language switch
            {
                "Java" => ExtractJavaReferences(file, source, fileSymbols, symbols),
                "Go" => ExtractGoReferences(file, source, fileSymbols, symbols),
                "TypeScript" => ExtractTypeScriptReferences(file, source, fileSymbols, symbols),
                "Python" => ExtractPythonReferences(file, source, fileSymbols, symbols),
                "PHP" => ExtractPhpReferences(file, source, fileSymbols, symbols),
                _ => Array.Empty<ReferenceRecord>()
            });
        }

        return references
            .Distinct()
            .OrderBy(reference => reference.TargetSymbolId, StringComparer.Ordinal)
            .ThenBy(reference => reference.FileId, StringComparer.Ordinal)
            .ThenBy(reference => reference.Range.StartLine)
            .ThenBy(reference => reference.Range.StartColumn)
            .ThenBy(reference => reference.SourceSymbolId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ReferenceRecord> ExtractJavaReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var packageName = MultiLanguageSymbolParser.GetJavaPackageName(file, source);
        var imports = ParseJavaImports(source);
        var packageSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Java", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, packageName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(packageName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Java", StringComparison.Ordinal) &&
                (imports.ImportedPackages.Any(package => string.Equals(symbol.QualifiedName, package, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(package + ".", StringComparison.Ordinal)) ||
                 imports.ImportedTypes.Any(typeName => string.Equals(symbol.QualifiedName, typeName, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(typeName + ".", StringComparison.Ordinal)) ||
                 imports.ImportedStaticTypes.Any(typeName => string.Equals(symbol.QualifiedName, typeName, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(typeName + ".", StringComparison.Ordinal)) ||
                 imports.ImportedStaticMembers.Any(memberName => string.Equals(symbol.QualifiedName, memberName, StringComparison.Ordinal))))
            .ToArray();
        var candidateSymbols = packageSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeSymbols = candidateSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToArray();
        var callableSymbols = candidateSymbols.Where(symbol => CallableKinds.Contains(symbol.Kind)).ToArray();
        var callableLookup = callableSymbols.ToDictionary(symbol => symbol.QualifiedName, StringComparer.Ordinal);
        var methodLookup = callableSymbols
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var typeLookup = typeSymbols
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.JavaOrCSharpTypeRegex, MultiLanguageSymbolParser.JavaOrCSharpMethodRegex);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in JavaMethodCallRegex.Matches(context.Line))
            {
                var callName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(callName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolveJavaMethodTarget(context.Type, qualifier, callName, packageName, imports, callableLookup, methodLookup, typeLookup);
                if (target is null)
                {
                    continue;
                }

                references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, callName, context.Line));
            }

            foreach (Match match in JavaConstructorReferenceRegex.Matches(context.Line))
            {
                var typeName = match.Groups["name"].Value;
                var targetType = ResolveJavaTypeTarget(typeName, packageName, imports, typeLookup);
                if (targetType is not null)
                {
                    references.Add(CreateReference(targetType, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, typeName, context.Line));
                }
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractGoReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var packageName = MultiLanguageSymbolParser.GetGoPackageName(file, source);
        var imports = ParseGoImports(source);
        var packageSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Go", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, packageName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(packageName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Go", StringComparison.Ordinal) &&
                imports.ImportedPackages.Any(importedPackage =>
                    string.Equals(symbol.QualifiedName, importedPackage, StringComparison.Ordinal) ||
                    symbol.QualifiedName.StartsWith(importedPackage + ".", StringComparison.Ordinal)))
            .ToArray();
        var candidateSymbols = packageSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeSymbols = candidateSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToArray();
        var callableSymbols = candidateSymbols.Where(symbol => CallableKinds.Contains(symbol.Kind)).ToArray();
        var packageFunctionLookup = packageSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || allSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedPackageFunctionLookup = callableSymbols
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => (MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var receiverMethodLookup = callableSymbols
            .Where(symbol => symbol.ParentId is not null)
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var typeLookup = typeSymbols
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var contexts = BuildGoContexts(source, fileSymbols);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in GoSelectorCallRegex.Matches(context.Line))
            {
                var methodName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(methodName))
                {
                    continue;
                }

                SymbolRecord? target = null;
                var qualifier = match.Groups["qualifier"].Value;

                if (imports.Bindings.TryGetValue(qualifier, out var importedPackageName) && importedPackageFunctionLookup.TryGetValue((importedPackageName, methodName), out var importedFunctions))
                {
                    target = importedFunctions[0];
                }
                else if (context.Type is not null && receiverMethodLookup.TryGetValue((context.Type.Id, methodName), out var methods))
                {
                    target = methods[0];
                }

                if (target is not null)
                {
                    references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, methodName, context.Line));
                }
            }

            foreach (Match match in GoCallRegex.Matches(context.Line))
            {
                var functionName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(functionName) || context.Line.Contains($".{functionName}(", StringComparison.Ordinal))
                {
                    continue;
                }

                if (packageFunctionLookup.TryGetValue(functionName, out var functions))
                {
                    references.Add(CreateReference(functions[0], context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, functionName, context.Line));
                }
            }

            foreach (var typeSymbol in typeLookup.Values)
            {
                var compositeLiteralToken = $"{typeSymbol.Name}{{";
                var index = context.Line.IndexOf(compositeLiteralToken, StringComparison.Ordinal);
                if (index >= 0)
                {
                    references.Add(CreateReference(typeSymbol, context.Callable.Id, file.Id, context.LineNumber, index + 1, typeSymbol.Name, context.Line));
                }
            }

            foreach (var binding in imports.Bindings)
            {
                foreach (var typeSymbol in typeSymbols.Where(symbol => string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), binding.Value, StringComparison.Ordinal)))
                {
                    var compositeLiteralToken = $"{binding.Key}.{typeSymbol.Name}{{";
                    var index = context.Line.IndexOf(compositeLiteralToken, StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        references.Add(CreateReference(typeSymbol, context.Callable.Id, file.Id, context.LineNumber, index + 1, typeSymbol.Name, context.Line));
                    }
                }
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractTypeScriptReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var moduleName = MultiLanguageSymbolParser.BuildLogicalModuleQualifier(file.Path);
        var moduleSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "TypeScript", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, moduleName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(moduleName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedModules = ParseTypeScriptImports(file.Path, source);
        var importedBindings = ParseTypeScriptImportBindings(file.Path, source);
        var importedSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "TypeScript", StringComparison.Ordinal) &&
            importedModules.Any(module =>
                string.Equals(symbol.QualifiedName, module, StringComparison.Ordinal) ||
                symbol.QualifiedName.StartsWith(module + ".", StringComparison.Ordinal)))
            .ToArray();
        var candidateSymbols = moduleSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeSymbols = candidateSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToArray();
        var callableSymbols = candidateSymbols.Where(symbol => CallableKinds.Contains(symbol.Kind)).ToArray();
        var propertySymbols = candidateSymbols.Where(symbol => symbol.Kind is SymbolKinds.Field or SymbolKinds.Property).ToArray();
        var typeLookup = typeSymbols
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var importedTypeLookup = ResolveImportedTypeLookup(importedBindings, candidateSymbols);
        var importedCallableLookup = ResolveImportedCallableLookup(importedBindings, candidateSymbols);
        var moduleFunctionLookup = callableSymbols.GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol).ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var topLevelFunctionLookup = callableSymbols
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedModuleFunctionLookup = callableSymbols
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => (MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var fieldTypeLookup = ParseTypeScriptFieldTypes(source, fileSymbols);
        var callableTypeHintLookup = ParseTypeScriptCallableTypeHints(source, fileSymbols, fieldTypeLookup);
        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.TypeScriptTypeRegex, MultiLanguageSymbolParser.TypeScriptMethodRegex);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in TypeScriptMethodCallRegex.Matches(context.Line))
            {
                var methodName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(methodName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolveTypeScriptCallableTarget(context.Type, context.Callable, qualifier, methodName, importedBindings, moduleFunctionLookup, topLevelFunctionLookup, importedModuleFunctionLookup, typeLookup, importedTypeLookup, importedCallableLookup, propertySymbols, fieldTypeLookup, callableTypeHintLookup);
                if (target is not null)
                {
                    references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, methodName, context.Line));
                }
            }

            foreach (Match match in TypeScriptConstructorReferenceRegex.Matches(context.Line))
            {
                var typeName = match.Groups["name"].Value;
                if (typeLookup.TryGetValue(typeName, out var targetType) || importedTypeLookup.TryGetValue(typeName, out targetType))
                {
                    references.Add(CreateReference(targetType, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, typeName, context.Line));
                }
            }

            foreach (var propertySymbol in propertySymbols)
            {
                var token = $".{propertySymbol.Name}";
                var index = context.Line.IndexOf(token, StringComparison.Ordinal);
                if (index >= 0 && (context.Type is null || string.Equals(propertySymbol.ParentId, context.Type.Id, StringComparison.Ordinal)))
                {
                    references.Add(CreateReference(propertySymbol, context.Callable.Id, file.Id, context.LineNumber, index + 2, propertySymbol.Name, context.Line));
                }
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractPythonReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var moduleName = MultiLanguageSymbolParser.BuildLogicalModuleQualifier(file.Path);
        var imports = ParsePythonImports(source);
        var moduleSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Python", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, moduleName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(moduleName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "Python", StringComparison.Ordinal) &&
                imports.Bindings.Values.Any(target =>
                    string.Equals(symbol.QualifiedName, target, StringComparison.Ordinal) ||
                    symbol.QualifiedName.StartsWith(target + ".", StringComparison.Ordinal)))
            .ToArray();
        var candidateSymbols = moduleSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeLookup = candidateSymbols
            .Where(MultiLanguageSymbolParser.IsTypeSymbol)
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var methodLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var topLevelFunctionLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedModuleFunctionLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Module))
            .GroupBy(symbol => (MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var importedMemberLookup = ResolveImportedMemberLookup(imports.Bindings, candidateSymbols);
        var fieldTypeLookup = ParsePythonFieldTypes(source, fileSymbols);
        var callableTypeHintLookup = ParsePythonCallableTypeHints(source, fileSymbols, fieldTypeLookup);
        var contexts = BuildPythonContexts(source, fileSymbols);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in PythonCallRegex.Matches(context.Line))
            {
                var callableName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(callableName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolvePythonCallableTarget(context.Type, context.Callable, qualifier, callableName, imports.Bindings, methodLookup, topLevelFunctionLookup, importedModuleFunctionLookup, typeLookup, importedMemberLookup, fieldTypeLookup, callableTypeHintLookup);
                if (target is null)
                {
                    continue;
                }

                references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, callableName, context.Line));
            }
        }

        return references;
    }

    private static IReadOnlyList<ReferenceRecord> ExtractPhpReferences(FileRecord file, string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyList<SymbolRecord> allSymbols)
    {
        var namespaceName = MultiLanguageSymbolParser.GetPhpNamespaceName(file, source);
        var imports = ParsePhpImports(source);
        var namespaceSymbols = allSymbols.Where(symbol =>
            string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "PHP", StringComparison.Ordinal) &&
            (string.Equals(symbol.QualifiedName, namespaceName, StringComparison.Ordinal) ||
             symbol.QualifiedName.StartsWith(namespaceName + ".", StringComparison.Ordinal)))
            .ToArray();
        var importedSymbols = allSymbols.Where(symbol =>
                string.Equals(MultiLanguageSymbolParser.GetLanguageFromSymbolId(symbol.Id), "PHP", StringComparison.Ordinal) &&
                (imports.TypeBindings.Values.Any(target => string.Equals(symbol.QualifiedName, target, StringComparison.Ordinal) || symbol.QualifiedName.StartsWith(target + ".", StringComparison.Ordinal)) ||
                 imports.FunctionBindings.Values.Any(target => string.Equals(symbol.QualifiedName, target, StringComparison.Ordinal))))
            .ToArray();
        var candidateSymbols = namespaceSymbols.Concat(importedSymbols).Distinct().ToArray();
        var typeLookup = candidateSymbols
            .Where(MultiLanguageSymbolParser.IsTypeSymbol)
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var methodLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .GroupBy(symbol => (symbol.ParentId, symbol.Name), symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray());
        var topLevelFunctionLookup = candidateSymbols
            .Where(symbol => CallableKinds.Contains(symbol.Kind))
            .Where(symbol => symbol.ParentId is null || candidateSymbols.Any(parent => parent.Id == symbol.ParentId && parent.Kind == SymbolKinds.Namespace))
            .GroupBy(symbol => symbol.Name, symbol => symbol)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.Range.StartLine).ToArray(), StringComparer.Ordinal);
        var importedFunctionLookup = ResolveImportedMemberLookup(imports.FunctionBindings, candidateSymbols);
        var importedTypeLookup = ResolveImportedMemberLookup(imports.TypeBindings, candidateSymbols);
        var fieldTypeLookup = ParsePhpFieldTypes(source, fileSymbols);
        var callableTypeHintLookup = ParsePhpCallableTypeHints(source, fileSymbols, fieldTypeLookup);
        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.PhpTypeRegex, MultiLanguageSymbolParser.PhpFunctionRegex);
        var references = new List<ReferenceRecord>();

        foreach (var context in contexts)
        {
            if (context.Callable is null || context.LineNumber == context.Callable.Range.StartLine)
            {
                continue;
            }

            foreach (Match match in PhpCallRegex.Matches(context.Line))
            {
                var callableName = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(callableName))
                {
                    continue;
                }

                var qualifier = match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : null;
                var target = ResolvePhpCallableTarget(context.Type, context.Callable, qualifier, callableName, methodLookup, topLevelFunctionLookup, typeLookup, importedTypeLookup, importedFunctionLookup, fieldTypeLookup, callableTypeHintLookup);
                if (target is null)
                {
                    continue;
                }

                references.Add(CreateReference(target, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, callableName, context.Line));
            }

            foreach (Match match in PhpConstructorReferenceRegex.Matches(context.Line))
            {
                var typeName = match.Groups["name"].Value;
                if (ResolvePhpTypeTarget(typeName, typeLookup, importedTypeLookup) is { } targetType)
                {
                    references.Add(CreateReference(targetType, context.Callable.Id, file.Id, context.LineNumber, match.Groups["name"].Index + 1, typeName, context.Line));
                }
            }
        }

        return references;
    }

    private static SymbolRecord? ResolveJavaMethodTarget(
        SymbolRecord? currentType,
        string? qualifier,
        string methodName,
        string packageName,
        JavaImportInfo imports,
        IReadOnlyDictionary<string, SymbolRecord> callableLookup,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            if (currentType is not null && (string.Equals(qualifier, "this", StringComparison.Ordinal) || string.Equals(qualifier, currentType.Name, StringComparison.Ordinal)) &&
                methodLookup.TryGetValue((currentType.Id, methodName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            var qualifiedType = ResolveJavaTypeTarget(qualifier, packageName, imports, typeLookup);
            if (qualifiedType is not null && methodLookup.TryGetValue((qualifiedType.Id, methodName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, methodName), out var methods))
        {
            return methods[0];
        }

        if (ResolveJavaStaticMethodTarget(methodName, imports, callableLookup, methodLookup, typeLookup) is { } staticTarget)
        {
            return staticTarget;
        }

        return null;
    }

    private static SymbolRecord? ResolveJavaStaticMethodTarget(
        string methodName,
        JavaImportInfo imports,
        IReadOnlyDictionary<string, SymbolRecord> callableLookup,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup)
    {
        if (imports.ImportedStaticMemberAliases.TryGetValue(methodName, out var staticMemberQualifiedName) && callableLookup.TryGetValue(staticMemberQualifiedName, out var explicitStaticTarget))
        {
            return explicitStaticTarget;
        }

        foreach (var staticTypeQualifiedName in imports.ImportedStaticTypes)
        {
            var typeName = staticTypeQualifiedName.Split('.').Last();
            if (typeLookup.TryGetValue(typeName, out var staticType) && string.Equals(staticType.QualifiedName, staticTypeQualifiedName, StringComparison.Ordinal) && methodLookup.TryGetValue((staticType.Id, methodName), out var staticMethods))
            {
                return staticMethods[0];
            }
        }

        return null;
    }

    private static SymbolRecord? ResolveJavaTypeTarget(string typeName, string packageName, JavaImportInfo imports, IReadOnlyDictionary<string, SymbolRecord> typeLookup)
    {
        if (typeLookup.TryGetValue(typeName, out var localType))
        {
            return localType;
        }

        if (imports.ImportedTypeAliases.TryGetValue(typeName, out var importedTypeName))
        {
            var importedSimpleName = importedTypeName.Split('.').Last();
            if (typeLookup.TryGetValue(importedSimpleName, out var importedType) && string.Equals(importedType.QualifiedName, importedTypeName, StringComparison.Ordinal))
            {
                return importedType;
            }
        }

        foreach (var importedPackage in imports.ImportedPackages)
        {
            var importedSimpleName = typeName;
            if (typeLookup.TryGetValue(importedSimpleName, out var candidate) && string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(candidate.QualifiedName), importedPackage, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static JavaImportInfo ParseJavaImports(string source)
    {
        var importedTypeAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedPackages = new HashSet<string>(StringComparer.Ordinal);
        var importedStaticMemberAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedStaticTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in JavaImportRegex.Matches(source))
        {
            var importPath = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(importPath))
            {
                continue;
            }

            if (match.Groups["static"].Success)
            {
                if (importPath.EndsWith(".*", StringComparison.Ordinal))
                {
                    importedStaticTypes.Add(importPath[..^2]);
                }
                else
                {
                    importedStaticMemberAliases[importPath.Split('.').Last()] = importPath;
                }

                continue;
            }

            if (importPath.EndsWith(".*", StringComparison.Ordinal))
            {
                importedPackages.Add(importPath[..^2]);
                continue;
            }

            var alias = importPath.Split('.').Last();
            importedTypeAliases[alias] = importPath;
        }

        return new JavaImportInfo(importedTypeAliases, importedPackages, importedStaticMemberAliases, importedStaticTypes);
    }

    private static SymbolRecord? ResolveTypeScriptCallableTarget(
        SymbolRecord? currentType,
        SymbolRecord? currentCallable,
        string? qualifier,
        string methodName,
        IReadOnlyDictionary<string, string> importedBindings,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord[]> topLevelFunctionLookup,
        IReadOnlyDictionary<(string ModuleName, string Name), SymbolRecord[]> importedModuleFunctionLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedTypeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedCallableLookup,
        IReadOnlyList<SymbolRecord> propertySymbols,
        IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup,
        IReadOnlyDictionary<(string CallableId, string Name), string> callableTypeHintLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            if (currentType is not null && (string.Equals(qualifier, "this", StringComparison.Ordinal) || string.Equals(qualifier, currentType.Name, StringComparison.Ordinal)) &&
                methodLookup.TryGetValue((currentType.Id, methodName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            if (typeLookup.TryGetValue(qualifier, out var qualifiedType) && methodLookup.TryGetValue((qualifiedType.Id, methodName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }

            if (currentType is not null && fieldTypeLookup.TryGetValue((currentType.Id, qualifier), out var fieldTypeName) &&
                (typeLookup.TryGetValue(fieldTypeName, out var fieldType) || importedTypeLookup.TryGetValue(fieldTypeName, out fieldType)) &&
                methodLookup.TryGetValue((fieldType.Id, methodName), out var fieldTypeMethods))
            {
                return fieldTypeMethods[0];
            }

            if (currentCallable is not null && callableTypeHintLookup.TryGetValue((currentCallable.Id, qualifier), out var hintedTypeName) &&
                (typeLookup.TryGetValue(hintedTypeName, out var hintedType) || importedTypeLookup.TryGetValue(hintedTypeName, out hintedType)) &&
                methodLookup.TryGetValue((hintedType.Id, methodName), out var hintedTypeMethods))
            {
                return hintedTypeMethods[0];
            }

            if (importedBindings.TryGetValue(qualifier, out var importedModuleName) &&
                importedModuleFunctionLookup.TryGetValue((importedModuleName, methodName), out var importedModuleFunctions))
            {
                return importedModuleFunctions[0];
            }

            if (importedCallableLookup.TryGetValue(methodName, out var importedCallable))
            {
                return importedCallable;
            }

            if (topLevelFunctionLookup.TryGetValue(methodName, out var qualifiedFunctions))
            {
                return qualifiedFunctions[0];
            }
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, methodName), out var methods))
        {
            return methods[0];
        }

        if (importedCallableLookup.TryGetValue(methodName, out var topLevelImportedCallable))
        {
            return topLevelImportedCallable;
        }

        return topLevelFunctionLookup.TryGetValue(methodName, out var topLevelMethods) ? topLevelMethods[0] : null;
    }

    private static IReadOnlyDictionary<(string CallableId, string Name), string> ParseTypeScriptCallableTypeHints(string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var callableByLine = fileSymbols
            .Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor)
            .ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var typeHints = new Dictionary<(string CallableId, string Name), string>();

        foreach (var entry in callableByLine)
        {
            var declarationLine = lines[entry.Key - 1];
            var openParen = declarationLine.IndexOf('(');
            var closeParen = declarationLine.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen)
            {
                continue;
            }

            var parameterList = declarationLine[(openParen + 1)..closeParen];
            foreach (Match match in TypeScriptCallableParameterRegex.Matches(parameterList))
            {
                typeHints[(entry.Value.Id, match.Groups["name"].Value)] = match.Groups["type"].Value;
            }
        }

        var contexts = BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.TypeScriptTypeRegex, MultiLanguageSymbolParser.TypeScriptMethodRegex);
        foreach (var context in contexts)
        {
            if (context.Callable is null)
            {
                continue;
            }

            var typedVariableMatch = TypeScriptVariableTypeRegex.Match(context.Line);
            if (typedVariableMatch.Success)
            {
                typeHints[(context.Callable.Id, typedVariableMatch.Groups["name"].Value)] = typedVariableMatch.Groups["type"].Value;
            }

            var newVariableMatch = TypeScriptVariableNewRegex.Match(context.Line);
            if (newVariableMatch.Success)
            {
                typeHints[(context.Callable.Id, newVariableMatch.Groups["name"].Value)] = newVariableMatch.Groups["type"].Value;
            }

            var aliasMatch = TypeScriptVariableAliasRegex.Match(context.Line);
            if (aliasMatch.Success)
            {
                var sourceName = aliasMatch.Groups["source"].Value;
                string? sourceType = null;
                if (context.Type is not null && aliasMatch.Groups["owner"].Success)
                {
                    fieldTypeLookup.TryGetValue((context.Type.Id, sourceName), out sourceType);
                }
                else
                {
                    typeHints.TryGetValue((context.Callable.Id, sourceName), out sourceType);
                }

                if (!string.IsNullOrWhiteSpace(sourceType))
                {
                    typeHints[(context.Callable.Id, aliasMatch.Groups["name"].Value)] = sourceType;
                }
            }
        }

        return typeHints;
    }

    private static IReadOnlyDictionary<string, SymbolRecord> ResolveImportedTypeLookup(IReadOnlyDictionary<string, string> importedBindings, IReadOnlyList<SymbolRecord> candidateSymbols)
    {
        var lookup = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (var binding in importedBindings)
        {
            var target = candidateSymbols.FirstOrDefault(symbol =>
                MultiLanguageSymbolParser.IsTypeSymbol(symbol) &&
                string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), binding.Value, StringComparison.Ordinal) &&
                string.Equals(symbol.Name, binding.Key, StringComparison.Ordinal));

            if (target is not null)
            {
                lookup[binding.Key] = target;
            }
        }

        return lookup;
    }

    private static IReadOnlyDictionary<string, SymbolRecord> ResolveImportedCallableLookup(IReadOnlyDictionary<string, string> importedBindings, IReadOnlyList<SymbolRecord> candidateSymbols)
    {
        var lookup = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (var binding in importedBindings)
        {
            var target = candidateSymbols.FirstOrDefault(symbol =>
                CallableKinds.Contains(symbol.Kind) &&
                string.Equals(MultiLanguageSymbolParser.GetQualifierPrefix(symbol.QualifiedName), binding.Value, StringComparison.Ordinal) &&
                string.Equals(symbol.Name, binding.Key, StringComparison.Ordinal));

            if (target is not null)
            {
                lookup[binding.Key] = target;
            }
        }

        return lookup;
    }

    private static IReadOnlyDictionary<string, SymbolRecord> ResolveImportedMemberLookup(IReadOnlyDictionary<string, string> importedBindings, IReadOnlyList<SymbolRecord> candidateSymbols)
    {
        var lookup = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (var binding in importedBindings)
        {
            var target = candidateSymbols.FirstOrDefault(symbol => string.Equals(symbol.QualifiedName, binding.Value, StringComparison.Ordinal));
            if (target is not null)
            {
                lookup[binding.Key] = target;
            }
        }

        return lookup;
    }

    private static GoImportInfo ParseGoImports(string source)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedPackages = new HashSet<string>(StringComparer.Ordinal);
        var inImportBlock = false;

        foreach (var rawLine in MultiLanguageSymbolParser.SplitLines(source))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "import (", StringComparison.Ordinal))
            {
                inImportBlock = true;
                continue;
            }

            if (inImportBlock && string.Equals(line, ")", StringComparison.Ordinal))
            {
                inImportBlock = false;
                continue;
            }

            var importSpec = inImportBlock
                ? line
                : line.StartsWith("import ", StringComparison.Ordinal) ? line[7..].Trim() : null;

            if (string.IsNullOrWhiteSpace(importSpec))
            {
                continue;
            }

            var match = GoImportSpecRegex.Match(importSpec);
            if (!match.Success)
            {
                continue;
            }

            var packageName = match.Groups["path"].Value.Split('/').Last();
            var alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : packageName;
            if (alias is "_" or ".")
            {
                continue;
            }

            bindings[alias] = packageName;
            importedPackages.Add(packageName);
        }

        return new GoImportInfo(bindings, importedPackages);
    }

    private static PythonImportInfo ParsePythonImports(string source)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in PythonImportRegex.Matches(source))
        {
            var moduleName = match.Groups["module"].Value;
            var alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : moduleName.Split('.').Last();
            bindings[alias] = moduleName;
        }

        foreach (Match match in PythonFromImportRegex.Matches(source))
        {
            var moduleName = match.Groups["module"].Value;
            foreach (var nameBinding in match.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var aliasParts = nameBinding.Split(" as ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (aliasParts.Length == 0)
                {
                    continue;
                }

                var importedName = aliasParts[0];
                var alias = aliasParts.Length > 1 ? aliasParts[1] : importedName;
                bindings[alias] = $"{moduleName}.{importedName}";
            }
        }

        return new PythonImportInfo(bindings);
    }

    private static IReadOnlyDictionary<(string OwnerId, string Name), string> ParsePythonFieldTypes(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var contexts = BuildPythonContexts(source, fileSymbols);
        var fieldTypes = new Dictionary<(string OwnerId, string Name), string>();

        foreach (var context in contexts)
        {
            if (context.Type is null)
            {
                continue;
            }

            var assignmentMatch = PythonAssignmentTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && assignmentMatch.Groups["owner"].Success)
            {
                fieldTypes[(context.Type.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PythonAliasAssignmentRegex.Match(context.Line);
            if (aliasMatch.Success && aliasMatch.Groups["owner"].Success)
            {
                if (aliasMatch.Groups["sourceOwner"].Success && fieldTypes.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out var fieldType))
                {
                    fieldTypes[(context.Type.Id, aliasMatch.Groups["name"].Value)] = fieldType;
                }
            }
        }

        return fieldTypes;
    }

    private static IReadOnlyDictionary<(string CallableId, string Name), string> ParsePythonCallableTypeHints(string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup)
    {
        var typeHints = new Dictionary<(string CallableId, string Name), string>();
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);

        foreach (var entry in callableByLine)
        {
            var declarationLine = lines[entry.Key - 1];
            var openParen = declarationLine.IndexOf('(');
            var closeParen = declarationLine.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen)
            {
                continue;
            }

            foreach (Match match in PythonCallableParameterRegex.Matches(declarationLine[(openParen + 1)..closeParen]))
            {
                typeHints[(entry.Value.Id, match.Groups["name"].Value)] = match.Groups["type"].Value;
            }
        }

        foreach (var context in BuildPythonContexts(source, fileSymbols))
        {
            if (context.Callable is null)
            {
                continue;
            }

            var assignmentMatch = PythonAssignmentTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && !assignmentMatch.Groups["owner"].Success)
            {
                typeHints[(context.Callable.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PythonAliasAssignmentRegex.Match(context.Line);
            if (!aliasMatch.Success || aliasMatch.Groups["owner"].Success)
            {
                continue;
            }

            string? sourceType = null;
            if (context.Type is not null && aliasMatch.Groups["sourceOwner"].Success)
            {
                fieldTypeLookup.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }
            else
            {
                typeHints.TryGetValue((context.Callable.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                typeHints[(context.Callable.Id, aliasMatch.Groups["name"].Value)] = sourceType;
            }
        }

        return typeHints;
    }

    private static PhpImportInfo ParsePhpImports(string source)
    {
        var typeBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        var functionBindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in PhpUseRegex.Matches(source))
        {
            var path = match.Groups["path"].Value.Replace('\\', '.');
            var alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : path.Split('.').Last();
            if (match.Groups["function"].Success)
            {
                functionBindings[alias] = path;
            }
            else
            {
                typeBindings[alias] = path;
            }
        }

        return new PhpImportInfo(typeBindings, functionBindings);
    }

    private static IReadOnlyDictionary<(string OwnerId, string Name), string> ParsePhpFieldTypes(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var fieldTypes = new Dictionary<(string OwnerId, string Name), string>();

        foreach (var context in BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.PhpTypeRegex, MultiLanguageSymbolParser.PhpFunctionRegex))
        {
            if (context.Type is null)
            {
                continue;
            }

            var declaredFieldMatch = PhpFieldTypeRegex.Match(context.Line);
            if (declaredFieldMatch.Success)
            {
                fieldTypes[(context.Type.Id, declaredFieldMatch.Groups["name"].Value)] = NormalizePhpTypeName(declaredFieldMatch.Groups["type"].Value);
                continue;
            }

            var assignmentMatch = PhpAssignmentNewTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && assignmentMatch.Groups["owner"].Success)
            {
                fieldTypes[(context.Type.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PhpAliasAssignmentRegex.Match(context.Line);
            if (aliasMatch.Success && aliasMatch.Groups["owner"].Success && aliasMatch.Groups["sourceOwner"].Success && fieldTypes.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out var fieldType))
            {
                fieldTypes[(context.Type.Id, aliasMatch.Groups["name"].Value)] = fieldType;
            }
        }

        return fieldTypes;
    }

    private static IReadOnlyDictionary<(string CallableId, string Name), string> ParsePhpCallableTypeHints(string source, IReadOnlyList<SymbolRecord> fileSymbols, IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup)
    {
        var typeHints = new Dictionary<(string CallableId, string Name), string>();
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);

        foreach (var entry in callableByLine)
        {
            var declarationLine = lines[entry.Key - 1];
            var openParen = declarationLine.IndexOf('(');
            var closeParen = declarationLine.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen)
            {
                continue;
            }

            foreach (Match match in PhpCallableParameterRegex.Matches(declarationLine[(openParen + 1)..closeParen]))
            {
                typeHints[(entry.Value.Id, match.Groups["name"].Value)] = NormalizePhpTypeName(match.Groups["type"].Value);
            }
        }

        foreach (var context in BuildBraceContexts(source, fileSymbols, MultiLanguageSymbolParser.PhpTypeRegex, MultiLanguageSymbolParser.PhpFunctionRegex))
        {
            if (context.Callable is null)
            {
                continue;
            }

            var assignmentMatch = PhpAssignmentNewTypeRegex.Match(context.Line);
            if (assignmentMatch.Success && !assignmentMatch.Groups["owner"].Success)
            {
                typeHints[(context.Callable.Id, assignmentMatch.Groups["name"].Value)] = assignmentMatch.Groups["type"].Value;
                continue;
            }

            var aliasMatch = PhpAliasAssignmentRegex.Match(context.Line);
            if (!aliasMatch.Success || aliasMatch.Groups["owner"].Success)
            {
                continue;
            }

            string? sourceType = null;
            if (context.Type is not null && aliasMatch.Groups["sourceOwner"].Success)
            {
                fieldTypeLookup.TryGetValue((context.Type.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }
            else
            {
                typeHints.TryGetValue((context.Callable.Id, aliasMatch.Groups["source"].Value), out sourceType);
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                typeHints[(context.Callable.Id, aliasMatch.Groups["name"].Value)] = sourceType;
            }
        }

        return typeHints;
    }

    private static string NormalizePhpTypeName(string value)
    {
        return value.TrimStart('?', '\\').Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0].Split('\\').Last();
    }

    private static SymbolRecord? ResolvePythonCallableTarget(
        SymbolRecord? currentType,
        SymbolRecord? currentCallable,
        string? qualifier,
        string callableName,
        IReadOnlyDictionary<string, string> importedBindings,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord[]> topLevelFunctionLookup,
        IReadOnlyDictionary<(string ModuleName, string Name), SymbolRecord[]> importedModuleFunctionLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedMemberLookup,
        IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup,
        IReadOnlyDictionary<(string CallableId, string Name), string> callableTypeHintLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            if (currentType is not null && (string.Equals(qualifier, "self", StringComparison.Ordinal) || string.Equals(qualifier, "cls", StringComparison.Ordinal)) &&
                methodLookup.TryGetValue((currentType.Id, callableName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            if (importedBindings.TryGetValue(qualifier, out var importedTarget))
            {
                if (importedModuleFunctionLookup.TryGetValue((importedTarget, callableName), out var importedModuleFunctions))
                {
                    return importedModuleFunctions[0];
                }

                if (importedMemberLookup.TryGetValue(qualifier, out var importedMember) && MultiLanguageSymbolParser.IsTypeSymbol(importedMember) && methodLookup.TryGetValue((importedMember.Id, callableName), out var importedTypeMethods))
                {
                    return importedTypeMethods[0];
                }
            }

            if (typeLookup.TryGetValue(qualifier, out var qualifiedType) && methodLookup.TryGetValue((qualifiedType.Id, callableName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }

            if (currentType is not null && fieldTypeLookup.TryGetValue((currentType.Id, qualifier), out var fieldTypeName) && typeLookup.TryGetValue(fieldTypeName, out var fieldType) && methodLookup.TryGetValue((fieldType.Id, callableName), out var fieldTypeMethods))
            {
                return fieldTypeMethods[0];
            }

            if (currentCallable is not null && callableTypeHintLookup.TryGetValue((currentCallable.Id, qualifier), out var localTypeName) && typeLookup.TryGetValue(localTypeName, out var localType) && methodLookup.TryGetValue((localType.Id, callableName), out var localTypeMethods))
            {
                return localTypeMethods[0];
            }

            return null;
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, callableName), out var methods))
        {
            return methods[0];
        }

        if (importedMemberLookup.TryGetValue(callableName, out var importedMemberTarget))
        {
            return importedMemberTarget;
        }

        if (topLevelFunctionLookup.TryGetValue(callableName, out var topLevelMethods))
        {
            return topLevelMethods[0];
        }

        return typeLookup.TryGetValue(callableName, out var constructorType) ? constructorType : null;
    }

    private static SymbolRecord? ResolvePhpCallableTarget(
        SymbolRecord? currentType,
        SymbolRecord? currentCallable,
        string? qualifier,
        string callableName,
        IReadOnlyDictionary<(string? ParentId, string Name), SymbolRecord[]> methodLookup,
        IReadOnlyDictionary<string, SymbolRecord[]> topLevelFunctionLookup,
        IReadOnlyDictionary<string, SymbolRecord> typeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedTypeLookup,
        IReadOnlyDictionary<string, SymbolRecord> importedFunctionLookup,
        IReadOnlyDictionary<(string OwnerId, string Name), string> fieldTypeLookup,
        IReadOnlyDictionary<(string CallableId, string Name), string> callableTypeHintLookup)
    {
        if (!string.IsNullOrWhiteSpace(qualifier))
        {
            var normalizedQualifier = qualifier.TrimStart('$');
            if (currentType is not null && string.Equals(qualifier, "$this", StringComparison.Ordinal) && methodLookup.TryGetValue((currentType.Id, callableName), out var currentTypeMethods))
            {
                return currentTypeMethods[0];
            }

            if (ResolvePhpTypeTarget(normalizedQualifier, typeLookup, importedTypeLookup) is { } qualifiedType && methodLookup.TryGetValue((qualifiedType.Id, callableName), out var qualifiedTypeMethods))
            {
                return qualifiedTypeMethods[0];
            }

            if (currentType is not null && fieldTypeLookup.TryGetValue((currentType.Id, normalizedQualifier), out var fieldTypeName) && ResolvePhpTypeTarget(fieldTypeName, typeLookup, importedTypeLookup) is { } fieldType && methodLookup.TryGetValue((fieldType.Id, callableName), out var fieldTypeMethods))
            {
                return fieldTypeMethods[0];
            }

            if (currentCallable is not null && callableTypeHintLookup.TryGetValue((currentCallable.Id, normalizedQualifier), out var localTypeName) && ResolvePhpTypeTarget(localTypeName, typeLookup, importedTypeLookup) is { } localType && methodLookup.TryGetValue((localType.Id, callableName), out var localTypeMethods))
            {
                return localTypeMethods[0];
            }

            return null;
        }

        if (currentType is not null && methodLookup.TryGetValue((currentType.Id, callableName), out var methods))
        {
            return methods[0];
        }

        if (importedFunctionLookup.TryGetValue(callableName, out var importedFunction))
        {
            return importedFunction;
        }

        return topLevelFunctionLookup.TryGetValue(callableName, out var topLevelMethods) ? topLevelMethods[0] : null;
    }

    private static SymbolRecord? ResolvePhpTypeTarget(string typeName, IReadOnlyDictionary<string, SymbolRecord> typeLookup, IReadOnlyDictionary<string, SymbolRecord> importedTypeLookup)
    {
        if (importedTypeLookup.TryGetValue(typeName, out var importedType))
        {
            return importedType;
        }

        return typeLookup.TryGetValue(typeName, out var localType) ? localType : null;
    }

    private static IReadOnlyDictionary<(string OwnerId, string Name), string> ParseTypeScriptFieldTypes(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var ownerByLine = fileSymbols
            .Where(MultiLanguageSymbolParser.IsTypeSymbol)
            .ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var fieldTypes = new Dictionary<(string OwnerId, string Name), string>();
        SymbolRecord? currentOwner = null;
        var braceDepth = 0;
        SymbolRecord? pendingType = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (ownerByLine.TryGetValue(lineNumber, out var declaredType))
            {
                pendingType = declaredType;
            }

            var fieldMatch = TypeScriptFieldTypeRegex.Match(line);
            if (currentOwner is not null && fieldMatch.Success)
            {
                fieldTypes[(currentOwner.Id, fieldMatch.Groups["name"].Value)] = fieldMatch.Groups["type"].Value;
            }

            if (currentOwner is not null)
            {
                var newAssignmentMatch = TypeScriptFieldAssignmentNewRegex.Match(line);
                if (newAssignmentMatch.Success)
                {
                    fieldTypes[(currentOwner.Id, newAssignmentMatch.Groups["name"].Value)] = newAssignmentMatch.Groups["type"].Value;
                }

                var aliasAssignmentMatch = TypeScriptFieldAssignmentAliasRegex.Match(line);
                if (aliasAssignmentMatch.Success)
                {
                    var sourceName = aliasAssignmentMatch.Groups["source"].Value;
                    if (aliasAssignmentMatch.Groups["owner"].Success && fieldTypes.TryGetValue((currentOwner.Id, sourceName), out var fieldType))
                    {
                        fieldTypes[(currentOwner.Id, aliasAssignmentMatch.Groups["name"].Value)] = fieldType;
                    }
                }
            }

            var openBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '{');
            var closeBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (pendingType is not null && openBraceCount > closeBraceCount)
            {
                currentOwner = pendingType;
                pendingType = null;
            }

            if (currentOwner is not null && braceDepth == 0)
            {
                currentOwner = null;
            }
        }

        return fieldTypes;
    }

    private static HashSet<string> ParseTypeScriptImports(string currentFilePath, string source)
    {
        return ParseTypeScriptImportBindings(currentFilePath, source).Values.ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> ParseTypeScriptImportBindings(string currentFilePath, string source)
    {
        var currentModule = MultiLanguageSymbolParser.BuildLogicalModuleQualifier(currentFilePath);
        var currentSegments = currentModule.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (currentSegments.Count > 0)
        {
            currentSegments.RemoveAt(currentSegments.Count - 1);
        }

        var importedBindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in TypeScriptImportRegex.Matches(source))
        {
            var importPath = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(importPath) || !importPath.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var resolvedSegments = new List<string>(currentSegments);
            foreach (var segment in importPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (resolvedSegments.Count > 0)
                    {
                        resolvedSegments.RemoveAt(resolvedSegments.Count - 1);
                    }

                    continue;
                }

                resolvedSegments.Add(segment);
            }

            if (resolvedSegments.Count > 0)
            {
                var resolvedModule = string.Join('.', resolvedSegments);
                if (match.Groups["default"].Success)
                {
                    importedBindings[match.Groups["default"].Value] = resolvedModule;
                }

                if (match.Groups["named"].Success)
                {
                    foreach (var binding in match.Groups["named"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var aliasParts = binding.Split(" as ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (aliasParts.Length == 0)
                        {
                            continue;
                        }

                        var alias = aliasParts.Length > 1 ? aliasParts[1] : aliasParts[0];
                        importedBindings[alias] = resolvedModule;
                    }
                }

                if (match.Groups["namespace"].Success)
                {
                    importedBindings[match.Groups["namespace"].Value] = resolvedModule;
                }
            }
        }

        return importedBindings;
    }

    private static ReferenceRecord CreateReference(SymbolRecord target, string? sourceSymbolId, string fileId, int lineNumber, int startColumn, string token, string line)
    {
        return new ReferenceRecord(
            target.Id,
            sourceSymbolId,
            fileId,
            new TextRangeRecord(lineNumber, startColumn, lineNumber, startColumn + token.Length - 1),
            line.Trim());
    }

    private static IReadOnlyList<ScopedLineContext> BuildBraceContexts(string source, IReadOnlyList<SymbolRecord> fileSymbols, Regex typeRegex, Regex callableRegex)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var results = new List<ScopedLineContext>(lines.Length);
        var typeByLine = fileSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var typeScopes = new Stack<ScopedSymbol>();
        ScopedSymbol? callableScope = null;
        SymbolRecord? pendingType = null;
        SymbolRecord? pendingCallable = null;
        var braceDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            while (typeScopes.Count > 0 && braceDepth < typeScopes.Peek().BodyDepth)
            {
                typeScopes.Pop();
            }

            if (callableScope is not null && braceDepth < callableScope.BodyDepth)
            {
                callableScope = null;
            }

            results.Add(new ScopedLineContext(lineNumber, line, typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null, callableScope?.Symbol));

            typeByLine.TryGetValue(lineNumber, out var declaredType);
            callableByLine.TryGetValue(lineNumber, out var declaredCallable);
            var openBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '{');
            var closeBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (declaredType is not null)
            {
                if (openBraceCount > closeBraceCount)
                {
                    typeScopes.Push(new ScopedSymbol(braceDepth, declaredType));
                }
                else
                {
                    pendingType = declaredType;
                }
            }
            else if (pendingType is not null && openBraceCount > closeBraceCount)
            {
                typeScopes.Push(new ScopedSymbol(braceDepth, pendingType));
                pendingType = null;
            }

            if (declaredCallable is not null)
            {
                if (openBraceCount > closeBraceCount)
                {
                    callableScope = new ScopedSymbol(braceDepth, declaredCallable);
                }
                else
                {
                    pendingCallable = declaredCallable;
                }
            }
            else if (pendingCallable is not null && openBraceCount > closeBraceCount)
            {
                callableScope = new ScopedSymbol(braceDepth, pendingCallable);
                pendingCallable = null;
            }
        }

        return results;
    }

    private static IReadOnlyList<ScopedLineContext> BuildGoContexts(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var results = new List<ScopedLineContext>(lines.Length);
        var typeByLine = fileSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableScope = default(ScopedSymbol);
        var currentType = default(SymbolRecord);
        SymbolRecord? pendingCallable = null;
        var braceDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (callableScope is not null && braceDepth < callableScope.BodyDepth)
            {
                callableScope = null;
                currentType = null;
            }

            results.Add(new ScopedLineContext(lineNumber, line, currentType, callableScope?.Symbol));

            callableByLine.TryGetValue(lineNumber, out var declaredCallable);
            var openBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '{');
            var closeBraceCount = MultiLanguageSymbolParser.CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (declaredCallable is not null)
            {
                if (openBraceCount > closeBraceCount)
                {
                    callableScope = new ScopedSymbol(braceDepth, declaredCallable);
                    currentType = declaredCallable.ParentId is not null && typeByLine.Values.FirstOrDefault(symbol => string.Equals(symbol.Id, declaredCallable.ParentId, StringComparison.Ordinal)) is { } parentType
                        ? parentType
                        : null;
                }
                else
                {
                    pendingCallable = declaredCallable;
                }
            }
            else if (pendingCallable is not null && openBraceCount > closeBraceCount)
            {
                callableScope = new ScopedSymbol(braceDepth, pendingCallable);
                currentType = pendingCallable.ParentId is not null && typeByLine.Values.FirstOrDefault(symbol => string.Equals(symbol.Id, pendingCallable.ParentId, StringComparison.Ordinal)) is { } parentType
                    ? parentType
                    : null;
                pendingCallable = null;
            }
        }

        return results;
    }

    private static IReadOnlyList<ScopedLineContext> BuildPythonContexts(string source, IReadOnlyList<SymbolRecord> fileSymbols)
    {
        var lines = MultiLanguageSymbolParser.SplitLines(source);
        var results = new List<ScopedLineContext>(lines.Length);
        var typeByLine = fileSymbols.Where(MultiLanguageSymbolParser.IsTypeSymbol).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var callableByLine = fileSymbols.Where(symbol => symbol.Kind is SymbolKinds.Method or SymbolKinds.Constructor).ToDictionary(symbol => symbol.Range.StartLine, symbol => symbol);
        var typeScopes = new Stack<IndentScope>();
        var callableScopes = new Stack<IndentScope>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            var trimmedLine = line.TrimStart();
            var indent = MultiLanguageSymbolParser.GetIndentWidth(line);

            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("#", StringComparison.Ordinal))
            {
                while (callableScopes.Count > 0 && indent <= callableScopes.Peek().Indent)
                {
                    callableScopes.Pop();
                }

                while (typeScopes.Count > 0 && indent <= typeScopes.Peek().Indent)
                {
                    typeScopes.Pop();
                }
            }

            results.Add(new ScopedLineContext(lineNumber, line, typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null, callableScopes.Count > 0 ? callableScopes.Peek().Symbol : null));

            if (typeByLine.TryGetValue(lineNumber, out var declaredType))
            {
                typeScopes.Push(new IndentScope(indent, declaredType));
            }

            if (callableByLine.TryGetValue(lineNumber, out var declaredCallable))
            {
                callableScopes.Push(new IndentScope(indent, declaredCallable));
            }
        }

        return results;
    }

    private sealed record ScopedLineContext(int LineNumber, string Line, SymbolRecord? Type, SymbolRecord? Callable);

    private sealed record ScopedSymbol(int BodyDepth, SymbolRecord Symbol);

    private sealed record JavaImportInfo(
        IReadOnlyDictionary<string, string> ImportedTypeAliases,
        IReadOnlySet<string> ImportedPackages,
        IReadOnlyDictionary<string, string> ImportedStaticMemberAliases,
        IReadOnlySet<string> ImportedStaticTypes)
    {
        public IEnumerable<string> ImportedTypes => ImportedTypeAliases.Values;

        public IEnumerable<string> ImportedStaticMembers => ImportedStaticMemberAliases.Values;
    }

    private sealed record GoImportInfo(
        IReadOnlyDictionary<string, string> Bindings,
        IReadOnlySet<string> ImportedPackages);

    private sealed record PythonImportInfo(IReadOnlyDictionary<string, string> Bindings);

    private sealed record PhpImportInfo(
        IReadOnlyDictionary<string, string> TypeBindings,
        IReadOnlyDictionary<string, string> FunctionBindings);

    private sealed record IndentScope(int Indent, SymbolRecord Symbol);
}

internal static class MultiLanguageSymbolParser
{
    private static readonly Regex PackageRegex = new(@"^\s*package\s+([A-Za-z_][\w\.]*)\s*;?", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PhpNamespaceRegex = new(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_\\]*)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PythonClassRegex = new(@"^(?<indent>\s*)class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonMethodRegex = new(@"^(?<indent>\s*)(?:async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex JavaOrCSharpTypeRegex = new(@"\b(class|interface|enum|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex JavaOrCSharpMethodRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|internal|static|abstract|virtual|override|sealed|async|final|synchronized|partial|new)\s+)*(?<type>[A-Za-z_][A-Za-z0-9_<>,\[\]?\.\\]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;]*\)\s*(?:\{|=>)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex TypeScriptTypeRegex = new(@"\b(class|interface|enum|type)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFunctionRegex = new(@"^(?<indent>\s*)(?:(?:export|default|async|declare)\s+)*function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptArrowFunctionRegex = new(@"^(?<indent>\s*)(?:(?:export|default|declare)\s+)*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\([^=;]*\)\s*=>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex TypeScriptMethodRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|async|readonly|get|set|override|abstract)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;]*\)\s*(?::[^\{=]+)?\s*(?:\{|;)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptFieldRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|readonly|declare|abstract)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::[^=;]+)?\s*(?:=[^;]+)?;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoPackageRegex = new(@"^\s*package\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoTypeRegex = new(@"^\s*type\s+([A-Za-z_][A-Za-z0-9_]*)\s+(struct|interface|map|chan|func|\[|[A-Za-z_*])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoFuncRegex = new(@"^\s*func\s*(\((?<receiver>[^)]*)\)\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex PhpTypeRegex = new(@"\b(class|interface|trait|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex PhpFunctionRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|final|abstract)\s+)*function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PhpFieldRegex = new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|readonly)\s+)+(?:[A-Za-z_\\][A-Za-z0-9_\\|?]*\s+)?\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]+)?;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ControlKeywords = new(StringComparer.Ordinal)
    {
        "if",
        "for",
        "foreach",
        "while",
        "switch",
        "catch",
        "using",
        "lock",
        "return"
    };

    public static IReadOnlyList<SymbolRecord> Parse(FileRecord file, string source)
    {
        return file.Language switch
        {
            "Python" => ParsePython(file, source),
            "Go" => ParseGo(file, source),
            "TypeScript" => ParseTypeScript(file, source),
            "PHP" => ParsePhp(file, source),
            "Java" => ParseJavaOrCSharp(file, source),
            "C#" => ParseJavaOrCSharp(file, source),
            _ => Array.Empty<SymbolRecord>()
        };
    }

    private static IReadOnlyList<SymbolRecord> ParseJavaOrCSharp(FileRecord file, string source)
    {
        var namespaceName = PackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
        return ParseBraceLanguage(file, source, namespaceName, JavaOrCSharpTypeRegex, JavaOrCSharpMethodRegex, "constructor");
    }

    private static IReadOnlyList<SymbolRecord> ParseTypeScript(FileRecord file, string source)
    {
        var moduleName = BuildLogicalModuleQualifier(file.Path);
        var symbols = new List<SymbolRecord>();
        var moduleSymbol = CreateContainerSymbol(file, Path.GetFileNameWithoutExtension(file.Path), moduleName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);
        symbols.AddRange(ParseBraceLanguage(file, source, moduleName, TypeScriptTypeRegex, TypeScriptMethodRegex, "constructor", TypeScriptFunctionRegex, TypeScriptArrowFunctionRegex, TypeScriptFieldRegex, moduleSymbol.Id, TypeScriptTypeRegex));
        return symbols;
    }

    private static IReadOnlyList<SymbolRecord> ParsePhp(FileRecord file, string source)
    {
        var namespaceName = PhpNamespaceRegex.Match(source) is { Success: true } namespaceMatch
            ? namespaceMatch.Groups[1].Value.Replace('\\', '.')
            : BuildLogicalModuleQualifier(file.Path);
        var symbols = new List<SymbolRecord>();
        var namespaceLine = PhpNamespaceRegex.Match(source) is { Success: true } phpNamespaceMatch ? GetLineNumber(source, phpNamespaceMatch.Index) : 1;
        var namespaceSymbol = CreateContainerSymbol(file, namespaceName.Split('.').Last(), namespaceName, SymbolKinds.Namespace, namespaceLine);
        symbols.Add(namespaceSymbol);
        symbols.AddRange(ParseBraceLanguage(file, source, namespaceName, PhpTypeRegex, PhpFunctionRegex, "__construct", PhpFunctionRegex, fieldRegex: PhpFieldRegex, rootParentId: namespaceSymbol.Id));
        return symbols;
    }

    private static IReadOnlyList<SymbolRecord> ParseGo(FileRecord file, string source)
    {
        var packageName = GoPackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
        var lines = SplitLines(source);
        var symbols = new List<SymbolRecord>();
        var typesByName = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);
        var moduleSymbol = CreateContainerSymbol(file, packageName, packageName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            var typeMatch = GoTypeRegex.Match(line);
            if (typeMatch.Success)
            {
                var typeName = typeMatch.Groups[1].Value;
                var typeQualifiedName = BuildQualifiedName(packageName, typeName);
                var kind = line.Contains("interface", StringComparison.Ordinal) ? SymbolKinds.Interface : SymbolKinds.Struct;
                var symbol = CreateSymbol(file, typeName, typeQualifiedName, kind, moduleSymbol.Id, lineNumber, line);
                symbols.Add(symbol);
                typesByName[typeName] = symbol;
                continue;
            }

            var funcMatch = GoFuncRegex.Match(line);
            if (!funcMatch.Success)
            {
                continue;
            }

            var methodName = funcMatch.Groups["name"].Value;
            var receiverType = ExtractGoReceiverType(funcMatch.Groups["receiver"].Value);
            SymbolRecord? parentSymbol = null;
            var parentId = receiverType is not null && typesByName.TryGetValue(receiverType, out parentSymbol)
                ? parentSymbol.Id
                : null;
            var methodQualifiedName = parentSymbol is not null
                ? BuildQualifiedName(parentSymbol.QualifiedName, methodName)
                : BuildQualifiedName(packageName, methodName);
            symbols.Add(CreateSymbol(file, methodName, methodQualifiedName, SymbolKinds.Method, parentId ?? moduleSymbol.Id, lineNumber, line));
        }

        return symbols;
    }

    private static IReadOnlyList<SymbolRecord> ParsePython(FileRecord file, string source)
    {
        var moduleName = BuildLogicalModuleQualifier(file.Path);
        var lines = SplitLines(source);
        var symbols = new List<SymbolRecord>();
        var classScopes = new Stack<IndentScope>();
        var moduleSymbol = CreateContainerSymbol(file, moduleName.Split('.').Last(), moduleName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);
        var propertiesByQualifiedName = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = GetIndentWidth(line);

            while (classScopes.Count > 0 && indent <= classScopes.Peek().Indent)
            {
                classScopes.Pop();
            }

            var classMatch = PythonClassRegex.Match(line);
            if (classMatch.Success)
            {
                var className = classMatch.Groups["name"].Value;
                var parentSymbol = classScopes.Count > 0 ? classScopes.Peek().Symbol : null;
                var classQualifiedName = parentSymbol is null
                    ? BuildQualifiedName(moduleName, className)
                    : BuildQualifiedName(parentSymbol.QualifiedName, className);
                var symbol = CreateSymbol(file, className, classQualifiedName, SymbolKinds.Class, parentSymbol?.Id ?? moduleSymbol.Id, lineNumber, line);
                symbols.Add(symbol);
                classScopes.Push(new IndentScope(indent, symbol));
                continue;
            }

            var methodMatch = PythonMethodRegex.Match(line);
            if (!methodMatch.Success)
            {
                continue;
            }

            var methodName = methodMatch.Groups["name"].Value;
            var parent = classScopes.Count > 0 ? classScopes.Peek().Symbol : null;
            var kind = parent is not null && string.Equals(methodName, "__init__", StringComparison.Ordinal)
                ? SymbolKinds.Constructor
                : SymbolKinds.Method;
            var methodQualifiedName = parent is null
                ? BuildQualifiedName(moduleName, methodName)
                : BuildQualifiedName(parent.QualifiedName, methodName);
            symbols.Add(CreateSymbol(file, methodName, methodQualifiedName, kind, parent?.Id ?? moduleSymbol.Id, lineNumber, line));

            if (parent is not null)
            {
                continue;
            }
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (classScopes.Count == 0)
            {
            }
            var propertyMatch = Regex.Match(line, @"self\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.CultureInvariant);
            if (!propertyMatch.Success)
            {
                continue;
            }

            SymbolRecord? owner = null;
            for (var lookback = index; lookback >= 0; lookback--)
            {
                var currentLine = lines[lookback];
                var classMatch = PythonClassRegex.Match(currentLine);
                if (!classMatch.Success)
                {
                    continue;
                }

                var candidateName = classMatch.Groups["name"].Value;
                owner = symbols.FirstOrDefault(symbol => symbol.Kind == SymbolKinds.Class && symbol.Name == candidateName && symbol.Range.StartLine == lookback + 1);
                if (owner is not null)
                {
                    break;
                }
            }

            if (owner is null)
            {
                continue;
            }

            var propertyName = propertyMatch.Groups["name"].Value;
            var propertyQualifiedName = BuildQualifiedName(owner.QualifiedName, propertyName);
            if (!propertiesByQualifiedName.Add(propertyQualifiedName))
            {
                continue;
            }

            symbols.Add(CreateSymbol(file, propertyName, propertyQualifiedName, SymbolKinds.Property, owner.Id, index + 1, line));
        }

        return symbols;
    }

    private static IReadOnlyList<SymbolRecord> ParseBraceLanguage(
        FileRecord file,
        string source,
        string rootQualifier,
        Regex typeRegex,
        Regex methodRegex,
        string constructorName,
        Regex? topLevelFunctionRegex = null,
        Regex? alternateTopLevelFunctionRegex = null,
        Regex? fieldRegex = null,
        string? rootParentId = null,
        Regex? typeAliasRegex = null)
    {
        var lines = SplitLines(source);
        var symbols = new List<SymbolRecord>();
        var typeScopes = new Stack<BraceScope>();
        var braceDepth = 0;
        SymbolRecord? pendingTypeScope = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            while (typeScopes.Count > 0 && braceDepth < typeScopes.Peek().BodyDepth)
            {
                typeScopes.Pop();
            }

            var typeMatch = typeRegex.Match(line);
            SymbolRecord? declaredType = null;

            if (typeMatch.Success)
            {
                var typeKindToken = typeMatch.Groups[1].Value;
                var typeName = typeMatch.Groups[2].Value;
                var parentSymbol = typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null;
                var qualifiedName = parentSymbol is null
                    ? BuildQualifiedName(rootQualifier, typeName)
                    : BuildQualifiedName(parentSymbol.QualifiedName, typeName);
                declaredType = CreateSymbol(file, typeName, qualifiedName, MapTypeKind(typeKindToken), parentSymbol?.Id ?? rootParentId, lineNumber, line);
                symbols.Add(declaredType);
            }
            else
            {
                if (typeAliasRegex is not null)
                {
                    var aliasMatch = typeAliasRegex.Match(line);
                    if (aliasMatch.Success)
                    {
                        var aliasName = aliasMatch.Groups[2].Value;
                        var aliasQualifiedName = BuildQualifiedName(rootQualifier, aliasName);
                        symbols.Add(CreateSymbol(file, aliasName, aliasQualifiedName, SymbolKinds.TypeAlias, rootParentId, lineNumber, line));
                        continue;
                    }
                }

                var functionMatch = (typeScopes.Count == 0 ? topLevelFunctionRegex : methodRegex)?.Match(line);
                functionMatch = functionMatch is { Success: true }
                    ? functionMatch
                    : (typeScopes.Count == 0 ? alternateTopLevelFunctionRegex : null)?.Match(line);
                if (functionMatch is { Success: true })
                {
                    var methodName = functionMatch.Groups["name"].Value;

                    if (!ControlKeywords.Contains(methodName))
                    {
                        var parentSymbol = typeScopes.Count > 0 ? typeScopes.Peek().Symbol : null;
                        var kind = parentSymbol is not null &&
                                   (string.Equals(methodName, parentSymbol.Name, StringComparison.Ordinal) ||
                                    string.Equals(methodName, constructorName, StringComparison.Ordinal))
                            ? SymbolKinds.Constructor
                            : SymbolKinds.Method;
                        var qualifiedName = parentSymbol is null
                            ? BuildQualifiedName(rootQualifier, methodName)
                            : BuildQualifiedName(parentSymbol.QualifiedName, methodName);
                        symbols.Add(CreateSymbol(file, methodName, qualifiedName, kind, parentSymbol?.Id ?? rootParentId, lineNumber, line, ExtractAccessibility(line), IsStatic(line), IsAbstract(line), false, IsOverride(line)));
                    }
                }
                else if (fieldRegex is not null && typeScopes.Count > 0 && IsTypeSymbol(typeScopes.Peek().Symbol))
                {
                    var fieldMatch = fieldRegex.Match(line);
                    if (fieldMatch.Success)
                    {
                        var fieldName = fieldMatch.Groups["name"].Value;
                        var parentSymbol = typeScopes.Peek().Symbol;
                        var qualifiedName = BuildQualifiedName(parentSymbol.QualifiedName, fieldName);
                        var memberKind = line.Contains("get ", StringComparison.Ordinal) || line.Contains("set ", StringComparison.Ordinal)
                            ? SymbolKinds.Property
                            : SymbolKinds.Field;
                        symbols.Add(CreateSymbol(file, fieldName, qualifiedName, memberKind, parentSymbol.Id, lineNumber, line, ExtractAccessibility(line), IsStatic(line)));
                    }
                }
            }

            var openBraceCount = CountOccurrences(line, '{');
            var closeBraceCount = CountOccurrences(line, '}');
            braceDepth += openBraceCount - closeBraceCount;

            if (declaredType is not null && openBraceCount > closeBraceCount)
            {
                typeScopes.Push(new BraceScope(braceDepth, declaredType));
            }
            else if (declaredType is not null)
            {
                pendingTypeScope = declaredType;
            }
            else if (pendingTypeScope is not null && openBraceCount > closeBraceCount)
            {
                typeScopes.Push(new BraceScope(braceDepth, pendingTypeScope));
                pendingTypeScope = null;
            }

            if (pendingTypeScope is not null && !string.IsNullOrWhiteSpace(line) && openBraceCount == 0 && closeBraceCount == 0)
            {
                continue;
            }

            while (typeScopes.Count > 0 && braceDepth < typeScopes.Peek().BodyDepth)
            {
                typeScopes.Pop();
            }
        }

        return symbols;
    }

    private static SymbolRecord CreateContainerSymbol(FileRecord file, string name, string qualifiedName, string kind, int lineNumber)
    {
        return CreateSymbol(file, name, qualifiedName, kind, null, lineNumber, name);
    }

    private static SymbolRecord CreateSymbol(
        FileRecord file,
        string name,
        string qualifiedName,
        string kind,
        string? parentId,
        int lineNumber,
        string line,
        string accessibility = "public",
        bool isStatic = false,
        bool isAbstract = false,
        bool isVirtual = false,
        bool isOverride = false)
    {
        var startColumn = Math.Max(1, line.IndexOf(name, StringComparison.Ordinal) + 1);
        var endColumn = Math.Max(startColumn, startColumn + name.Length - 1);
        var stableId = $"text:{file.Language}:{file.Path}:{kind}:{qualifiedName}";
        return new SymbolRecord(
            DeterministicId.CreateSymbolId(stableId),
            name,
            qualifiedName,
            kind,
            file.Id,
            new TextRangeRecord(lineNumber, startColumn, lineNumber, endColumn),
            kind == SymbolKinds.Constructor ? $"{qualifiedName}()" : $"{kind} {qualifiedName}",
            $"{kind} {name} in {file.Language} source.",
            parentId,
            accessibility,
            isStatic,
            isAbstract,
            isVirtual,
            isOverride);
    }

    internal static string[] SplitLines(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    internal static string BuildLogicalModuleQualifier(string filePath)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        var withoutExtension = Path.ChangeExtension(normalizedPath, null)?.Replace('\\', '/') ?? normalizedPath;
        var segments = withoutExtension.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (segments.Count > 0 && IsCommonSourceRootSegment(segments[0]))
        {
            segments.RemoveAt(0);
        }

        if (segments.Count > 0 && string.Equals(segments[^1], "__init__", StringComparison.Ordinal))
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return segments.Count == 0 ? Path.GetFileNameWithoutExtension(filePath) : string.Join('.', segments);
    }

    private static string BuildQualifiedName(string? prefix, string name)
    {
        return string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}.{name}";
    }

    internal static string GetQualifierPrefix(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot <= 0 ? qualifiedName : qualifiedName[..lastDot];
    }

    internal static string GetLanguageFromSymbolId(string symbolId)
    {
        var parts = symbolId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : string.Empty;
    }

    internal static string GetJavaPackageName(FileRecord file, string source)
    {
        return PackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
    }

    internal static string GetGoPackageName(FileRecord file, string source)
    {
        return GoPackageRegex.Match(source) is { Success: true } packageMatch
            ? packageMatch.Groups[1].Value
            : Path.GetFileNameWithoutExtension(file.Path);
    }

    internal static string GetPhpNamespaceName(FileRecord file, string source)
    {
        return PhpNamespaceRegex.Match(source) is { Success: true } namespaceMatch
            ? namespaceMatch.Groups[1].Value.Replace('\\', '.')
            : BuildLogicalModuleQualifier(file.Path);
    }

    private static bool IsCommonSourceRootSegment(string segment)
    {
        return string.Equals(segment, "src", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "lib", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "app", StringComparison.OrdinalIgnoreCase);
    }

    internal static int CountOccurrences(string line, char value)
    {
        var count = 0;

        foreach (var character in line)
        {
            if (character == value)
            {
                count++;
            }
        }

        return count;
    }

    private static string MapTypeKind(string value)
    {
        return value switch
        {
            "class" => SymbolKinds.Class,
            "interface" => SymbolKinds.Interface,
            "enum" => SymbolKinds.Enum,
            "record" => SymbolKinds.Record,
            "struct" => SymbolKinds.Struct,
            "trait" => SymbolKinds.Class,
            _ => SymbolKinds.Class
        };
    }

    internal static bool IsTypeSymbol(SymbolRecord symbol)
    {
        return symbol.Kind is SymbolKinds.Class or SymbolKinds.Record or SymbolKinds.Interface or SymbolKinds.Struct or SymbolKinds.Enum;
    }

    private static int GetLineNumber(string source, int absoluteIndex)
    {
        var line = 1;
        for (var index = 0; index < absoluteIndex && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string ExtractAccessibility(string line)
    {
        if (line.Contains("private", StringComparison.Ordinal))
        {
            return "private";
        }

        if (line.Contains("protected", StringComparison.Ordinal))
        {
            return "protected";
        }

        if (line.Contains("internal", StringComparison.Ordinal))
        {
            return "internal";
        }

        return "public";
    }

    private static bool IsStatic(string line) => line.Contains("static", StringComparison.Ordinal);

    private static bool IsAbstract(string line) => line.Contains("abstract", StringComparison.Ordinal);

    private static bool IsOverride(string line) => line.Contains("override", StringComparison.Ordinal);

    internal static int GetIndentWidth(string line)
    {
        var indent = 0;

        foreach (var character in line)
        {
            if (character == ' ')
            {
                indent++;
                continue;
            }

            if (character == '\t')
            {
                indent += 4;
                continue;
            }

            break;
        }

        return indent;
    }

    private static string? ExtractGoReceiverType(string receiver)
    {
        if (string.IsNullOrWhiteSpace(receiver))
        {
            return null;
        }

        var parts = receiver.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var typeToken = parts.LastOrDefault();
        return typeToken?.TrimStart('*');
    }

    private sealed record BraceScope(int BodyDepth, SymbolRecord Symbol);

    private sealed record IndentScope(int Indent, SymbolRecord Symbol);
}