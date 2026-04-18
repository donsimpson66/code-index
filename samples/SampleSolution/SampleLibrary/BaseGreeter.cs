namespace SampleLibrary;

/// <summary>
/// Provides shared greeting behavior for sample greeters.
/// </summary>
public abstract class BaseGreeter : IGreeter
{
    protected readonly string greetingPrefix;

    protected BaseGreeter(string greetingPrefix)
    {
        this.greetingPrefix = greetingPrefix;
    }

    /// <summary>
    /// Gets the configured greeting prefix.
    /// </summary>
    public string Prefix => greetingPrefix;

    /// <summary>
    /// Creates a default greeting using the configured prefix.
    /// </summary>
    public virtual string CreateGreeting(string name)
    {
        return $"{greetingPrefix}, {name}.";
    }
}