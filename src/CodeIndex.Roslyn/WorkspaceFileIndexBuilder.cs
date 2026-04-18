using CodeIndex.Core;

namespace CodeIndex.Roslyn;

public sealed class WorkspaceFileIndexBuilder
{
    private readonly WorkspaceInspector _workspaceInspector = new();

    public async Task<IReadOnlyList<FileRecord>> BuildAsync(string inputPath, bool includeGenerated = false, CancellationToken cancellationToken = default)
    {
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);
        var sourceRoot = WorkspaceLoader.GetSourceRoot(loadedWorkspace.InputPath);
        var fileRecords = new List<FileRecord>();

        foreach (var project in loadedWorkspace.Projects)
        {
            var projectDirectory = project.FilePath is null ? null : Path.GetDirectoryName(project.FilePath);

            foreach (var document in project.Documents)
            {
                if (document.FilePath is null || !CSharpSourceDocumentFilter.IsRelevantSourceDocument(document.FilePath, projectDirectory, includeGenerated))
                {
                    continue;
                }

                var documentPath = document.FilePath;
                var normalizedPath = PathNormalization.NormalizeRelativePath(sourceRoot, documentPath);
                var hash = await FileHashProvider.ComputeSha256Async(documentPath, cancellationToken);
                var summary = await CSharpFileSummaryGenerator.CreateSummaryAsync(document, normalizedPath, cancellationToken);

                fileRecords.Add(new FileRecord(
                    DeterministicId.CreateFileId(normalizedPath),
                    normalizedPath,
                    project.Name,
                    "C#",
                    hash,
                    summary));
            }
        }

        return fileRecords
            .OrderBy(record => record.Path, StringComparer.Ordinal)
            .ToArray();
    }
}