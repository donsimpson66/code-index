namespace CodeIndex.Core;

public sealed record FileRecord(
    string Id,
    string Path,
    string ProjectName,
    string Language,
    string Hash,
    string Summary);