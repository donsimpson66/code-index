using CodeIndex.Core;
using CodeIndex.Roslyn;
using System.Diagnostics;
using System.CommandLine;
using System.Text;
using System.Text.Json;

namespace CodeIndex.Cli;

public sealed class CliRuntime
{
	private static bool UsesDirectoryIndexing(string path)
	{
		return SourceInputKindDetector.IsDirectoryInput(path);
	}

	public Func<string, CancellationToken, Task<WorkspaceInspectionResult>> InspectAsync { get; init; } = async (path, cancellationToken) =>
	{
		var inspector = new WorkspaceInspector();
		return await inspector.InspectAsync(path, cancellationToken);
	};

	public Func<string, bool, CancellationToken, Task<IReadOnlyList<FileRecord>>> BuildFilesAsync { get; init; } = async (path, includeGenerated, cancellationToken) =>
	{
		if (UsesDirectoryIndexing(path))
		{
			var multiLanguageBuilder = new MultiLanguageFileIndexBuilder();
			return await multiLanguageBuilder.BuildAsync(path, includeGenerated, cancellationToken);
		}

		var builder = new WorkspaceFileIndexBuilder();
		return await builder.BuildAsync(path, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyList<FileRecord>, bool, CancellationToken, Task<IReadOnlyList<SymbolRecord>>> BuildSymbolsAsync { get; init; } = async (path, files, includeGenerated, cancellationToken) =>
	{
		if (UsesDirectoryIndexing(path))
		{
			var multiLanguageBuilder = new MultiLanguageSymbolIndexBuilder();
			return await multiLanguageBuilder.BuildAsync(path, files, includeGenerated, cancellationToken);
		}

		var builder = new WorkspaceSymbolIndexBuilder();
		return await builder.BuildAsync(path, files, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyList<FileRecord>, IReadOnlyCollection<string>, bool, CancellationToken, Task<IReadOnlyList<SymbolRecord>>> BuildSymbolsForFilesAsync { get; init; } = async (path, files, indexedFilePaths, includeGenerated, cancellationToken) =>
	{
		if (UsesDirectoryIndexing(path))
		{
			var multiLanguageBuilder = new MultiLanguageSymbolIndexBuilder();
			return await multiLanguageBuilder.BuildAsync(path, files, indexedFilePaths, includeGenerated, cancellationToken);
		}

		var builder = new WorkspaceSymbolIndexBuilder();
		return await builder.BuildAsync(path, files, indexedFilePaths, includeGenerated, cancellationToken);
	};

	public Func<string, bool, CancellationToken, Task<IReadOnlyList<EdgeRecord>>> BuildEdgesAsync { get; init; } = async (path, includeGenerated, cancellationToken) =>
	{
		if (UsesDirectoryIndexing(path))
		{
			var multiLanguageBuilder = new MultiLanguageEdgeIndexBuilder();
			return await multiLanguageBuilder.BuildAsync(path, includeGenerated, cancellationToken);
		}

		var builder = new WorkspaceEdgeIndexBuilder();
		return await builder.BuildAsync(path, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyCollection<string>, IReadOnlySet<string>, bool, CancellationToken, Task<IReadOnlyList<EdgeRecord>>> BuildEdgesForFilesAsync { get; init; } = async (path, indexedFilePaths, knownSymbolIds, includeGenerated, cancellationToken) =>
	{
		if (UsesDirectoryIndexing(path))
		{
			var multiLanguageBuilder = new MultiLanguageEdgeIndexBuilder();
			return await multiLanguageBuilder.BuildAsync(path, indexedFilePaths, knownSymbolIds, includeGenerated, cancellationToken);
		}

		var builder = new WorkspaceEdgeIndexBuilder();
		return await builder.BuildAsync(path, indexedFilePaths, knownSymbolIds, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyList<FileRecord>, IReadOnlyList<SymbolRecord>, bool, CancellationToken, Task<IReadOnlyList<ReferenceRecord>>> BuildReferencesAsync { get; init; } = async (path, files, symbols, includeGenerated, cancellationToken) =>
	{
		if (UsesDirectoryIndexing(path))
		{
			var multiLanguageBuilder = new MultiLanguageReferenceIndexBuilder();
			return await multiLanguageBuilder.BuildAsync(path, files, symbols, includeGenerated, cancellationToken);
		}

		var builder = new WorkspaceReferenceIndexBuilder();
		return await builder.BuildAsync(path, files, symbols, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyList<FileRecord>, IReadOnlyList<SymbolRecord>, IReadOnlyCollection<string>, bool, CancellationToken, Task<IReadOnlyList<ReferenceRecord>>> BuildReferencesForFilesAsync { get; init; } = async (path, files, symbols, indexedFilePaths, includeGenerated, cancellationToken) =>
	{
		if (UsesDirectoryIndexing(path))
		{
			var multiLanguageBuilder = new MultiLanguageReferenceIndexBuilder();
			return await multiLanguageBuilder.BuildAsync(path, files, symbols, indexedFilePaths, includeGenerated, cancellationToken);
		}

		var builder = new WorkspaceReferenceIndexBuilder();
		return await builder.BuildAsync(path, files, symbols, indexedFilePaths, includeGenerated, cancellationToken);
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
	private static readonly JsonSerializerOptions OutputJsonOptions = QueryResultFormatter.CompactJsonOptions;

	public static async Task<int> RunAsync(string[] args, CliRuntime? runtime = null)
	{
		try
		{
			var parseResult = CreateRootCommand(runtime).Parse(args);
			return await parseResult.InvokeAsync();
		}
		catch (OperationCanceledException)
		{
			Console.Error.WriteLine("The operation was canceled.");
			return 2;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
	}

	public static RootCommand CreateRootCommand(CliRuntime? runtime = null)
	{
		runtime ??= new CliRuntime();
		var buildService = new CodeIndexBuildService(runtime);
		var queryService = new CodeIndexQueryService(runtime);

		var pathArgument = new Argument<string>("path")
		{
			Description = "Path to a .sln, .csproj, or supported source directory."
		};

		var outputOption = new Option<string>("--out")
		{
			Description = "Directory where code-index artifacts will be written."
		};

		var includeGeneratedOption = new Option<bool>("--include-generated")
		{
			Description = "Include generated C# files such as obj outputs and *.g.cs files."
		};

		var includeLocalsOption = new Option<bool>("--include-locals")
		{
			Description = "Include local variable symbols in the index. Locals are excluded by default."
		};

		var skipEmbeddingsOption = new Option<bool>("--skip-embeddings")
		{
			Description = "Skip embedding generation. Semantic search will be unavailable until embeddings are rebuilt."
		};

		var ifChangedOption = new Option<bool>("--if-changed")
		{
			Description = "Skip rewriting artifacts when --out already matches the current file hashes."
		};

		var verboseOption = new Option<bool>("--verbose")
		{
			Description = "Write progress information while building the index."
		};

		var incrementalFromIndexOption = new Option<string>("--incremental-from-index")
		{
			Description = "Existing code-index artifact directory to use as an incremental build baseline."
		};

		var indexOption = new Option<string>("--index")
		{
			Description = "Directory containing generated code-index artifacts."
		};

		var limitOption = new Option<int>("--limit")
		{
			Description = "Maximum number of results to return. Use 0 for no limit."
		};

		var findLimitOption = new Option<int>("--limit")
		{
			Description = "Maximum number of results to return. Use 0 for no limit.",
			DefaultValueFactory = _ => 20
		};

		var childrenLimitOption = new Option<int>("--limit")
		{
			Description = "Maximum number of results to return. Use 0 for no limit.",
			DefaultValueFactory = _ => 40
		};

		var formatOption = new Option<string>("--format")
		{
			Description = "Output format: json (default) or text."
		};

		var fullOption = new Option<bool>("--full")
		{
			Description = "Return full symbol records instead of compact list projections."
		};

		var compactOption = new Option<bool>("--compact")
		{
			Description = "Force compact list projections (default for list queries)."
		};

		var fieldsOption = new Option<string>("--fields")
		{
			Description = "Comma-separated fields to include in JSON output."
		};

		var prettyOption = new Option<bool>("--pretty")
		{
			Description = "Pretty-print JSON output with indentation."
		};

		var kindOption = new Option<string>("--kind")
		{
			Description = "Filter results by symbol kind such as class, method, interface, or property."
		};

		var typeOption = new Option<string>("--type")
		{
			Description = "Filter semantic search results by item type: symbol or file."
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

		var benchmarkSymbolOption = new Option<string>("--symbol")
		{
			Description = "Representative symbol query to compare targeted index retrieval costs."
		};

		var benchmarkFileOption = new Option<string>("--file")
		{
			Description = "Repository-relative file path to compare excerpt retrieval costs."
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

		var buildCommand = new Command("build", "Build the initial file index artifact from a solution, project, or supported source directory.");
		buildCommand.Add(pathArgument);
		buildCommand.Add(outputOption);
		buildCommand.Add(includeGeneratedOption);
		buildCommand.Add(includeLocalsOption);
		buildCommand.Add(skipEmbeddingsOption);
		buildCommand.Add(ifChangedOption);
		buildCommand.Add(verboseOption);
		buildCommand.Add(incrementalFromIndexOption);
		buildCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var path = parseResult.GetValue(pathArgument);
			var outputDirectory = parseResult.GetValue(outputOption);
			var includeGenerated = parseResult.GetValue(includeGeneratedOption);
			var includeLocals = parseResult.GetValue(includeLocalsOption);
			var skipEmbeddings = parseResult.GetValue(skipEmbeddingsOption);
			var ifChanged = parseResult.GetValue(ifChangedOption);
			var verbose = parseResult.GetValue(verboseOption);
			var incrementalFromIndex = parseResult.GetValue(incrementalFromIndexOption);

			if (string.IsNullOrWhiteSpace(path))
			{
				throw new InvalidOperationException("A solution, project, or source directory path is required.");
			}

			if (string.IsNullOrWhiteSpace(outputDirectory))
			{
				throw new InvalidOperationException("An output target is required. Pass --out <path>.");
			}

			if (verbose)
			{
				Console.WriteLine($"Loading source input from {Path.GetFullPath(path)}");
				Console.WriteLine(includeGenerated ? "Including generated files." : "Excluding generated files.");
				Console.WriteLine(includeLocals ? "Including local symbols." : "Excluding local symbols.");
				Console.WriteLine(skipEmbeddings ? "Skipping embeddings." : "Building embeddings.");
			}

			if (verbose && !string.IsNullOrWhiteSpace(incrementalFromIndex))
			{
				Console.WriteLine($"Loaded incremental baseline from {Path.GetFullPath(incrementalFromIndex)}");
			}

			var buildResult = await buildService.BuildAsync(
				new CodeIndexBuildRequest(path, includeGenerated, incrementalFromIndex, includeLocals, skipEmbeddings, ifChanged, outputDirectory),
				cancellationToken);
			var snapshot = buildResult.Snapshot;
			var stats = buildResult.Stats;

			if (verbose)
			{
				Console.WriteLine($"Indexed {stats.FileCount} files.");

				if (stats.SkippedBecauseUnchanged)
				{
					Console.WriteLine("No file changes detected for --if-changed. Skipping artifact rewrite.");
				}
				else if (stats.ReusedIncrementalBaseline)
				{
					Console.WriteLine("No file changes detected against the incremental baseline. Reusing symbols, edges, references, and embeddings.");
					Console.WriteLine($"Reused {stats.SymbolCount} symbols.");
					Console.WriteLine($"Reused {stats.EdgeCount} edges.");
					Console.WriteLine($"Reused {stats.ReferenceCount} references.");
					Console.WriteLine($"Reused {stats.EmbeddingCount} embeddings.");
				}
				else if (stats.UsedIncrementalBaseline)
				{
					Console.WriteLine($"Detected {stats.ChangedFileCount} changed files and {stats.RemovedFileCount} removed files against the incremental baseline.");
					Console.WriteLine($"Rebuilt {stats.RebuiltSymbolCount} symbols across {stats.ChangedFileCount} changed files.");
					Console.WriteLine($"Rebuilt {stats.RebuiltEdgeCount} edges across {stats.RebuiltEdgeFileCount} impacted files.");
					Console.WriteLine($"Rebuilt {stats.RebuiltReferenceCount} references across {stats.RebuiltReferenceFileCount} impacted files.");
					Console.WriteLine($"Rebuilt {stats.EmbeddingCount} embeddings from merged files and symbols.");
				}
				else
				{
					Console.WriteLine($"Indexed {stats.SymbolCount} symbols.");
					Console.WriteLine($"Indexed {stats.EdgeCount} edges.");
					Console.WriteLine($"Indexed {stats.ReferenceCount} references.");
					Console.WriteLine($"Indexed {stats.EmbeddingCount} embeddings.");
				}

				Console.WriteLine("Validation passed.");
			}

			if (!string.IsNullOrWhiteSpace(outputDirectory) && !stats.SkippedBecauseUnchanged)
			{
				var outputPaths = await buildService.WriteSnapshotAsync(snapshot, outputDirectory, cancellationToken);

				Console.WriteLine($"Wrote metadata to {outputPaths.MetaPath}");
				Console.WriteLine($"Wrote {snapshot.Files.Count} file records to {outputPaths.FilesPath}");
				Console.WriteLine($"Wrote {snapshot.Symbols.Count} symbol records to {outputPaths.SymbolsPath}");
				Console.WriteLine($"Wrote {snapshot.Edges.Count} edge records to {outputPaths.EdgesPath}");
				Console.WriteLine($"Wrote {snapshot.References.Count} reference records to {outputPaths.ReferencesPath}");
				Console.WriteLine($"Wrote {snapshot.Embeddings.Count} embedding records to {outputPaths.EmbeddingsPath}");
			}
			else if (stats.SkippedBecauseUnchanged)
			{
				Console.WriteLine($"Index unchanged at {Path.GetFullPath(outputDirectory)}");
			}

			return 0;
		});

		var findSymbolCommand = new Command("find-symbol", "Find symbols by name or qualified name from a generated index.");
		findSymbolCommand.Add(symbolQueryArgument);
		findSymbolCommand.Add(indexOption);
		findSymbolCommand.Add(findLimitOption);
		findSymbolCommand.Add(kindOption);
		findSymbolCommand.Add(accessibilityOption);
		findSymbolCommand.Add(sortOption);
		AddQueryOutputOptions(findSymbolCommand, formatOption, fullOption, compactOption, fieldsOption, prettyOption);
		findSymbolCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var limit = parseResult.GetValue(findLimitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);
			var output = ReadQueryOutputOptions(parseResult, formatOption, fullOption, compactOption, fieldsOption, prettyOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await queryService.ReadSnapshotAsync(indexDirectory, cancellationToken);
			var matches = queryService.FindSymbols(snapshot, new CodeIndexFindSymbolsRequest(query, indexDirectory, limit, kind, accessibility, sort));
			WriteQueryOutput(QueryResultFormatter.PrepareSymbolResults(matches, snapshot, output.Full, output.Fields), output);
			return 0;
		});

		var semanticSearchCommand = new Command("semantic-search", "Search embedded file and symbol text using deterministic vector similarity.");
		semanticSearchCommand.Add(symbolQueryArgument);
		semanticSearchCommand.Add(indexOption);
		semanticSearchCommand.Add(findLimitOption);
		semanticSearchCommand.Add(typeOption);
		AddQueryOutputOptions(semanticSearchCommand, formatOption, fullOption, compactOption, fieldsOption, prettyOption);
		semanticSearchCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var limit = parseResult.GetValue(findLimitOption);
			var itemType = NormalizeOptional(parseResult.GetValue(typeOption));
			var output = ReadQueryOutputOptions(parseResult, formatOption, fullOption, compactOption, fieldsOption, prettyOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A semantic search query is required.");
			}

			var results = await queryService.SemanticSearchAsync(new CodeIndexSemanticSearchRequest(query, indexDirectory, limit, itemType), cancellationToken);
			WriteQueryOutput(QueryResultFormatter.ApplyFields(results, output.Fields), output);
			return 0;
		});

		var findReferencesCommand = new Command("find-references", "Find references for a symbol ID or qualified name from a generated index.");
		findReferencesCommand.Add(symbolQueryArgument);
		findReferencesCommand.Add(indexOption);
		findReferencesCommand.Add(findLimitOption);
		AddQueryOutputOptions(findReferencesCommand, formatOption, fullOption, compactOption, fieldsOption, prettyOption);
		findReferencesCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var limit = parseResult.GetValue(findLimitOption);
			var output = ReadQueryOutputOptions(parseResult, formatOption, fullOption, compactOption, fieldsOption, prettyOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var results = await queryService.FindReferencesAsync(new CodeIndexReferenceQuery(query, indexDirectory, limit), cancellationToken);
			WriteQueryOutput(QueryResultFormatter.ApplyFields(results, output.Fields), output);
			return 0;
		});

		var getSymbolCommand = new Command("get-symbol", "Get a single symbol by ID or qualified name from a generated index.");
		getSymbolCommand.Add(symbolQueryArgument);
		getSymbolCommand.Add(indexOption);
		AddQueryOutputOptions(getSymbolCommand, formatOption, fullOption, compactOption, fieldsOption, prettyOption);
		getSymbolCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var output = ReadQueryOutputOptions(parseResult, formatOption, fullOption, compactOption, fieldsOption, prettyOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await queryService.ReadSnapshotAsync(indexDirectory, cancellationToken);
			var symbol = await queryService.GetSymbolAsync(query, indexDirectory, cancellationToken);
			// get-symbol defaults to full record unless --compact is set.
			var useFull = output.Full || !output.CompactRequested;
			WriteQueryOutput(QueryResultFormatter.PrepareSingleSymbol(symbol, snapshot, useFull, output.Fields), output);
			return 0;
		});

		var getChildrenCommand = new Command("get-children", "Get child symbols for a parent symbol from a generated index.");
		AddRelatedSymbolCommand(getChildrenCommand, symbolQueryArgument, indexOption, childrenLimitOption, kindOption, accessibilityOption, sortOption, formatOption, fullOption, compactOption, fieldsOption, prettyOption,
			async (query, indexDirectory, limit, kind, accessibility, sort, cancellationToken) =>
				await queryService.GetChildrenAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken),
			queryService);

		var getCalleesCommand = new Command("get-callees", "Get call targets for a method or constructor from a generated index.");
		AddRelatedSymbolCommand(getCalleesCommand, symbolQueryArgument, indexOption, childrenLimitOption, kindOption, accessibilityOption, sortOption, formatOption, fullOption, compactOption, fieldsOption, prettyOption,
			async (query, indexDirectory, limit, kind, accessibility, sort, cancellationToken) =>
				await queryService.GetCalleesAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken),
			queryService);

		var getCallersCommand = new Command("get-callers", "Get callers for a method or constructor from a generated index.");
		AddRelatedSymbolCommand(getCallersCommand, symbolQueryArgument, indexOption, childrenLimitOption, kindOption, accessibilityOption, sortOption, formatOption, fullOption, compactOption, fieldsOption, prettyOption,
			async (query, indexDirectory, limit, kind, accessibility, sort, cancellationToken) =>
				await queryService.GetCallersAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken),
			queryService);

		var getTestTargetsCommand = new Command("get-test-targets", "Get heuristic production targets for a test symbol from a generated index.");
		AddRelatedSymbolCommand(getTestTargetsCommand, symbolQueryArgument, indexOption, childrenLimitOption, kindOption, accessibilityOption, sortOption, formatOption, fullOption, compactOption, fieldsOption, prettyOption,
			async (query, indexDirectory, limit, kind, accessibility, sort, cancellationToken) =>
				await queryService.GetTestTargetsAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken),
			queryService);

		var getTestsCommand = new Command("get-tests", "Get heuristic tests for a production symbol from a generated index.");
		AddRelatedSymbolCommand(getTestsCommand, symbolQueryArgument, indexOption, childrenLimitOption, kindOption, accessibilityOption, sortOption, formatOption, fullOption, compactOption, fieldsOption, prettyOption,
			async (query, indexDirectory, limit, kind, accessibility, sort, cancellationToken) =>
				await queryService.GetTestsAsync(new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort), cancellationToken),
			queryService);

		var outlineCommand = new Command("outline", "Get a compact type/member outline for a symbol in one call.");
		outlineCommand.Add(symbolQueryArgument);
		outlineCommand.Add(indexOption);
		outlineCommand.Add(childrenLimitOption);
		outlineCommand.Add(kindOption);
		outlineCommand.Add(accessibilityOption);
		outlineCommand.Add(sortOption);
		AddQueryOutputOptions(outlineCommand, formatOption, fullOption, compactOption, fieldsOption, prettyOption);
		outlineCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var limit = parseResult.GetValue(childrenLimitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption) ?? "declaration";
			var output = ReadQueryOutputOptions(parseResult, formatOption, fullOption, compactOption, fieldsOption, prettyOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await queryService.ReadSnapshotAsync(indexDirectory, cancellationToken);
			var (symbol, children) = await queryService.GetOutlineAsync(
				new CodeIndexChildQueryRequest(query, indexDirectory, limit, kind, accessibility, sort),
				cancellationToken);
			var payload = new
			{
				symbol = QueryResultFormatter.PrepareSingleSymbol(symbol, snapshot, output.Full, null),
				children = QueryResultFormatter.PrepareSymbolResults(children, snapshot, output.Full, null)
			};
			WriteQueryOutput(QueryResultFormatter.ApplyFields(payload, output.Fields), output);
			return 0;
		});

		var getExcerptCommand = new Command("get-excerpt", "Get exact file lines from a generated index.");
		getExcerptCommand.Add(filePathArgument);
		getExcerptCommand.Add(indexOption);
		getExcerptCommand.Add(startOption);
		getExcerptCommand.Add(endOption);
		AddQueryOutputOptions(getExcerptCommand, formatOption, fullOption, compactOption, fieldsOption, prettyOption);
		getExcerptCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var file = parseResult.GetValue(filePathArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var start = parseResult.GetValue(startOption);
			var end = parseResult.GetValue(endOption);
			var output = ReadQueryOutputOptions(parseResult, formatOption, fullOption, compactOption, fieldsOption, prettyOption);

			if (string.IsNullOrWhiteSpace(file))
			{
				throw new InvalidOperationException("A file path is required.");
			}

			var excerpt = await queryService.GetExcerptAsync(new CodeIndexExcerptQuery(file, indexDirectory, start, end), cancellationToken);
			WriteQueryOutput(QueryResultFormatter.ApplyFields(excerpt, output.Fields), output);
			return 0;
		});

		var benchmarkCommand = new Command("benchmark", "Compare reading the indexed project directly versus using code-index artifacts first.");
		benchmarkCommand.Add(indexOption);
		benchmarkCommand.Add(benchmarkSymbolOption);
		benchmarkCommand.Add(benchmarkFileOption);
		benchmarkCommand.Add(startOption);
		benchmarkCommand.Add(endOption);
		benchmarkCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var indexDirectory = parseResult.GetValue(indexOption);
			var symbolQuery = parseResult.GetValue(benchmarkSymbolOption);
			var file = parseResult.GetValue(benchmarkFileOption);
			var start = parseResult.GetValue(startOption);
			var end = parseResult.GetValue(endOption);

			if (string.IsNullOrWhiteSpace(file) && (start > 0 || end > 0))
			{
				throw new InvalidOperationException("Pass --file when using --start or --end with benchmark.");
			}

			if (!string.IsNullOrWhiteSpace(file) && (start <= 0 || end < start))
			{
				throw new InvalidOperationException("Use positive line numbers and ensure --end is greater than or equal to --start when benchmarking an excerpt.");
			}

			var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, cancellationToken);
			var fullIndexDirectory = Path.GetFullPath(indexDirectory!);
			var meta = snapshot.Meta;
			var files = snapshot.Files;
			var symbolCount = snapshot.Symbols.Count;
			var edgeCount = snapshot.Edges.Count;
			var indexLoadStopwatch = Stopwatch.StartNew();

