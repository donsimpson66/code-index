namespace CodeIndex.Core;

public sealed record EdgeRecord(
    string Type,
    string From,
    string To);

public static class EdgeTypes
{
    public const string Contains = "contains";
    public const string Inherits = "inherits";
    public const string Implements = "implements";
    public const string Overrides = "overrides";
    public const string Calls = "calls";
    public const string Tests = "tests";
}