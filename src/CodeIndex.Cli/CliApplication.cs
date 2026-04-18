using CodeIndex.Core;
using CodeIndex.Roslyn;
using System.CommandLine;
using System.Text.Json;

namespace CodeIndex.Cli;

public sealed class CliRuntime
{
	public Func<string, CancellationToken, Task<WorkspaceInspectionResult>> InspectAsync { get; init; } = async (path, cancellationToken) =>
	{
		var inspector = new WorkspaceInspector();
		return await inspector.InspectAsync(path, cancellationToken);
	};

	public Func<string, bool, CancellationToken, Task<IReadOnlyList<FileRecord>>> BuildFilesAsync { get; init; } = async (path, includeGenerated, cancellationToken) =>
	{
		var builder = new WorkspaceFileIndexBuilder();
		return await builder.BuildAsync(path, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyList<FileRecord>, bool, CancellationToken, Task<IReadOnlyList<SymbolRecord>>> BuildSymbolsAsync { get; init; } = async (path, files, includeGenerated, cancellationToken) =>
	{
		var builder = new WorkspaceSymbolIndexBuilder();
		return await builder.BuildAsync(path, files, includeGenerated, cancellationToken);
	};

	public Func<string, bool, CancellationToken, Task<IReadOnlyList<EdgeRecord>>> BuildEdgesAsync { get; init; } = async (path, includeGenerated, cancellationToken) =>
	{
		var builder = new WorkspaceEdgeIndexBuilder();
		return await builder.BuildAsync(path, includeGenerated, cancellationToken);
	};

	public Action<CodeIndexSnapshot> ValidateSnapshot { get; init; } = snapshot =>
	{
		var validator = new CodeIndexValidator();
		validator.ValidateOrThrow(snapshot);
	};

	public Func<string, CancellationToken, Task<CodeIndexSnapshot>> ReadSnapshotAsync { get; init; } = async (indexDirectory, cancellationToken) =>
	{
		var reader = new CodeIndexReader();
		return await reader.ReadAsync(indexDirectory, cancellationToken);
	};
}

public static class CliApplication
{
	public static async Task<int> RunAsync(string[] args, CliRuntime? runtime = null)
	{
		var parseResult = CreateRootCommand(runtime).Parse(args);
		return await parseResult.InvokeAsync();
	}

	public static RootCommand CreateRootCommand(CliRuntime? runtime = null)
	{
		runtime ??= new CliRuntime();

		var pathArgument = new Argument<string>("path")
		{
			Description = "Path to a .sln or .csproj file to inspect."
		};

		var outputOption = new Option<string>("--out")
		{
			Description = "Directory where code-index artifacts will be written."
		};

		var includeGeneratedOption = new Option<bool>("--include-generated")
		{
			Description = "Include generated C# files such as obj outputs and *.g.cs files."
		};

		var verboseOption = new Option<bool>("--verbose")
		{
			Description = "Write progress information while building the index."
		};

		var indexOption = new Option<string>("--index")
		{
			Description = "Directory containing generated code-index artifacts."
		};

		var limitOption = new Option<int>("--limit")
		{
			Description = "Maximum number of results to return."
		};

		var kindOption = new Option<string>("--kind")
		{
			Description = "Filter results by symbol kind such as class, method, interface, or property."
		};

		var accessibilityOption = new Option<string>("--accessibility")
		{
			Description = "Filter results by accessibility such as public, internal, or private."
		};

		var sortOption = new Option<string>("--sort")
		{
			Description = "Sort mode for query results. find-symbol: ranked, name, accessibility. get-children: name, accessibility, declaration."
		};

		var symbolQueryArgument = new Argument<string>("query")
		{
			Description = "A symbol name, qualified name, or symbol ID."
		};

		var filePathArgument = new Argument<string>("file")
		{
			Description = "A repository-relative file path from the index."
		};

		var startOption = new Option<int>("--start")
		{
			Description = "Start line, inclusive."
		};

		var endOption = new Option<int>("--end")
		{
			Description = "End line, inclusive."
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

			var result = await runtime.InspectAsync(path, cancellationToken);

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
		buildCommand.Add(includeGeneratedOption);
		buildCommand.Add(verboseOption);
		buildCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var path = parseResult.GetValue(pathArgument);
			var outputDirectory = parseResult.GetValue(outputOption);
			var includeGenerated = parseResult.GetValue(includeGeneratedOption);
			var verbose = parseResult.GetValue(verboseOption);

			if (string.IsNullOrWhiteSpace(path))
			{
				throw new InvalidOperationException("A solution or project path is required.");
			}

			if (string.IsNullOrWhiteSpace(outputDirectory))
			{
				throw new InvalidOperationException("An output directory is required. Pass --out <path>.");
			}

			if (verbose)
			{
				Console.WriteLine($"Loading workspace from {Path.GetFullPath(path)}");
				Console.WriteLine(includeGenerated ? "Including generated files." : "Excluding generated files.");
			}

			var files = await runtime.BuildFilesAsync(path, includeGenerated, cancellationToken);

			if (verbose)
			{
				Console.WriteLine($"Indexed {files.Count} files.");
			}

			var symbols = await runtime.BuildSymbolsAsync(path, files, includeGenerated, cancellationToken);

			if (verbose)
			{
				Console.WriteLine($"Indexed {symbols.Count} symbols.");
			}

			var edges = await runtime.BuildEdgesAsync(path, includeGenerated, cancellationToken);

			if (verbose)
			{
				Console.WriteLine($"Indexed {edges.Count} edges.");
			}

			var fullOutputDirectory = Path.GetFullPath(outputDirectory);
			var inputKind = Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase) ? "solution" : "project";
			var meta = CodeIndexMetaFactory.Create(path, inputKind);
			var snapshot = new CodeIndexSnapshot(meta, files, symbols, edges);
			runtime.ValidateSnapshot(snapshot);

			if (verbose)
			{
				Console.WriteLine("Validation passed.");
			}

			var metaOutputPath = Path.Combine(fullOutputDirectory, "code-index.meta.json");
			var filesOutputPath = Path.Combine(fullOutputDirectory, "code-index.files.json");
			var symbolsOutputPath = Path.Combine(fullOutputDirectory, "code-index.symbols.json");
			var edgesOutputPath = Path.Combine(fullOutputDirectory, "code-index.edges.json");

			await CodeIndexJson.WriteToFileAsync(metaOutputPath, meta, cancellationToken);
			await CodeIndexJson.WriteToFileAsync(filesOutputPath, files, cancellationToken);
			await CodeIndexJson.WriteToFileAsync(symbolsOutputPath, symbols, cancellationToken);
			await CodeIndexJson.WriteToFileAsync(edgesOutputPath, edges, cancellationToken);

			Console.WriteLine($"Wrote metadata to {metaOutputPath}");
			Console.WriteLine($"Wrote {files.Count} file records to {filesOutputPath}");
			Console.WriteLine($"Wrote {symbols.Count} symbol records to {symbolsOutputPath}");
			Console.WriteLine($"Wrote {edges.Count} edge records to {edgesOutputPath}");

			return 0;
		});

		var findSymbolCommand = new Command("find-symbol", "Find symbols by name or qualified name from a generated index.");
		findSymbolCommand.Add(symbolQueryArgument);
		findSymbolCommand.Add(indexOption);
		findSymbolCommand.Add(limitOption);
		findSymbolCommand.Add(kindOption);
		findSymbolCommand.Add(accessibilityOption);
		findSymbolCommand.Add(sortOption);
		findSymbolCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, cancellationToken);
			var matches = LimitResults(OrderFindSymbolResults(snapshot.Symbols
				.Where(symbol =>
					string.Equals(symbol.Name, query, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(symbol.QualifiedName, query, StringComparison.OrdinalIgnoreCase) ||
					symbol.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
				.Where(symbol => string.IsNullOrWhiteSpace(kind) || string.Equals(symbol.Kind, kind, StringComparison.OrdinalIgnoreCase))
				.Where(symbol => string.IsNullOrWhiteSpace(accessibility) || string.Equals(symbol.Accessibility, accessibility, StringComparison.OrdinalIgnoreCase)), query, sort), limit)
				.ToArray();

			WriteJson(matches);
			return 0;
		});