			indexLoadStopwatch.Stop();

			var sourceScanStopwatch = Stopwatch.StartNew();
			var sourceFiles = files
				.Select(fileRecord => new
				{
					Record = fileRecord,
					FullPath = Path.Combine(meta.SourceRoot, fileRecord.Path.Replace('/', Path.DirectorySeparatorChar))
				})
				.ToArray();

			foreach (var sourceFile in sourceFiles)
			{
				if (!File.Exists(sourceFile.FullPath))
				{
					throw new InvalidOperationException($"Indexed source file no longer exists: {sourceFile.Record.Path}");
				}
			}

			var totalSourceBytes = sourceFiles.Sum(sourceFile => new FileInfo(sourceFile.FullPath).Length);
			var totalSourceLines = sourceFiles.Sum(sourceFile => File.ReadLines(sourceFile.FullPath).Count());
			sourceScanStopwatch.Stop();

			long metaBytes = 0;
			long filesBytes = 0;
			long symbolsBytes = 0;
			long edgesBytes = 0;
			long referencesBytes = 0;
			long embeddingsBytes = 0;
			long totalIndexBytes = 0;

			var metaPath = Path.Combine(fullIndexDirectory, "code-index.meta.json");
			var filesPath = Path.Combine(fullIndexDirectory, "code-index.files.json");
			var symbolsPath = Path.Combine(fullIndexDirectory, "code-index.symbols.json");
			var edgesPath = Path.Combine(fullIndexDirectory, "code-index.edges.json");
			var referencesPath = Path.Combine(fullIndexDirectory, "code-index.references.json");
			var embeddingsPath = Path.Combine(fullIndexDirectory, "code-index.embeddings.json");

