namespace SampleLibrary;

/// <summary>
/// A sample greeter that adds a more enthusiastic greeting.
/// </summary>
public sealed class FriendlyGreeter : BaseGreeter
{
    private int greetingCount;

    public FriendlyGreeter(string greetingPrefix)
        : base(greetingPrefix)
    {
    }

    /// <summary>
    /// Gets the number of greetings created by this instance.
    /// </summary>
    public int GreetingCount => greetingCount;

    /// <summary>
    /// Creates an enthusiastic greeting and tracks how many times it has been used.
    /// </summary>
    public override string CreateGreeting(string name)
    {
        greetingCount++;
        return $"{Prefix}, {name}! Welcome to CodeIndex.";
    }
}