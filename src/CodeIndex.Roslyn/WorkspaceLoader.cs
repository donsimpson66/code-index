using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeIndex.Roslyn;

internal sealed class LoadedWorkspace : IDisposable
{
    public LoadedWorkspace(MSBuildWorkspace workspace, string inputPath, string inputKind, IReadOnlyList<Project> projects)
    {
        Workspace = workspace;
        InputPath = inputPath;
        InputKind = inputKind;
        Projects = projects;
    }

    public MSBuildWorkspace Workspace { get; }

    public string InputPath { get; }

    public string InputKind { get; }

    public IReadOnlyList<Project> Projects { get; }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}

internal static class WorkspaceLoader
{
    private static readonly Lock RegistrationLock = new();
    private static bool _isMsBuildRegistered;

    public static async Task<LoadedWorkspace> LoadAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        var fullPath = Path.GetFullPath(inputPath);
        var extension = Path.GetExtension(fullPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Input path does not exist: {fullPath}", fullPath);
        }

        EnsureMsBuildRegistered();

        var workspace = MSBuildWorkspace.Create();
        var failures = new List<string>();
        workspace.RegisterWorkspaceFailedHandler(args => failures.Add(args.Diagnostic.Message));

        try
        {
            var projects = extension.ToLowerInvariant() switch
            {
                ".sln" => await LoadSolutionProjectsAsync(workspace, fullPath, cancellationToken),
                ".csproj" => await LoadProjectAsync(workspace, fullPath, cancellationToken),
                _ => throw new NotSupportedException("Only .sln and .csproj inputs are supported.")
            };

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
            }

            return new LoadedWorkspace(
                workspace,
                fullPath,
                extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ? "solution" : "project",
                projects);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    public static string GetSourceRoot(string inputPath)
    {
        return Path.GetDirectoryName(Path.GetFullPath(inputPath))
            ?? throw new InvalidOperationException($"Could not determine source root for {inputPath}");
    }

    private static void EnsureMsBuildRegistered()
    {
        if (_isMsBuildRegistered || MSBuildLocator.IsRegistered)
        {
            _isMsBuildRegistered = true;
            return;
        }

        lock (RegistrationLock)
        {
            if (_isMsBuildRegistered || MSBuildLocator.IsRegistered)
            {
                _isMsBuildRegistered = true;
                return;
            }

            var instance = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();

            if (instance is null)
            {
                throw new InvalidOperationException("No MSBuild instance was found. Install the .NET SDK or MSBuild tooling.");
            }

            MSBuildLocator.RegisterInstance(instance);
            _isMsBuildRegistered = true;
        }
    }

    private static async Task<IReadOnlyList<Project>> LoadSolutionProjectsAsync(
        MSBuildWorkspace workspace,
        string solutionPath,
        CancellationToken cancellationToken)
    {
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);

        return solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<Project>> LoadProjectAsync(
        MSBuildWorkspace workspace,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);

        if (project.Language != LanguageNames.CSharp)
        {
            throw new InvalidOperationException($"Project is not a C# project: {project.FilePath}");
        }

        return new[] { project };
    }
}

internal static class CSharpSourceDocumentFilter
{
    public static bool IsRelevantSourceDocument(string path, string? projectDirectory)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);

        if (fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains($"{Path.DirectorySeparatorChar}.nuget{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (projectDirectory is null)
        {
            return true;
        }

        var fullProjectDirectory = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.StartsWith(fullProjectDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}