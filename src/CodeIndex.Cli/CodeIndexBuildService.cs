using CodeIndex.Core;

namespace CodeIndex.Cli;

public sealed class CodeIndexBuildService(CliRuntime runtime)
{
    public async Task<CodeIndexBuildResult> BuildAsync(CodeIndexBuildRequest request, CancellationToken cancellationToken)
    {
        CodeIndexSnapshot? incrementalBaseline = null;

        if (!string.IsNullOrWhiteSpace(request.IncrementalFromIndex))
        {
            incrementalBaseline = await runtime.ReadSnapshotAsync(request.IncrementalFromIndex, cancellationToken);
        }

        var files = await runtime.BuildFilesAsync(request.InputPath, request.IncludeGenerated, cancellationToken);
        var inputKind = SourceInputKindDetector.Detect(request.InputPath);
        var incrementalMergeService = new IncrementalIndexMergeService();
        var embeddingBuilder = new SemanticEmbeddingIndexBuilder();
        var canReuseIncrementalBaseline = incrementalBaseline is not null &&
            incrementalMergeService.CanReuseBaseline(request.InputPath, inputKind, files, incrementalBaseline);

        IReadOnlyList<SymbolRecord> symbols;
        IReadOnlyList<EdgeRecord> edges;
        IReadOnlyList<ReferenceRecord> references;
        IReadOnlyList<EmbeddingRecord> embeddings;
        CodeIndexBuildStats stats;

        if (canReuseIncrementalBaseline)
        {
            symbols = incrementalBaseline!.Symbols;
            edges = incrementalBaseline.Edges;
            references = incrementalBaseline.References;
            embeddings = incrementalBaseline.Embeddings;
            stats = new CodeIndexBuildStats(
                UsedIncrementalBaseline: true,
                ReusedIncrementalBaseline: true,
                FileCount: files.Count,
                SymbolCount: symbols.Count,
                EdgeCount: edges.Count,
                ReferenceCount: references.Count,
                EmbeddingCount: embeddings.Count,
                ChangedFileCount: 0,
                RemovedFileCount: 0,
                RebuiltSymbolCount: 0,
                RebuiltEdgeCount: 0,
                RebuiltReferenceCount: 0);
        }
        else if (incrementalBaseline is not null)
        {
            var fileChanges = incrementalMergeService.AnalyzeFiles(files, incrementalBaseline);
            var rebuiltSymbols = fileChanges.ChangedCurrentFiles.Count == 0
                ? Array.Empty<SymbolRecord>()
                : (await runtime.BuildSymbolsForFilesAsync(
                    request.InputPath,
                    files,
                    fileChanges.ChangedCurrentFiles.Select(file => file.Path).ToArray(),
                    request.IncludeGenerated,
                    cancellationToken)).ToArray();

            var mergePlan = incrementalMergeService.CreateMergePlan(files, incrementalBaseline, fileChanges, rebuiltSymbols);
            var rebuiltEdges = mergePlan.EdgeRebuildFilePaths.Count == 0
                ? Array.Empty<EdgeRecord>()
                : (await runtime.BuildEdgesForFilesAsync(
                    request.InputPath,
                    mergePlan.EdgeRebuildFilePaths,
                    mergePlan.MergedSymbolIds,
                    request.IncludeGenerated,
                    cancellationToken)).ToArray();
            var rebuiltReferences = mergePlan.ReferenceRebuildFilePaths.Count == 0
                ? Array.Empty<ReferenceRecord>()
                : (await runtime.BuildReferencesForFilesAsync(
                    request.InputPath,
                    files,
                    mergePlan.MergedSymbols,
                    mergePlan.ReferenceRebuildFilePaths,
                    request.IncludeGenerated,
                    cancellationToken)).ToArray();

            symbols = mergePlan.MergedSymbols;
            edges = incrementalMergeService.MergeEdges(incrementalBaseline, mergePlan, rebuiltEdges);
            references = incrementalMergeService.MergeReferences(incrementalBaseline, mergePlan, rebuiltReferences);
            embeddings = embeddingBuilder.Build(files, symbols);
            stats = new CodeIndexBuildStats(
                UsedIncrementalBaseline: true,
                ReusedIncrementalBaseline: false,
                FileCount: files.Count,
                SymbolCount: symbols.Count,
                EdgeCount: edges.Count,
                ReferenceCount: references.Count,
                EmbeddingCount: embeddings.Count,
                ChangedFileCount: fileChanges.ChangedCurrentFiles.Count,
                RemovedFileCount: fileChanges.RemovedFiles.Count,
                RebuiltSymbolCount: rebuiltSymbols.Length,
                RebuiltEdgeCount: rebuiltEdges.Length,
                RebuiltReferenceCount: rebuiltReferences.Length,
                RebuiltEdgeFileCount: mergePlan.EdgeRebuildFilePaths.Count,
                RebuiltReferenceFileCount: mergePlan.ReferenceRebuildFilePaths.Count);
        }
        else
        {
            symbols = await runtime.BuildSymbolsAsync(request.InputPath, files, request.IncludeGenerated, cancellationToken);
            edges = await runtime.BuildEdgesAsync(request.InputPath, request.IncludeGenerated, cancellationToken);
            references = await runtime.BuildReferencesAsync(request.InputPath, files, symbols, request.IncludeGenerated, cancellationToken);
            embeddings = embeddingBuilder.Build(files, symbols);
            stats = new CodeIndexBuildStats(
                UsedIncrementalBaseline: false,
                ReusedIncrementalBaseline: false,
                FileCount: files.Count,
                SymbolCount: symbols.Count,
                EdgeCount: edges.Count,
                ReferenceCount: references.Count,
                EmbeddingCount: embeddings.Count,
                ChangedFileCount: 0,
                RemovedFileCount: 0,
                RebuiltSymbolCount: 0,
                RebuiltEdgeCount: 0,
                RebuiltReferenceCount: 0,
                RebuiltEdgeFileCount: 0,
                RebuiltReferenceFileCount: 0);
        }

        var meta = CodeIndexMetaFactory.Create(request.InputPath, inputKind);
        var snapshot = new CodeIndexSnapshot(meta, files, symbols, edges, references, embeddings);
        runtime.ValidateSnapshot(snapshot);
        return new CodeIndexBuildResult(snapshot, stats);
    }

