namespace CodeIndex.Core;

public sealed record TextRangeRecord(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public sealed record SymbolRecord(
    string Id,
    string Name,
    string QualifiedName,
    string Kind,
    string FileId,
    TextRangeRecord Range,
    string Signature,
    string Summary,
    string? ParentId,
    string Accessibility,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride);