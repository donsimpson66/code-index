namespace CodeIndex.Core;

public sealed record IncrementalFileChanges(
    IReadOnlyList<FileRecord> ChangedCurrentFiles,
    IReadOnlyList<FileRecord> RemovedFiles,
    IReadOnlySet<string> AffectedFileIds);

public sealed record IncrementalMergePlan(
    IReadOnlyList<SymbolRecord> MergedSymbols,
    IReadOnlySet<string> MergedSymbolIds,
    IReadOnlyList<string> EdgeRebuildFilePaths,
    IReadOnlySet<string> ImpactedEdgeSourceSymbolIds,
    IReadOnlySet<string> RemovedOrRebuiltSymbolIds,
    IReadOnlyList<string> ReferenceRebuildFilePaths,
    IReadOnlySet<string> ReferenceAffectedFileIds);

public sealed class IncrementalIndexMergeService
{
    public bool CanReuseBaseline(string inputPath, string inputKind, IReadOnlyList<FileRecord> currentFiles, CodeIndexSnapshot baseline)
    {
        if (!string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(baseline.Meta.InputPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(inputKind, baseline.Meta.InputKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (currentFiles.Count != baseline.Files.Count)
        {
            return false;
        }

        var baselineFilesByPath = baseline.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);

        foreach (var currentFile in currentFiles)
        {
            if (!baselineFilesByPath.TryGetValue(currentFile.Path, out var baselineFile))
            {
                return false;
            }

            if (!string.Equals(currentFile.Hash, baselineFile.Hash, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public IncrementalFileChanges AnalyzeFiles(IReadOnlyList<FileRecord> currentFiles, CodeIndexSnapshot baseline)
    {
        var currentFilesByPath = currentFiles.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var baselineFilesByPath = baseline.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);

        var changedCurrentFiles = currentFiles
            .Where(file => !baselineFilesByPath.TryGetValue(file.Path, out var baselineFile) || !string.Equals(file.Hash, baselineFile.Hash, StringComparison.Ordinal))
            .ToArray();

        var removedFiles = baseline.Files
            .Where(file => !currentFilesByPath.ContainsKey(file.Path))
            .ToArray();

        var affectedFileIds = new HashSet<string>(
            changedCurrentFiles.Select(file => file.Id).Concat(removedFiles.Select(file => file.Id)),
            StringComparer.Ordinal);

        return new IncrementalFileChanges(changedCurrentFiles, removedFiles, affectedFileIds);
    }

    public IncrementalMergePlan CreateMergePlan(
        IReadOnlyList<FileRecord> currentFiles,
        CodeIndexSnapshot baseline,
        IncrementalFileChanges fileChanges,
        IReadOnlyList<SymbolRecord> rebuiltSymbols)
    {
        var currentFilesById = currentFiles.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var affectedOldSymbols = baseline.Symbols.Where(symbol => fileChanges.AffectedFileIds.Contains(symbol.FileId)).ToArray();
        var removedSymbolIds = new HashSet<string>(affectedOldSymbols.Select(symbol => symbol.Id), StringComparer.Ordinal);
        var rebuiltSymbolIds = rebuiltSymbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var rebuiltSymbolId in rebuiltSymbolIds)
        {
            removedSymbolIds.Remove(rebuiltSymbolId);
        }

        var retainedSymbols = baseline.Symbols.Where(symbol => !fileChanges.AffectedFileIds.Contains(symbol.FileId));
        var mergedSymbols = retainedSymbols
            .Concat(rebuiltSymbols)
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ToArray();
        var mergedSymbolIds = mergedSymbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var symbolsById = mergedSymbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);

        var impactedEdgeSourceSymbolIds = new HashSet<string>(rebuiltSymbolIds, StringComparer.Ordinal);
        impactedEdgeSourceSymbolIds.UnionWith(removedSymbolIds);

        foreach (var edge in baseline.Edges)
        {
            if (!string.Equals(edge.Type, EdgeTypes.Contains, StringComparison.Ordinal) && removedSymbolIds.Contains(edge.To))
            {
                impactedEdgeSourceSymbolIds.Add(edge.From);
            }
        }

        var edgeRebuildFilePaths = new HashSet<string>(fileChanges.ChangedCurrentFiles.Select(file => file.Path), StringComparer.Ordinal);

        foreach (var impactedSourceSymbolId in impactedEdgeSourceSymbolIds)
        {
            if (symbolsById.TryGetValue(impactedSourceSymbolId, out var impactedSymbol) && currentFilesById.TryGetValue(impactedSymbol.FileId, out var impactedFile))
            {
                edgeRebuildFilePaths.Add(impactedFile.Path);
            }
        }

        var removedOrRebuiltSymbolIds = new HashSet<string>(affectedOldSymbols.Select(symbol => symbol.Id), StringComparer.Ordinal);
        removedOrRebuiltSymbolIds.UnionWith(rebuiltSymbolIds);

        var referenceRebuildFilePaths = new HashSet<string>(fileChanges.ChangedCurrentFiles.Select(file => file.Path), StringComparer.Ordinal);
        var targetChangedSymbolIds = new HashSet<string>(rebuiltSymbolIds, StringComparer.Ordinal);
        targetChangedSymbolIds.UnionWith(removedSymbolIds);

        foreach (var reference in baseline.References)
        {
            if (!targetChangedSymbolIds.Contains(reference.TargetSymbolId))
            {
                continue;
            }

            if (currentFilesById.TryGetValue(reference.FileId, out var referenceFile))
            {
                referenceRebuildFilePaths.Add(referenceFile.Path);
            }
        }

        var referenceAffectedFileIds = new HashSet<string>(fileChanges.RemovedFiles.Select(file => file.Id), StringComparer.Ordinal);

        foreach (var referenceRebuildFilePath in referenceRebuildFilePaths)
        {
            var currentFile = currentFiles.First(file => string.Equals(file.Path, referenceRebuildFilePath, StringComparison.Ordinal));
            referenceAffectedFileIds.Add(currentFile.Id);
        }

        return new IncrementalMergePlan(
            mergedSymbols,
            mergedSymbolIds,
            edgeRebuildFilePaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            impactedEdgeSourceSymbolIds,
            removedOrRebuiltSymbolIds,
            referenceRebuildFilePaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            referenceAffectedFileIds);
    }

    public IReadOnlyList<EdgeRecord> MergeEdges(CodeIndexSnapshot baseline, IncrementalMergePlan plan, IReadOnlyList<EdgeRecord> rebuiltEdges)
    {
        return baseline.Edges.Where(edge =>
            string.Equals(edge.Type, EdgeTypes.Contains, StringComparison.Ordinal)
                ? !plan.RemovedOrRebuiltSymbolIds.Contains(edge.To)
                : !plan.ImpactedEdgeSourceSymbolIds.Contains(edge.From))
            .Concat(rebuiltEdges)
            .OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ReferenceRecord> MergeReferences(CodeIndexSnapshot baseline, IncrementalMergePlan plan, IReadOnlyList<ReferenceRecord> rebuiltReferences)
    {
        return baseline.References
            .Where(reference => !plan.ReferenceAffectedFileIds.Contains(reference.FileId))
            .Concat(rebuiltReferences)
            .OrderBy(reference => reference.TargetSymbolId, StringComparer.Ordinal)
            .ThenBy(reference => reference.FileId, StringComparer.Ordinal)
            .ThenBy(reference => reference.Range.StartLine)
            .ThenBy(reference => reference.Range.StartColumn)
            .ThenBy(reference => reference.SourceSymbolId, StringComparer.Ordinal)
            .ToArray();
    }
}