    public async Task<CodeIndexSnapshotOutputPaths> WriteSnapshotAsync(CodeIndexSnapshot snapshot, string outputDirectory, CancellationToken cancellationToken)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var metaOutputPath = Path.Combine(fullOutputDirectory, "code-index.meta.json");
        var filesOutputPath = Path.Combine(fullOutputDirectory, "code-index.files.json");
        var symbolsOutputPath = Path.Combine(fullOutputDirectory, "code-index.symbols.json");
        var edgesOutputPath = Path.Combine(fullOutputDirectory, "code-index.edges.json");
        var referencesOutputPath = Path.Combine(fullOutputDirectory, "code-index.references.json");
        var embeddingsOutputPath = Path.Combine(fullOutputDirectory, "code-index.embeddings.json");

        await CodeIndexJson.WriteToFileAsync(metaOutputPath, snapshot.Meta, cancellationToken);
        await CodeIndexJson.WriteToFileAsync(filesOutputPath, snapshot.Files, cancellationToken);
        await CodeIndexJson.WriteToFileAsync(symbolsOutputPath, snapshot.Symbols, cancellationToken);
        await CodeIndexJson.WriteToFileAsync(edgesOutputPath, snapshot.Edges, cancellationToken);
        await CodeIndexJson.WriteToFileAsync(referencesOutputPath, snapshot.References, cancellationToken);
        await CodeIndexJson.WriteToFileAsync(embeddingsOutputPath, snapshot.Embeddings, cancellationToken);

        return new CodeIndexSnapshotOutputPaths(
            metaOutputPath,
            filesOutputPath,
            symbolsOutputPath,
            edgesOutputPath,
            referencesOutputPath,
            embeddingsOutputPath);
    }
}

public sealed record CodeIndexBuildRequest(string InputPath, bool IncludeGenerated, string? IncrementalFromIndex);

public sealed record CodeIndexBuildResult(CodeIndexSnapshot Snapshot, CodeIndexBuildStats Stats);

public sealed record CodeIndexBuildStats(
    bool UsedIncrementalBaseline,
    bool ReusedIncrementalBaseline,
    int FileCount,
    int SymbolCount,
    int EdgeCount,
    int ReferenceCount,
    int EmbeddingCount,
    int ChangedFileCount,
    int RemovedFileCount,
    int RebuiltSymbolCount,
    int RebuiltEdgeCount,
    int RebuiltReferenceCount,
    int RebuiltEdgeFileCount = 0,
    int RebuiltReferenceFileCount = 0);

public sealed record CodeIndexSnapshotOutputPaths(
    string MetaPath,
    string FilesPath,
    string SymbolsPath,
    string EdgesPath,
    string ReferencesPath,
    string EmbeddingsPath);