			metaBytes = new FileInfo(metaPath).Length;
			filesBytes = new FileInfo(filesPath).Length;
			symbolsBytes = new FileInfo(symbolsPath).Length;
			edgesBytes = new FileInfo(edgesPath).Length;
			referencesBytes = File.Exists(referencesPath) ? new FileInfo(referencesPath).Length : 0;
			embeddingsBytes = File.Exists(embeddingsPath) ? new FileInfo(embeddingsPath).Length : 0;
			totalIndexBytes = metaBytes + filesBytes + symbolsBytes + edgesBytes + referencesBytes + embeddingsBytes;

			object? symbolQueryMetrics = null;
			long indexFirstFlowBytes = 0;
			long indexFirstFlowElapsedMs = 0;

			if (!string.IsNullOrWhiteSpace(symbolQuery))
			{
				var symbolQueryStopwatch = Stopwatch.StartNew();
				var matches = LimitResults(OrderFindSymbolResults(snapshot.Symbols
					.Where(symbol => !string.Equals(symbol.Kind, SymbolKinds.Local, StringComparison.Ordinal))
					.Where(symbol =>
						string.Equals(symbol.Name, symbolQuery, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(symbol.QualifiedName, symbolQuery, StringComparison.OrdinalIgnoreCase) ||
						symbol.QualifiedName.Contains(symbolQuery, StringComparison.OrdinalIgnoreCase)), symbolQuery, "ranked"), 5)
					.ToArray();

				var selected = matches.FirstOrDefault();
				var children = selected is null
					? Array.Empty<SymbolRecord>()
					: OrderChildResults(snapshot.Edges
						.Where(edge => edge.Type == EdgeTypes.Contains && edge.From == selected.Id)
						.Join(snapshot.Symbols, edge => edge.To, symbol => symbol.Id, (_, symbol) => symbol)
						.Where(symbol => !string.Equals(symbol.Kind, SymbolKinds.Local, StringComparison.Ordinal)), "declaration")
						.ToArray();

				var findSymbolBytes = GetSerializedByteCount(QueryResultFormatter.ProjectSymbols(matches, snapshot));
				var getSymbolBytes = selected is null ? 0 : GetSerializedByteCount(selected);
				var getChildrenBytes = children.Length == 0 ? 0 : GetSerializedByteCount(QueryResultFormatter.ProjectSymbols(children, snapshot));
				symbolQueryStopwatch.Stop();
				indexFirstFlowBytes += findSymbolBytes + getSymbolBytes + getChildrenBytes;
				indexFirstFlowElapsedMs += symbolQueryStopwatch.ElapsedMilliseconds;

				symbolQueryMetrics = new
				{
					query = symbolQuery,
					matchCount = matches.Length,
					elapsedMs = symbolQueryStopwatch.ElapsedMilliseconds,
					findSymbolBytes,
					findSymbolEstimatedTokens = EstimateTokenCount(findSymbolBytes),
					selectedSymbolId = selected?.Id,
					selectedQualifiedName = selected?.QualifiedName,
					getSymbolBytes,
					getSymbolEstimatedTokens = EstimateTokenCount(getSymbolBytes),
					getChildrenBytes,
					getChildrenEstimatedTokens = EstimateTokenCount(getChildrenBytes)
				};
			}

			object? excerptMetrics = null;

			if (!string.IsNullOrWhiteSpace(file))
			{
				var excerptStopwatch = Stopwatch.StartNew();
				var fileRecord = files.FirstOrDefault(candidate => string.Equals(candidate.Path, file, StringComparison.OrdinalIgnoreCase));

				if (fileRecord is null)
				{
					throw new InvalidOperationException($"No indexed file found for path: {file}");
				}

				var fullFilePath = Path.Combine(meta.SourceRoot, fileRecord.Path.Replace('/', Path.DirectorySeparatorChar));
				var lines = await File.ReadAllLinesAsync(fullFilePath, cancellationToken);
				var excerpt = Enumerable.Range(start, Math.Min(end, lines.Length) - start + 1)
					.Select(lineNumber => new { line = lineNumber, text = lines[lineNumber - 1] })
					.ToArray();

				var excerptBytes = GetSerializedByteCount(excerpt);
				excerptStopwatch.Stop();
				indexFirstFlowBytes += excerptBytes;
				indexFirstFlowElapsedMs += excerptStopwatch.ElapsedMilliseconds;

				excerptMetrics = new
				{
					file = fileRecord.Path,
					start,
					end,
					elapsedMs = excerptStopwatch.ElapsedMilliseconds,
					excerptBytes,
					excerptEstimatedTokens = EstimateTokenCount(excerptBytes)
				};
			}

			var rawSourceEstimatedTokens = EstimateTokenCount(totalSourceBytes);
			var totalIndexEstimatedTokens = EstimateTokenCount(totalIndexBytes);
			var indexFirstFlowEstimatedTokens = indexFirstFlowBytes == 0 ? (int?)null : EstimateTokenCount(indexFirstFlowBytes);

			var benchmark = new
			{
				inputPath = meta.InputPath,
				inputKind = meta.InputKind,
				sourceRoot = meta.SourceRoot,
				rawSource = new
				{
					fileCount = files.Count,
					elapsedMs = sourceScanStopwatch.ElapsedMilliseconds,
					totalBytes = totalSourceBytes,
					totalEstimatedTokens = rawSourceEstimatedTokens,
					totalLines = totalSourceLines,
					averageBytesPerFile = files.Count == 0 ? 0 : totalSourceBytes / files.Count
				},
				indexArtifacts = new
				{
					directory = fullIndexDirectory,
					loadElapsedMs = indexLoadStopwatch.ElapsedMilliseconds,
					metaBytes,
					filesBytes,
					symbolsBytes,
					edgesBytes,
					referencesBytes,
					embeddingsBytes,
					totalBytes = totalIndexBytes,
					totalEstimatedTokens = totalIndexEstimatedTokens,
					symbolCount = symbolCount,
					edgeCount = edgeCount
				},
				wholeProjectComparison = new
				{
					indexToSourceByteRatio = totalSourceBytes == 0 ? 0 : Math.Round((double)totalIndexBytes / totalSourceBytes, 3),
					recommendation = totalIndexBytes <= totalSourceBytes
						? "Reading the full index may be competitive, but index-first still works best when queries are selective."
						: "Do not read the full index by default. Use the index selectively to narrow symbol and excerpt retrieval before opening source files."
				},
				symbolQuery = symbolQueryMetrics,
				excerptQuery = excerptMetrics,
				indexFirstFlowBytes = indexFirstFlowBytes == 0 ? (long?)null : indexFirstFlowBytes,
				indexFirstFlowEstimatedTokens = indexFirstFlowEstimatedTokens,
				indexFirstFlowElapsedMs = indexFirstFlowBytes == 0 ? (long?)null : indexFirstFlowElapsedMs,
				indexFirstVsFullSourceRatio = indexFirstFlowBytes == 0 || totalSourceBytes == 0 ? (double?)null : Math.Round((double)indexFirstFlowBytes / totalSourceBytes, 3)
			};

			WriteJson(benchmark);
			return 0;
		});

		var rootCommand = new RootCommand("CodeIndex CLI");
		rootCommand.Add(inspectCommand);
		rootCommand.Add(buildCommand);
		rootCommand.Add(benchmarkCommand);
		rootCommand.Add(findSymbolCommand);
		rootCommand.Add(semanticSearchCommand);
		rootCommand.Add(findReferencesCommand);
		rootCommand.Add(getSymbolCommand);
		rootCommand.Add(getChildrenCommand);
		rootCommand.Add(getCalleesCommand);
		rootCommand.Add(getCallersCommand);
		rootCommand.Add(getTestTargetsCommand);
		rootCommand.Add(getTestsCommand);
		rootCommand.Add(outlineCommand);
		rootCommand.Add(getExcerptCommand);

		return rootCommand;
	}

	private static void AddQueryOutputOptions(
		Command command,
		Option<string> formatOption,
		Option<bool> fullOption,
		Option<bool> compactOption,
		Option<string> fieldsOption,
		Option<bool> prettyOption)
	{
		command.Add(formatOption);
		command.Add(fullOption);
		command.Add(compactOption);
		command.Add(fieldsOption);
		command.Add(prettyOption);
	}

	private static QueryOutputOptions ReadQueryOutputOptions(
		ParseResult parseResult,
		Option<string> formatOption,
		Option<bool> fullOption,
		Option<bool> compactOption,
		Option<string> fieldsOption,
		Option<bool> prettyOption)
	{
		return new QueryOutputOptions(
			parseResult.GetValue(formatOption),
			parseResult.GetValue(fullOption),
			parseResult.GetValue(compactOption),
			parseResult.GetValue(fieldsOption),
			parseResult.GetValue(prettyOption));
	}

	private static void WriteQueryOutput(object value, QueryOutputOptions options)
	{
		Console.WriteLine(QueryResultFormatter.Format(value, options.Format, options.Pretty));
	}

	private static void AddRelatedSymbolCommand(
		Command command,
		Argument<string> symbolQueryArgument,
		Option<string> indexOption,
		Option<int> limitOption,
		Option<string> kindOption,
		Option<string> accessibilityOption,
		Option<string> sortOption,
		Option<string> formatOption,
		Option<bool> fullOption,
		Option<bool> compactOption,
		Option<string> fieldsOption,
		Option<bool> prettyOption,
		Func<string, string?, int, string?, string?, string?, CancellationToken, Task<IReadOnlyList<SymbolRecord>>> execute,
		CodeIndexQueryService queryService)
	{
		command.Add(symbolQueryArgument);
		command.Add(indexOption);
		command.Add(limitOption);
		command.Add(kindOption);
		command.Add(accessibilityOption);
		command.Add(sortOption);
		AddQueryOutputOptions(command, formatOption, fullOption, compactOption, fieldsOption, prettyOption);
		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);
			var output = ReadQueryOutputOptions(parseResult, formatOption, fullOption, compactOption, fieldsOption, prettyOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await queryService.ReadSnapshotAsync(indexDirectory, cancellationToken);
			var results = await execute(query!, indexDirectory, limit, kind, accessibility, sort, cancellationToken);
			var useFull = output.Full && !output.CompactRequested;
			WriteQueryOutput(QueryResultFormatter.PrepareSymbolResults(results, snapshot, useFull, output.Fields), output);
			return 0;
		});
	}

	private sealed record QueryOutputOptions(string? Format, bool Full, bool CompactRequested, string? Fields, bool Pretty);

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
		Console.WriteLine(JsonSerializer.Serialize(value, OutputJsonOptions));
	}

	private static void WriteJsonPretty<T>(T value)
	{
		Console.WriteLine(JsonSerializer.Serialize(value, QueryResultFormatter.PrettyJsonOptions));
	}

	private static object CreateSemanticSearchResult(CodeIndexSnapshot snapshot, EmbeddingSearchResult result)
	{
		var filesById = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
		var symbolsById = snapshot.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);

		if (string.Equals(result.ItemType, EmbeddingItemTypes.File, StringComparison.Ordinal))
		{
			var file = filesById[result.ItemId];
			return new
			{
				itemType = result.ItemType,
				itemId = result.ItemId,
				score = Math.Round(result.Score, 4),
				path = file.Path,
				projectName = file.ProjectName,
				language = file.Language,
				summary = file.Summary
			};
		}

		var symbol = symbolsById[result.ItemId];
		return new
		{
			itemType = result.ItemType,
			itemId = result.ItemId,
			score = Math.Round(result.Score, 4),
			name = symbol.Name,
			qualifiedName = symbol.QualifiedName,
			kind = symbol.Kind,
			fileId = symbol.FileId,
			parentId = symbol.ParentId,
			signature = symbol.Signature,
			summary = symbol.Summary
		};
	}

	private static int GetSerializedByteCount<T>(T value)
	{
		return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value, OutputJsonOptions));
	}

	private static int EstimateTokenCount(long byteCount)
	{
		if (byteCount <= 0)
		{
			return 0;
		}

		return (int)Math.Ceiling(byteCount / 4.0);
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

	private static string? NormalizeOptional(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
	}


	private static int GetKindRank(string kind)
	{
		return SymbolKinds.GetRank(kind);
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