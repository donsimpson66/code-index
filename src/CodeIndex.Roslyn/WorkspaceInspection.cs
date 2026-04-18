using Microsoft.CodeAnalysis;

namespace CodeIndex.Roslyn;

public sealed record ProjectInspectionResult(
    string Name,
    string? FilePath,
    IReadOnlyList<string> DocumentPaths);

public sealed record WorkspaceInspectionResult(
    string InputPath,
    string InputKind,
    IReadOnlyList<ProjectInspectionResult> Projects);

public sealed class WorkspaceInspector
{
    public async Task<WorkspaceInspectionResult> InspectAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        using var loadedWorkspace = await WorkspaceLoader.LoadAsync(inputPath, cancellationToken);

        return new WorkspaceInspectionResult(
            loadedWorkspace.InputPath,
            loadedWorkspace.InputKind,
            loadedWorkspace.Projects.Select(CreateProjectResult).ToArray());
    }

    private static ProjectInspectionResult CreateProjectResult(Project project)
    {
        var projectDirectory = project.FilePath is null
            ? null
            : Path.GetDirectoryName(project.FilePath);

        var documentPaths = project.Documents
            .Where(document => document.SourceCodeKind == SourceCodeKind.Regular)
            .Select(document => document.FilePath)
            .OfType<string>()
            .Where(path => CSharpSourceDocumentFilter.IsRelevantSourceDocument(path, projectDirectory))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProjectInspectionResult(project.Name, project.FilePath, documentPaths);
    }
}