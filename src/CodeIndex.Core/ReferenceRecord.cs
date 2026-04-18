namespace CodeIndex.Core;

public sealed record ReferenceRecord(
    string TargetSymbolId,
    string? SourceSymbolId,
    string FileId,
    TextRangeRecord Range,
    string LineText);