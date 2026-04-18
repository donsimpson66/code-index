namespace CodeIndex.Core;

public sealed record CodeIndexMeta(
    string SchemaVersion,
    string ToolVersion,
    string RepoName,
    DateTimeOffset GeneratedAtUtc,
    string SourceRoot,
    string InputPath,
    string InputKind);