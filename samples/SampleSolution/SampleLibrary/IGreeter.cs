namespace SampleLibrary;

/// <summary>
/// Defines a greeter that can produce a greeting for a person.
/// </summary>
public interface IGreeter
{
    /// <summary>
    /// Creates a greeting message for the supplied person.
    /// </summary>
    string CreateGreeting(string name);
}