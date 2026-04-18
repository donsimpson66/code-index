using CodeIndex.Core;
using CodeIndex.Roslyn;
using System.CommandLine;

var pathArgument = new Argument<string>("path")
{
	Description = "Path to a .sln or .csproj file to inspect."
};

var outputOption = new Option<string>("--out")
{
	Description = "Directory where code-index artifacts will be written."
};

var inspectCommand = new Command("inspect", "Load a solution or project and list discovered C# projects and source documents.");
inspectCommand.Add(pathArgument);
inspectCommand.SetAction(async (parseResult, cancellationToken) =>
{
	var path = parseResult.GetValue(pathArgument);

	if (string.IsNullOrWhiteSpace(path))
	{
		throw new InvalidOperationException("A solution or project path is required.");
	}

	var inspector = new WorkspaceInspector();
	var result = await inspector.InspectAsync(path, cancellationToken);

	Console.WriteLine($"Input: {result.InputPath}");
	Console.WriteLine($"Kind: {result.InputKind}");
	Console.WriteLine($"Projects: {result.Projects.Count}");

	foreach (var project in result.Projects)
	{
		Console.WriteLine($"PROJECT {project.Name}");

		if (!string.IsNullOrWhiteSpace(project.FilePath))
		{
			Console.WriteLine($"  File: {project.FilePath}");
		}

		foreach (var documentPath in project.DocumentPaths)
		{
			Console.WriteLine($"  DOC {documentPath}");
		}
	}

	return 0;
});

var buildCommand = new Command("build", "Build the initial file index artifact from a solution or project.");
buildCommand.Add(pathArgument);
buildCommand.Add(outputOption);
buildCommand.SetAction(async (parseResult, cancellationToken) =>
{
	var path = parseResult.GetValue(pathArgument);
	var outputDirectory = parseResult.GetValue(outputOption);

	if (string.IsNullOrWhiteSpace(path))
	{
		throw new InvalidOperationException("A solution or project path is required.");
	}

	if (string.IsNullOrWhiteSpace(outputDirectory))
	{
		throw new InvalidOperationException("An output directory is required. Pass --out <path>.");
	}

	var fileBuilder = new WorkspaceFileIndexBuilder();
	var symbolBuilder = new WorkspaceSymbolIndexBuilder();
	var files = await fileBuilder.BuildAsync(path, cancellationToken);
	var symbols = await symbolBuilder.BuildAsync(path, files, cancellationToken);
	var fullOutputDirectory = Path.GetFullPath(outputDirectory);
	var inputKind = Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase) ? "solution" : "project";
	var meta = CodeIndexMetaFactory.Create(path, inputKind);
	var metaOutputPath = Path.Combine(fullOutputDirectory, "code-index.meta.json");
	var filesOutputPath = Path.Combine(fullOutputDirectory, "code-index.files.json");
	var symbolsOutputPath = Path.Combine(fullOutputDirectory, "code-index.symbols.json");

	await CodeIndexJson.WriteToFileAsync(metaOutputPath, meta, cancellationToken);
	await CodeIndexJson.WriteToFileAsync(filesOutputPath, files, cancellationToken);
	await CodeIndexJson.WriteToFileAsync(symbolsOutputPath, symbols, cancellationToken);

	Console.WriteLine($"Wrote metadata to {metaOutputPath}");
	Console.WriteLine($"Wrote {files.Count} file records to {filesOutputPath}");
	Console.WriteLine($"Wrote {symbols.Count} symbol records to {symbolsOutputPath}");

	return 0;
});

var rootCommand = new RootCommand("CodeIndex CLI");
rootCommand.Add(inspectCommand);
rootCommand.Add(buildCommand);

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
