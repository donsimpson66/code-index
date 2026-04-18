namespace CodeIndex.Core;

public static class SymbolKinds
{
    public const string Namespace = "namespace";
    public const string Record = "record";
    public const string Class = "class";
    public const string Interface = "interface";
    public const string Struct = "struct";
    public const string Enum = "enum";
    public const string Delegate = "delegate";
    public const string Constructor = "constructor";
    public const string Method = "method";
    public const string Property = "property";
    public const string Field = "field";
    public const string Event = "event";
    public const string Local = "local";

    public static int GetRank(string? kind)
    {
        return kind switch
        {
            Class => 0,
            Record => 1,
            Interface => 2,
            Struct => 3,
            Enum => 4,
            Delegate => 5,
            Namespace => 6,
            Constructor => 7,
            Method => 8,
            Property => 9,
            Field => 10,
            Event => 11,
            Local => 12,
            _ => 13
        };
    }
}