		var getSymbolCommand = new Command("get-symbol", "Get a single symbol by ID or qualified name from a generated index.");
		getSymbolCommand.Add(symbolQueryArgument);
		getSymbolCommand.Add(indexOption);
		getSymbolCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, cancellationToken);
			var symbol = snapshot.Symbols.FirstOrDefault(candidate =>
				string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
				string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

			if (symbol is null)
			{
				throw new InvalidOperationException($"No symbol found for query: {query}");
			}

			WriteJson(symbol);
			return 0;
		});

		var getChildrenCommand = new Command("get-children", "Get child symbols for a parent symbol from a generated index.");
		getChildrenCommand.Add(symbolQueryArgument);
		getChildrenCommand.Add(indexOption);
		getChildrenCommand.Add(limitOption);
		getChildrenCommand.Add(kindOption);
		getChildrenCommand.Add(accessibilityOption);
		getChildrenCommand.Add(sortOption);
		getChildrenCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, cancellationToken);
			var parent = snapshot.Symbols.FirstOrDefault(candidate =>
				string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
				string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

			if (parent is null)
			{
				throw new InvalidOperationException($"No symbol found for query: {query}");
			}

			var children = LimitResults(OrderChildResults(snapshot.Edges
				.Where(edge => edge.Type == EdgeTypes.Contains && edge.From == parent.Id)
				.Join(snapshot.Symbols, edge => edge.To, symbol => symbol.Id, (_, symbol) => symbol)
				.Where(symbol => string.IsNullOrWhiteSpace(kind) || string.Equals(symbol.Kind, kind, StringComparison.OrdinalIgnoreCase))
				.Where(symbol => string.IsNullOrWhiteSpace(accessibility) || string.Equals(symbol.Accessibility, accessibility, StringComparison.OrdinalIgnoreCase)), sort), limit)
				.ToArray();

			WriteJson(children);
			return 0;
		});

		var getExcerptCommand = new Command("get-excerpt", "Get exact file lines from a generated index.");
		getExcerptCommand.Add(filePathArgument);
		getExcerptCommand.Add(indexOption);
		getExcerptCommand.Add(startOption);
		getExcerptCommand.Add(endOption);
		getExcerptCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var file = parseResult.GetValue(filePathArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var start = parseResult.GetValue(startOption);
			var end = parseResult.GetValue(endOption);

			if (string.IsNullOrWhiteSpace(file))
			{
				throw new InvalidOperationException("A file path is required.");
			}

			if (start <= 0 || end < start)
			{
				throw new InvalidOperationException("Use positive line numbers and ensure --end is greater than or equal to --start.");
			}

			var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, cancellationToken);
			var fileRecord = snapshot.Files.FirstOrDefault(candidate => string.Equals(candidate.Path, file, StringComparison.OrdinalIgnoreCase));

			if (fileRecord is null)
			{
				throw new InvalidOperationException($"No indexed file found for path: {file}");
			}

			var fullFilePath = Path.Combine(snapshot.Meta.SourceRoot, fileRecord.Path.Replace('/', Path.DirectorySeparatorChar));
			var lines = await File.ReadAllLinesAsync(fullFilePath, cancellationToken);
			var excerpt = Enumerable.Range(start, Math.Min(end, lines.Length) - start + 1)
				.Select(lineNumber => new { line = lineNumber, text = lines[lineNumber - 1] })
				.ToArray();

			WriteJson(excerpt);
			return 0;
		});

		var rootCommand = new RootCommand("CodeIndex CLI");
		rootCommand.Add(inspectCommand);
		rootCommand.Add(buildCommand);
		rootCommand.Add(findSymbolCommand);
		rootCommand.Add(getSymbolCommand);
		rootCommand.Add(getChildrenCommand);
		rootCommand.Add(getExcerptCommand);

		return rootCommand;
	}

	private static async Task<CodeIndexSnapshot> ReadSnapshotAsync(CliRuntime runtime, string? indexDirectory, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(indexDirectory))
		{
			throw new InvalidOperationException("An index directory is required. Pass --index <path>.");
		}

		return await runtime.ReadSnapshotAsync(indexDirectory, cancellationToken);
	}

	private static void WriteJson<T>(T value)
	{
		Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		}));
	}

	private static int GetMatchRank(SymbolRecord symbol, string query)
	{
		if (string.Equals(symbol.QualifiedName, query, StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}

		if (string.Equals(symbol.Id, query, StringComparison.Ordinal))
		{
			return 1;
		}

		if (string.Equals(symbol.Name, query, StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}

		if (symbol.QualifiedName.EndsWith(query, StringComparison.OrdinalIgnoreCase))
		{
			return 3;
		}

		if (symbol.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
		{
			return 4;
		}

		return 5;
	}

	private static int GetKindRank(string kind)
	{
		return kind switch
		{
			"class" => 0,
			"record" => 1,
			"interface" => 2,
			"struct" => 3,
			"enum" => 4,
			"delegate" => 5,
			"namespace" => 6,
			"constructor" => 7,
			"method" => 8,
			"property" => 9,
			"field" => 10,
			"event" => 11,
			_ => 12
		};
	}

	private static int GetAccessibilityRank(string accessibility)
	{
		return accessibility switch
		{
			"public" => 0,
			"protected" => 1,
			"protected internal" => 2,
			"internal" => 3,
			"private protected" => 4,
			"private" => 5,
			_ => 6
		};
	}

	private static IEnumerable<SymbolRecord> OrderFindSymbolResults(IEnumerable<SymbolRecord> symbols, string query, string? sort)
	{
		return NormalizeSort(sort, "ranked") switch
		{
			"ranked" => symbols
				.OrderBy(symbol => GetMatchRank(symbol, query))
				.ThenBy(symbol => GetKindRank(symbol.Kind))
				.ThenBy(symbol => GetAccessibilityRank(symbol.Accessibility))
				.ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
			"name" => symbols
				.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
				.ThenBy(symbol => symbol.Id, StringComparer.Ordinal),
			"accessibility" => symbols
				.OrderBy(symbol => GetAccessibilityRank(symbol.Accessibility))
				.ThenBy(symbol => GetKindRank(symbol.Kind))
				.ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
			_ => throw new InvalidOperationException("Unsupported --sort for find-symbol. Use ranked, name, or accessibility.")
		};
	}

	private static IEnumerable<SymbolRecord> OrderChildResults(IEnumerable<SymbolRecord> symbols, string? sort)
	{
		return NormalizeSort(sort, "name") switch
		{
			"name" => symbols
				.OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
				.ThenBy(symbol => symbol.Id, StringComparer.Ordinal),
			"accessibility" => symbols
				.OrderBy(symbol => GetAccessibilityRank(symbol.Accessibility))
				.ThenBy(symbol => GetKindRank(symbol.Kind))
				.ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
			"declaration" => symbols
				.OrderBy(symbol => symbol.FileId, StringComparer.Ordinal)
				.ThenBy(symbol => symbol.Range.StartLine)
				.ThenBy(symbol => symbol.Range.StartColumn)
				.ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal),
			_ => throw new InvalidOperationException("Unsupported --sort for get-children. Use name, accessibility, or declaration.")
		};
	}

	private static string NormalizeSort(string? sort, string defaultValue)
	{
		return string.IsNullOrWhiteSpace(sort) ? defaultValue : sort.Trim().ToLowerInvariant();
	}

	private static IEnumerable<T> LimitResults<T>(IEnumerable<T> source, int limit)
	{
		return limit > 0 ? source.Take(limit) : source;
	}
}