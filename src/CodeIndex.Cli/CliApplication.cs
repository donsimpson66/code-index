using CodeIndex.Core;
using CodeIndex.Roslyn;
using System.Diagnostics;
using System.CommandLine;
using System.Text;
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

	public Func<string, IReadOnlyList<FileRecord>, IReadOnlyCollection<string>, bool, CancellationToken, Task<IReadOnlyList<SymbolRecord>>> BuildSymbolsForFilesAsync { get; init; } = async (path, files, indexedFilePaths, includeGenerated, cancellationToken) =>
	{
		var builder = new WorkspaceSymbolIndexBuilder();
		return await builder.BuildAsync(path, files, indexedFilePaths, includeGenerated, cancellationToken);
	};

	public Func<string, bool, CancellationToken, Task<IReadOnlyList<EdgeRecord>>> BuildEdgesAsync { get; init; } = async (path, includeGenerated, cancellationToken) =>
	{
		var builder = new WorkspaceEdgeIndexBuilder();
		return await builder.BuildAsync(path, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyCollection<string>, IReadOnlySet<string>, bool, CancellationToken, Task<IReadOnlyList<EdgeRecord>>> BuildEdgesForFilesAsync { get; init; } = async (path, indexedFilePaths, knownSymbolIds, includeGenerated, cancellationToken) =>
	{
		var builder = new WorkspaceEdgeIndexBuilder();
		return await builder.BuildAsync(path, indexedFilePaths, knownSymbolIds, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyList<FileRecord>, IReadOnlyList<SymbolRecord>, bool, CancellationToken, Task<IReadOnlyList<ReferenceRecord>>> BuildReferencesAsync { get; init; } = async (path, files, symbols, includeGenerated, cancellationToken) =>
	{
		var builder = new WorkspaceReferenceIndexBuilder();
		return await builder.BuildAsync(path, files, symbols, includeGenerated, cancellationToken);
	};

	public Func<string, IReadOnlyList<FileRecord>, IReadOnlyList<SymbolRecord>, IReadOnlyCollection<string>, bool, CancellationToken, Task<IReadOnlyList<ReferenceRecord>>> BuildReferencesForFilesAsync { get; init; } = async (path, files, symbols, indexedFilePaths, includeGenerated, cancellationToken) =>
	{
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

	public Func<string, CancellationToken, Task<CodeIndexSnapshot>> ReadDatabaseSnapshotAsync { get; init; } = async (databasePath, cancellationToken) =>
	{
		var store = new SqliteCodeIndexStore();
		return await store.ReadAsync(databasePath, cancellationToken);
	};

	public Func<string, string, CancellationToken, Task<IReadOnlyList<ReferenceRecord>>> FindReferencesAsync { get; init; } = async (databasePath, targetSymbolId, cancellationToken) =>
	{
		var store = new SqliteCodeIndexStore();
		return await store.FindReferencesAsync(databasePath, targetSymbolId, cancellationToken);
	};
}

public static class CliApplication
{
	private static readonly JsonSerializerOptions OutputJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

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

		var pathArgument = new Argument<string>("path")
		{
			Description = "Path to a .sln or .csproj file to inspect."
		};

		var outputOption = new Option<string>("--out")
		{
			Description = "Directory where code-index artifacts will be written."
		};

		var dbOutOption = new Option<string>("--db-out")
		{
			Description = "SQLite database file where code-index data will be written."
		};

		var includeGeneratedOption = new Option<bool>("--include-generated")
		{
			Description = "Include generated C# files such as obj outputs and *.g.cs files."
		};

		var verboseOption = new Option<bool>("--verbose")
		{
			Description = "Write progress information while building the index."
		};

		var incrementalFromIndexOption = new Option<string>("--incremental-from-index")
		{
			Description = "Existing code-index artifact directory to use as an incremental build baseline."
		};

		var incrementalFromDbOption = new Option<string>("--incremental-from-db")
		{
			Description = "Existing SQLite code-index database to use as an incremental build baseline."
		};

		var indexOption = new Option<string>("--index")
		{
			Description = "Directory containing generated code-index artifacts."
		};

		var dbOption = new Option<string>("--db")
		{
			Description = "SQLite database file containing generated code-index data."
		};

		var limitOption = new Option<int>("--limit")
		{
			Description = "Maximum number of results to return."
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

		var buildCommand = new Command("build", "Build the initial file index artifact from a solution or project.");
		buildCommand.Add(pathArgument);
		buildCommand.Add(outputOption);
		buildCommand.Add(dbOutOption);
		buildCommand.Add(includeGeneratedOption);
		buildCommand.Add(verboseOption);
		buildCommand.Add(incrementalFromIndexOption);
		buildCommand.Add(incrementalFromDbOption);
		buildCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var path = parseResult.GetValue(pathArgument);
			var outputDirectory = parseResult.GetValue(outputOption);
			var databaseOutputPath = parseResult.GetValue(dbOutOption);
			var includeGenerated = parseResult.GetValue(includeGeneratedOption);
			var verbose = parseResult.GetValue(verboseOption);
			var incrementalFromIndex = parseResult.GetValue(incrementalFromIndexOption);
			var incrementalFromDb = parseResult.GetValue(incrementalFromDbOption);

			if (string.IsNullOrWhiteSpace(path))
			{
				throw new InvalidOperationException("A solution or project path is required.");
			}

			if (string.IsNullOrWhiteSpace(outputDirectory) && string.IsNullOrWhiteSpace(databaseOutputPath))
			{
				throw new InvalidOperationException("An output target is required. Pass --out <path> and/or --db-out <path>.");
			}

			EnsureIncrementalBaselineSource(incrementalFromIndex, incrementalFromDb);

			if (verbose)
			{
				Console.WriteLine($"Loading workspace from {Path.GetFullPath(path)}");
				Console.WriteLine(includeGenerated ? "Including generated files." : "Excluding generated files.");
			}

			CodeIndexSnapshot? incrementalBaseline = null;

			if (!string.IsNullOrWhiteSpace(incrementalFromIndex))
			{
				incrementalBaseline = await runtime.ReadSnapshotAsync(incrementalFromIndex, cancellationToken);

				if (verbose)
				{
					Console.WriteLine($"Loaded incremental baseline from {Path.GetFullPath(incrementalFromIndex)}");
				}
			}
			else if (!string.IsNullOrWhiteSpace(incrementalFromDb))
			{
				incrementalBaseline = await runtime.ReadDatabaseSnapshotAsync(incrementalFromDb, cancellationToken);

				if (verbose)
				{
					Console.WriteLine($"Loaded incremental baseline from {Path.GetFullPath(incrementalFromDb)}");
				}
			}

			var files = await runtime.BuildFilesAsync(path, includeGenerated, cancellationToken);

			if (verbose)
			{
				Console.WriteLine($"Indexed {files.Count} files.");
			}

			var inputKind = Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase) ? "solution" : "project";
			var incrementalMergeService = new IncrementalIndexMergeService();
			var embeddingBuilder = new SemanticEmbeddingIndexBuilder();
			var canReuseIncrementalBaseline = incrementalBaseline is not null &&
				incrementalMergeService.CanReuseBaseline(path, inputKind, files, incrementalBaseline);

			IReadOnlyList<SymbolRecord> symbols;
			IReadOnlyList<EdgeRecord> edges;
			IReadOnlyList<ReferenceRecord> references;
			IReadOnlyList<EmbeddingRecord> embeddings;

			if (canReuseIncrementalBaseline)
			{
				symbols = incrementalBaseline!.Symbols;
				edges = incrementalBaseline.Edges;
				references = incrementalBaseline.References;
				embeddings = incrementalBaseline.Embeddings;

				if (verbose)
				{
					Console.WriteLine("No file changes detected against the incremental baseline. Reusing symbols, edges, references, and embeddings.");
					Console.WriteLine($"Reused {symbols.Count} symbols.");
					Console.WriteLine($"Reused {edges.Count} edges.");
					Console.WriteLine($"Reused {references.Count} references.");
					Console.WriteLine($"Reused {embeddings.Count} embeddings.");
				}
			}
			else if (incrementalBaseline is not null)
			{
				var fileChanges = incrementalMergeService.AnalyzeFiles(files, incrementalBaseline);
				var rebuiltSymbols = fileChanges.ChangedCurrentFiles.Count == 0
					? Array.Empty<SymbolRecord>()
					: (await runtime.BuildSymbolsForFilesAsync(
						path,
						files,
						fileChanges.ChangedCurrentFiles.Select(file => file.Path).ToArray(),
						includeGenerated,
						cancellationToken)).ToArray();

				var mergePlan = incrementalMergeService.CreateMergePlan(files, incrementalBaseline, fileChanges, rebuiltSymbols);
				var rebuiltEdges = mergePlan.EdgeRebuildFilePaths.Count == 0
					? Array.Empty<EdgeRecord>()
					: (await runtime.BuildEdgesForFilesAsync(
						path,
						mergePlan.EdgeRebuildFilePaths,
						mergePlan.MergedSymbolIds,
						includeGenerated,
						cancellationToken)).ToArray();
				var rebuiltReferences = mergePlan.ReferenceRebuildFilePaths.Count == 0
					? Array.Empty<ReferenceRecord>()
					: (await runtime.BuildReferencesForFilesAsync(
						path,
						files,
						mergePlan.MergedSymbols,
						mergePlan.ReferenceRebuildFilePaths,
						includeGenerated,
						cancellationToken)).ToArray();

				symbols = mergePlan.MergedSymbols;
				edges = incrementalMergeService.MergeEdges(incrementalBaseline, mergePlan, rebuiltEdges);
				references = incrementalMergeService.MergeReferences(incrementalBaseline, mergePlan, rebuiltReferences);
				embeddings = embeddingBuilder.Build(files, symbols);

				if (verbose)
				{
					Console.WriteLine($"Detected {fileChanges.ChangedCurrentFiles.Count} changed files and {fileChanges.RemovedFiles.Count} removed files against the incremental baseline.");
					Console.WriteLine($"Rebuilt {rebuiltSymbols.Length} symbols across {fileChanges.ChangedCurrentFiles.Count} changed files.");
					Console.WriteLine($"Rebuilt {rebuiltEdges.Length} edges across {mergePlan.EdgeRebuildFilePaths.Count} impacted files.");
					Console.WriteLine($"Rebuilt {rebuiltReferences.Length} references across {mergePlan.ReferenceRebuildFilePaths.Count} impacted files.");
					Console.WriteLine($"Rebuilt {embeddings.Count} embeddings from merged files and symbols.");
				}
			}
			else
			{
				if (verbose && incrementalBaseline is not null)
				{
					Console.WriteLine("Detected file changes against the incremental baseline. Rebuilding symbols, edges, and references.");
				}

				symbols = await runtime.BuildSymbolsAsync(path, files, includeGenerated, cancellationToken);

				if (verbose)
				{
					Console.WriteLine($"Indexed {symbols.Count} symbols.");
				}

				edges = await runtime.BuildEdgesAsync(path, includeGenerated, cancellationToken);

				if (verbose)
				{
					Console.WriteLine($"Indexed {edges.Count} edges.");
				}

				references = await runtime.BuildReferencesAsync(path, files, symbols, includeGenerated, cancellationToken);

				if (verbose)
				{
					Console.WriteLine($"Indexed {references.Count} references.");
				}

				embeddings = embeddingBuilder.Build(files, symbols);

				if (verbose)
				{
					Console.WriteLine($"Indexed {embeddings.Count} embeddings.");
				}
			}

			var meta = CodeIndexMetaFactory.Create(path, inputKind);
			var snapshot = new CodeIndexSnapshot(meta, files, symbols, edges, references, embeddings);
			runtime.ValidateSnapshot(snapshot);
			var sqliteStore = new SqliteCodeIndexStore();

			if (verbose)
			{
				Console.WriteLine("Validation passed.");
			}

			if (!string.IsNullOrWhiteSpace(outputDirectory))
			{
				var fullOutputDirectory = Path.GetFullPath(outputDirectory);
				var metaOutputPath = Path.Combine(fullOutputDirectory, "code-index.meta.json");
				var filesOutputPath = Path.Combine(fullOutputDirectory, "code-index.files.json");
				var symbolsOutputPath = Path.Combine(fullOutputDirectory, "code-index.symbols.json");
				var edgesOutputPath = Path.Combine(fullOutputDirectory, "code-index.edges.json");
				var referencesOutputPath = Path.Combine(fullOutputDirectory, "code-index.references.json");
				var embeddingsOutputPath = Path.Combine(fullOutputDirectory, "code-index.embeddings.json");

				await CodeIndexJson.WriteToFileAsync(metaOutputPath, meta, cancellationToken);
				await CodeIndexJson.WriteToFileAsync(filesOutputPath, files, cancellationToken);
				await CodeIndexJson.WriteToFileAsync(symbolsOutputPath, symbols, cancellationToken);
				await CodeIndexJson.WriteToFileAsync(edgesOutputPath, edges, cancellationToken);
				await CodeIndexJson.WriteToFileAsync(referencesOutputPath, references, cancellationToken);
				await CodeIndexJson.WriteToFileAsync(embeddingsOutputPath, embeddings, cancellationToken);

				Console.WriteLine($"Wrote metadata to {metaOutputPath}");
				Console.WriteLine($"Wrote {files.Count} file records to {filesOutputPath}");
				Console.WriteLine($"Wrote {symbols.Count} symbol records to {symbolsOutputPath}");
				Console.WriteLine($"Wrote {edges.Count} edge records to {edgesOutputPath}");
				Console.WriteLine($"Wrote {references.Count} reference records to {referencesOutputPath}");
				Console.WriteLine($"Wrote {embeddings.Count} embedding records to {embeddingsOutputPath}");
			}

			if (!string.IsNullOrWhiteSpace(databaseOutputPath))
			{
				var fullDatabasePath = Path.GetFullPath(databaseOutputPath);
				await sqliteStore.WriteAsync(fullDatabasePath, snapshot, cancellationToken);
				Console.WriteLine($"Wrote SQLite index to {fullDatabasePath}");
			}

			return 0;
		});

		var findSymbolCommand = new Command("find-symbol", "Find symbols by name or qualified name from a generated index.");
		findSymbolCommand.Add(symbolQueryArgument);
		findSymbolCommand.Add(indexOption);
		findSymbolCommand.Add(dbOption);
		findSymbolCommand.Add(limitOption);
		findSymbolCommand.Add(kindOption);
		findSymbolCommand.Add(accessibilityOption);
		findSymbolCommand.Add(sortOption);
		findSymbolCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			EnsureSingleStoreSource(indexDirectory, databasePath);

			var sqliteStore = new SqliteCodeIndexStore();
			var matches = !string.IsNullOrWhiteSpace(databasePath)
				? LimitResults(OrderFindSymbolResults(await sqliteStore.FindSymbolsAsync(databasePath, query, kind, accessibility, cancellationToken), query, sort), limit).ToArray()
				: LimitResults(OrderFindSymbolResults((await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken)).Symbols
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

		var semanticSearchCommand = new Command("semantic-search", "Search embedded file and symbol text using deterministic vector similarity.");
		semanticSearchCommand.Add(symbolQueryArgument);
		semanticSearchCommand.Add(indexOption);
		semanticSearchCommand.Add(dbOption);
		semanticSearchCommand.Add(limitOption);
		semanticSearchCommand.Add(typeOption);
		semanticSearchCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);
			var itemType = NormalizeOptional(parseResult.GetValue(typeOption));

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A semantic search query is required.");
			}

			if (itemType is not null &&
				!string.Equals(itemType, EmbeddingItemTypes.File, StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(itemType, EmbeddingItemTypes.Symbol, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Unsupported --type for semantic-search. Use file or symbol.");
			}

			var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
			var embeddingBuilder = new SemanticEmbeddingIndexBuilder();
			var results = embeddingBuilder.Search(query, snapshot.Embeddings, itemType, limit <= 0 ? 10 : limit)
				.Select(result => CreateSemanticSearchResult(snapshot, result))
				.ToArray();

			WriteJson(results);
			return 0;
		});

		var findReferencesCommand = new Command("find-references", "Find references for a symbol ID or qualified name from a generated index.");
		findReferencesCommand.Add(symbolQueryArgument);
		findReferencesCommand.Add(indexOption);
		findReferencesCommand.Add(dbOption);
		findReferencesCommand.Add(limitOption);
		findReferencesCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
			var targetSymbol = snapshot.Symbols.FirstOrDefault(candidate =>
				string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
				string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

			if (targetSymbol is null)
			{
				throw new InvalidOperationException($"No symbol found for query: {query}");
			}

			var filesById = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
			var symbolsById = snapshot.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
			var referencesForSymbol = snapshot.References
				.Where(reference => string.Equals(reference.TargetSymbolId, targetSymbol.Id, StringComparison.Ordinal))
				.OrderBy(reference => filesById.TryGetValue(reference.FileId, out var file) ? file.Path : reference.FileId, StringComparer.Ordinal)
				.ThenBy(reference => reference.Range.StartLine)
				.ThenBy(reference => reference.Range.StartColumn);

			var results = LimitResults(referencesForSymbol.Select(reference => new
			{
				TargetSymbolId = reference.TargetSymbolId,
				TargetQualifiedName = targetSymbol.QualifiedName,
				SourceSymbolId = reference.SourceSymbolId,
				SourceQualifiedName = reference.SourceSymbolId is not null && symbolsById.TryGetValue(reference.SourceSymbolId, out var sourceSymbol)
					? sourceSymbol.QualifiedName
					: null,
				File = filesById.TryGetValue(reference.FileId, out var file) ? file.Path : reference.FileId,
				Range = reference.Range,
				reference.LineText
			}), limit).ToArray();

			WriteJson(results);
			return 0;
		});

		var getSymbolCommand = new Command("get-symbol", "Get a single symbol by ID or qualified name from a generated index.");
		getSymbolCommand.Add(symbolQueryArgument);
		getSymbolCommand.Add(indexOption);
		getSymbolCommand.Add(dbOption);
		getSymbolCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var sqliteStore = new SqliteCodeIndexStore();
			var symbol = !string.IsNullOrWhiteSpace(databasePath)
				? await sqliteStore.GetSymbolAsync(databasePath, query, cancellationToken)
				: (await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken)).Symbols.FirstOrDefault(candidate =>
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
		getChildrenCommand.Add(dbOption);
		getChildrenCommand.Add(limitOption);
		getChildrenCommand.Add(kindOption);
		getChildrenCommand.Add(accessibilityOption);
		getChildrenCommand.Add(sortOption);
		getChildrenCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var sqliteStore = new SqliteCodeIndexStore();
			SymbolRecord[] children;
			if (!string.IsNullOrWhiteSpace(databasePath))
			{
				var parent = await sqliteStore.GetSymbolAsync(databasePath, query, cancellationToken);

				if (parent is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				children = LimitResults(OrderChildResults(await sqliteStore.GetChildrenAsync(databasePath, parent.Id, kind, accessibility, cancellationToken), sort), limit).ToArray();
			}
			else
			{
				var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
				var parent = snapshot.Symbols.FirstOrDefault(candidate =>
					string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
					string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

				if (parent is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				children = LimitResults(OrderChildResults(snapshot.Edges
					.Where(edge => edge.Type == EdgeTypes.Contains && edge.From == parent.Id)
					.Join(snapshot.Symbols, edge => edge.To, symbol => symbol.Id, (_, symbol) => symbol)
					.Where(symbol => string.IsNullOrWhiteSpace(kind) || string.Equals(symbol.Kind, kind, StringComparison.OrdinalIgnoreCase))
					.Where(symbol => string.IsNullOrWhiteSpace(accessibility) || string.Equals(symbol.Accessibility, accessibility, StringComparison.OrdinalIgnoreCase)), sort), limit)
					.ToArray();
			}

			WriteJson(children);
			return 0;
		});

		var getCalleesCommand = new Command("get-callees", "Get call targets for a method or constructor from a generated index.");
		getCalleesCommand.Add(symbolQueryArgument);
		getCalleesCommand.Add(indexOption);
		getCalleesCommand.Add(dbOption);
		getCalleesCommand.Add(limitOption);
		getCalleesCommand.Add(kindOption);
		getCalleesCommand.Add(accessibilityOption);
		getCalleesCommand.Add(sortOption);
		getCalleesCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var sqliteStore = new SqliteCodeIndexStore();
			SymbolRecord[] callees;

			if (!string.IsNullOrWhiteSpace(databasePath))
			{
				var symbol = await sqliteStore.GetSymbolAsync(databasePath, query, cancellationToken);

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				callees = LimitResults(OrderChildResults(await sqliteStore.GetCalleesAsync(databasePath, symbol.Id, kind, accessibility, cancellationToken), sort), limit).ToArray();
			}
			else
			{
				var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
				var symbol = snapshot.Symbols.FirstOrDefault(candidate =>
					string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
					string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				callees = LimitResults(OrderChildResults(snapshot.Edges
					.Where(edge => edge.Type == EdgeTypes.Calls && edge.From == symbol.Id)
					.Join(snapshot.Symbols, edge => edge.To, candidate => candidate.Id, (_, candidate) => candidate)
					.Where(candidate => string.IsNullOrWhiteSpace(kind) || string.Equals(candidate.Kind, kind, StringComparison.OrdinalIgnoreCase))
					.Where(candidate => string.IsNullOrWhiteSpace(accessibility) || string.Equals(candidate.Accessibility, accessibility, StringComparison.OrdinalIgnoreCase)), sort), limit)
					.ToArray();
			}

			WriteJson(callees);
			return 0;
		});

		var getCallersCommand = new Command("get-callers", "Get callers for a method or constructor from a generated index.");
		getCallersCommand.Add(symbolQueryArgument);
		getCallersCommand.Add(indexOption);
		getCallersCommand.Add(dbOption);
		getCallersCommand.Add(limitOption);
		getCallersCommand.Add(kindOption);
		getCallersCommand.Add(accessibilityOption);
		getCallersCommand.Add(sortOption);
		getCallersCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var sqliteStore = new SqliteCodeIndexStore();
			SymbolRecord[] callers;

			if (!string.IsNullOrWhiteSpace(databasePath))
			{
				var symbol = await sqliteStore.GetSymbolAsync(databasePath, query, cancellationToken);

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				callers = LimitResults(OrderChildResults(await sqliteStore.GetCallersAsync(databasePath, symbol.Id, kind, accessibility, cancellationToken), sort), limit).ToArray();
			}
			else
			{
				var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
				var symbol = snapshot.Symbols.FirstOrDefault(candidate =>
					string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
					string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				callers = LimitResults(OrderChildResults(snapshot.Edges
					.Where(edge => edge.Type == EdgeTypes.Calls && edge.To == symbol.Id)
					.Join(snapshot.Symbols, edge => edge.From, candidate => candidate.Id, (_, candidate) => candidate)
					.Where(candidate => string.IsNullOrWhiteSpace(kind) || string.Equals(candidate.Kind, kind, StringComparison.OrdinalIgnoreCase))
					.Where(candidate => string.IsNullOrWhiteSpace(accessibility) || string.Equals(candidate.Accessibility, accessibility, StringComparison.OrdinalIgnoreCase)), sort), limit)
					.ToArray();
			}

			WriteJson(callers);
			return 0;
		});

		var getTestTargetsCommand = new Command("get-test-targets", "Get heuristic production targets for a test symbol from a generated index.");
		getTestTargetsCommand.Add(symbolQueryArgument);
		getTestTargetsCommand.Add(indexOption);
		getTestTargetsCommand.Add(dbOption);
		getTestTargetsCommand.Add(limitOption);
		getTestTargetsCommand.Add(kindOption);
		getTestTargetsCommand.Add(accessibilityOption);
		getTestTargetsCommand.Add(sortOption);
		getTestTargetsCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var sqliteStore = new SqliteCodeIndexStore();
			SymbolRecord[] targets;

			if (!string.IsNullOrWhiteSpace(databasePath))
			{
				var symbol = await sqliteStore.GetSymbolAsync(databasePath, query, cancellationToken);

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				targets = LimitResults(OrderChildResults(await sqliteStore.GetTestTargetsAsync(databasePath, symbol.Id, kind, accessibility, cancellationToken), sort), limit).ToArray();
			}
			else
			{
				var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
				var symbol = snapshot.Symbols.FirstOrDefault(candidate =>
					string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
					string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				targets = LimitResults(OrderChildResults(snapshot.Edges
					.Where(edge => edge.Type == EdgeTypes.Tests && edge.From == symbol.Id)
					.Join(snapshot.Symbols, edge => edge.To, candidate => candidate.Id, (_, candidate) => candidate)
					.Where(candidate => string.IsNullOrWhiteSpace(kind) || string.Equals(candidate.Kind, kind, StringComparison.OrdinalIgnoreCase))
					.Where(candidate => string.IsNullOrWhiteSpace(accessibility) || string.Equals(candidate.Accessibility, accessibility, StringComparison.OrdinalIgnoreCase)), sort), limit)
					.ToArray();
			}

			WriteJson(targets);
			return 0;
		});

		var getTestsCommand = new Command("get-tests", "Get heuristic tests for a production symbol from a generated index.");
		getTestsCommand.Add(symbolQueryArgument);
		getTestsCommand.Add(indexOption);
		getTestsCommand.Add(dbOption);
		getTestsCommand.Add(limitOption);
		getTestsCommand.Add(kindOption);
		getTestsCommand.Add(accessibilityOption);
		getTestsCommand.Add(sortOption);
		getTestsCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var query = parseResult.GetValue(symbolQueryArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
			var limit = parseResult.GetValue(limitOption);
			var kind = parseResult.GetValue(kindOption);
			var accessibility = parseResult.GetValue(accessibilityOption);
			var sort = parseResult.GetValue(sortOption);

			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException("A symbol query is required.");
			}

			var sqliteStore = new SqliteCodeIndexStore();
			SymbolRecord[] tests;

			if (!string.IsNullOrWhiteSpace(databasePath))
			{
				var symbol = await sqliteStore.GetSymbolAsync(databasePath, query, cancellationToken);

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				tests = LimitResults(OrderChildResults(await sqliteStore.GetTestsAsync(databasePath, symbol.Id, kind, accessibility, cancellationToken), sort), limit).ToArray();
			}
			else
			{
				var snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
				var symbol = snapshot.Symbols.FirstOrDefault(candidate =>
					string.Equals(candidate.Id, query, StringComparison.Ordinal) ||
					string.Equals(candidate.QualifiedName, query, StringComparison.OrdinalIgnoreCase));

				if (symbol is null)
				{
					throw new InvalidOperationException($"No symbol found for query: {query}");
				}

				tests = LimitResults(OrderChildResults(snapshot.Edges
					.Where(edge => edge.Type == EdgeTypes.Tests && edge.To == symbol.Id)
					.Join(snapshot.Symbols, edge => edge.From, candidate => candidate.Id, (_, candidate) => candidate)
					.Where(candidate => string.IsNullOrWhiteSpace(kind) || string.Equals(candidate.Kind, kind, StringComparison.OrdinalIgnoreCase))
					.Where(candidate => string.IsNullOrWhiteSpace(accessibility) || string.Equals(candidate.Accessibility, accessibility, StringComparison.OrdinalIgnoreCase)), sort), limit)
					.ToArray();
			}

			WriteJson(tests);
			return 0;
		});

		var getExcerptCommand = new Command("get-excerpt", "Get exact file lines from a generated index.");
		getExcerptCommand.Add(filePathArgument);
		getExcerptCommand.Add(indexOption);
		getExcerptCommand.Add(dbOption);
		getExcerptCommand.Add(startOption);
		getExcerptCommand.Add(endOption);
		getExcerptCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var file = parseResult.GetValue(filePathArgument);
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
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

			var sqliteStore = new SqliteCodeIndexStore();
			var fileRecord = !string.IsNullOrWhiteSpace(databasePath)
				? await sqliteStore.GetFileByPathAsync(databasePath, file, cancellationToken)
				: (await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken)).Files.FirstOrDefault(candidate => string.Equals(candidate.Path, file, StringComparison.OrdinalIgnoreCase));

			if (fileRecord is null)
			{
				throw new InvalidOperationException($"No indexed file found for path: {file}");
			}

			var meta = !string.IsNullOrWhiteSpace(databasePath)
				? await sqliteStore.ReadMetaAsync(databasePath, cancellationToken)
				: (await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken)).Meta;
			var fullFilePath = Path.Combine(meta.SourceRoot, fileRecord.Path.Replace('/', Path.DirectorySeparatorChar));
			var lines = await File.ReadAllLinesAsync(fullFilePath, cancellationToken);
			var excerpt = Enumerable.Range(start, Math.Min(end, lines.Length) - start + 1)
				.Select(lineNumber => new { line = lineNumber, text = lines[lineNumber - 1] })
				.ToArray();

			WriteJson(excerpt);
			return 0;
		});

		var benchmarkCommand = new Command("benchmark", "Compare reading the indexed project directly versus using code-index artifacts first.");
		benchmarkCommand.Add(indexOption);
		benchmarkCommand.Add(dbOption);
		benchmarkCommand.Add(benchmarkSymbolOption);
		benchmarkCommand.Add(benchmarkFileOption);
		benchmarkCommand.Add(startOption);
		benchmarkCommand.Add(endOption);
		benchmarkCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			var indexDirectory = parseResult.GetValue(indexOption);
			var databasePath = parseResult.GetValue(dbOption);
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

			EnsureSingleStoreSource(indexDirectory, databasePath);
			var sqliteStore = new SqliteCodeIndexStore();
			CodeIndexSnapshot? snapshot = null;
			var fullIndexDirectory = string.IsNullOrWhiteSpace(indexDirectory) ? null : Path.GetFullPath(indexDirectory);
			var fullDatabasePath = string.IsNullOrWhiteSpace(databasePath) ? null : Path.GetFullPath(databasePath);
			CodeIndexMeta meta;
			IReadOnlyList<FileRecord> files;
			int symbolCount;
			int edgeCount;
			var indexLoadStopwatch = Stopwatch.StartNew();

			if (!string.IsNullOrWhiteSpace(fullDatabasePath))
			{
				meta = await sqliteStore.ReadMetaAsync(fullDatabasePath, cancellationToken);
				files = await sqliteStore.ReadFilesAsync(fullDatabasePath, cancellationToken);
				symbolCount = await sqliteStore.CountSymbolsAsync(fullDatabasePath, cancellationToken);
				edgeCount = await sqliteStore.CountEdgesAsync(fullDatabasePath, cancellationToken);
			}
			else
			{
				snapshot = await ReadSnapshotAsync(runtime, indexDirectory, databasePath, cancellationToken);
				meta = snapshot.Meta;
				files = snapshot.Files;
				symbolCount = snapshot.Symbols.Count;
				edgeCount = snapshot.Edges.Count;
			}

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
			long? databaseBytes = null;

			if (!string.IsNullOrWhiteSpace(fullIndexDirectory))
			{
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
			}
			else if (!string.IsNullOrWhiteSpace(fullDatabasePath))
			{
				databaseBytes = new FileInfo(fullDatabasePath).Length;
				totalIndexBytes = databaseBytes.Value;
			}

			object? symbolQueryMetrics = null;
			long indexFirstFlowBytes = 0;
			long indexFirstFlowElapsedMs = 0;

			if (!string.IsNullOrWhiteSpace(symbolQuery))
			{
				var symbolQueryStopwatch = Stopwatch.StartNew();
				var matches = !string.IsNullOrWhiteSpace(fullDatabasePath)
					? LimitResults(OrderFindSymbolResults(await sqliteStore.FindSymbolsAsync(fullDatabasePath, symbolQuery, null, null, cancellationToken), symbolQuery, "ranked"), 5).ToArray()
					: LimitResults(OrderFindSymbolResults(snapshot!.Symbols
						.Where(symbol =>
							string.Equals(symbol.Name, symbolQuery, StringComparison.OrdinalIgnoreCase) ||
							string.Equals(symbol.QualifiedName, symbolQuery, StringComparison.OrdinalIgnoreCase) ||
							symbol.QualifiedName.Contains(symbolQuery, StringComparison.OrdinalIgnoreCase)), symbolQuery, "ranked"), 5)
						.ToArray();

				var selected = matches.FirstOrDefault();
				var children = selected is null
					? Array.Empty<SymbolRecord>()
					: !string.IsNullOrWhiteSpace(fullDatabasePath)
						? OrderChildResults(await sqliteStore.GetChildrenAsync(fullDatabasePath, selected.Id, null, null, cancellationToken), "declaration").ToArray()
						: OrderChildResults(snapshot!.Edges
							.Where(edge => edge.Type == EdgeTypes.Contains && edge.From == selected.Id)
							.Join(snapshot.Symbols, edge => edge.To, symbol => symbol.Id, (_, symbol) => symbol), "declaration")
							.ToArray();

				var findSymbolBytes = GetSerializedByteCount(matches);
				var getSymbolBytes = selected is null ? 0 : GetSerializedByteCount(selected);
				var getChildrenBytes = children.Length == 0 ? 0 : GetSerializedByteCount(children);
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
				var fileRecord = !string.IsNullOrWhiteSpace(fullDatabasePath)
					? await sqliteStore.GetFileByPathAsync(fullDatabasePath, file, cancellationToken)
					: files.FirstOrDefault(candidate => string.Equals(candidate.Path, file, StringComparison.OrdinalIgnoreCase));

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
			var totalIndexEstimatedTokens = !string.IsNullOrWhiteSpace(fullIndexDirectory)
				? EstimateTokenCount(totalIndexBytes)
				: (int?)null;
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
					loadElapsedMs = fullIndexDirectory is null ? (long?)null : indexLoadStopwatch.ElapsedMilliseconds,
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
				database = databaseBytes is null ? null : new
				{
					path = fullDatabasePath,
					loadElapsedMs = indexLoadStopwatch.ElapsedMilliseconds,
					totalBytes = databaseBytes,
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
		rootCommand.Add(getExcerptCommand);

		return rootCommand;
	}

	private static async Task<CodeIndexSnapshot> ReadSnapshotAsync(CliRuntime runtime, string? indexDirectory, CancellationToken cancellationToken)
	{
		return await ReadSnapshotAsync(runtime, indexDirectory, null, cancellationToken);
	}

	private static async Task<CodeIndexSnapshot> ReadSnapshotAsync(CliRuntime runtime, string? indexDirectory, string? databasePath, CancellationToken cancellationToken)
	{
		EnsureSingleStoreSource(indexDirectory, databasePath);

		if (!string.IsNullOrWhiteSpace(databasePath))
		{
			var sqliteStore = new SqliteCodeIndexStore();
			return await sqliteStore.ReadAsync(databasePath, cancellationToken);
		}

		if (string.IsNullOrWhiteSpace(indexDirectory))
		{
			throw new InvalidOperationException("An index directory is required. Pass --index <path>.");
		}

		return await runtime.ReadSnapshotAsync(indexDirectory, cancellationToken);
	}

	private static void EnsureSingleStoreSource(string? indexDirectory, string? databasePath)
	{
		var hasIndex = !string.IsNullOrWhiteSpace(indexDirectory);
		var hasDatabase = !string.IsNullOrWhiteSpace(databasePath);

		if (!hasIndex && !hasDatabase)
		{
			throw new InvalidOperationException("An index directory or database is required. Pass --index <path> or --db <path>.");
		}

		if (hasIndex && hasDatabase)
		{
			throw new InvalidOperationException("Pass either --index <path> or --db <path>, but not both.");
		}
	}

	private static void WriteJson<T>(T value)
	{
		Console.WriteLine(JsonSerializer.Serialize(value, OutputJsonOptions));
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

	private static void EnsureIncrementalBaselineSource(string? incrementalFromIndex, string? incrementalFromDb)
	{
		if (string.IsNullOrWhiteSpace(incrementalFromIndex) || string.IsNullOrWhiteSpace(incrementalFromDb))
		{
			return;
		}

		throw new InvalidOperationException("Pass either --incremental-from-index <path> or --incremental-from-db <path>, but not both.");
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