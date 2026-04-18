using CodeIndex.Core;
using CodeIndex.Roslyn;

namespace CodeIndex.Roslyn.Tests;

public class WorkspaceIndexBuildersTests
{
    [Fact]
    public async Task BuildAsync_GeneratesStructuredFileSummaries()
    {
        var solutionPath = GetSolutionPath();
        var builder = new WorkspaceFileIndexBuilder();

        var files = await builder.BuildAsync(solutionPath);
        var programFile = Assert.Single(files, file => file.Path == "src/CodeIndex.Cli/Program.cs");

        Assert.Contains("top-level statements", programFile.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ExtractsWorkspaceInspectorSymbol()
    {
        var solutionPath = GetSolutionPath();
        var fileBuilder = new WorkspaceFileIndexBuilder();
        var symbolBuilder = new WorkspaceSymbolIndexBuilder();

        var files = await fileBuilder.BuildAsync(solutionPath);
        var symbols = await symbolBuilder.BuildAsync(solutionPath, files);

        Assert.Contains(symbols, symbol => symbol.Name == "WorkspaceInspector" && symbol.Kind == "class");
        Assert.Contains(symbols, symbol => symbol.Name == "InspectAsync" && symbol.Kind == "method");
    }

    [Fact]
    public async Task BuildAsync_IndexesLocalVariables_AndContainsEdges()
    {
        var projectPath = CreateTempProject(
            ("Greeter.cs", """
namespace Sample;

public class Greeter
{
    public string Run()
    {
        var total = 1;
        string message = total.ToString();
        return message;
    }
}
"""));

        try
        {
            var fileBuilder = new WorkspaceFileIndexBuilder();
            var symbolBuilder = new WorkspaceSymbolIndexBuilder();
            var edgeBuilder = new WorkspaceEdgeIndexBuilder();

            var files = await fileBuilder.BuildAsync(projectPath);
            var symbols = await symbolBuilder.BuildAsync(projectPath, files);
            var edges = await edgeBuilder.BuildAsync(projectPath);

            var runMethod = Assert.Single(symbols, symbol => symbol.Kind == SymbolKinds.Method && symbol.Name == "Run");
            var totalLocal = Assert.Single(symbols, symbol => symbol.Kind == SymbolKinds.Local && symbol.Name == "total");
            var messageLocal = Assert.Single(symbols, symbol => symbol.Kind == SymbolKinds.Local && symbol.Name == "message");

            Assert.Equal(runMethod.Id, totalLocal.ParentId);
            Assert.Equal(runMethod.Id, messageLocal.ParentId);
            Assert.Equal("local", totalLocal.Accessibility);
            Assert.Contains("total", totalLocal.Signature, StringComparison.Ordinal);

            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Contains && edge.From == runMethod.Id && edge.To == totalLocal.Id);
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Contains && edge.From == runMethod.Id && edge.To == messageLocal.Id);
        }
        finally
        {
            DeleteTempProject(projectPath);
        }
    }

    [Fact]
    public async Task BuildAsync_GeneratesContainsEdgesForIndexedSymbols()
    {
        var solutionPath = GetSolutionPath();
        var builder = new WorkspaceEdgeIndexBuilder();

        var edges = await builder.BuildAsync(solutionPath);

        Assert.Contains(edges, edge =>
            edge.Type == "contains" &&
            edge.From == "s:T:CodeIndex.Roslyn.WorkspaceInspector" &&
            edge.To == "s:M:CodeIndex.Roslyn.WorkspaceInspector.InspectAsync(System.String,System.Threading.CancellationToken)");

        Assert.Equal(edges.OrderBy(edge => edge.Type, StringComparer.Ordinal)
            .ThenBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal), edges);
    }

    [Fact]
    public async Task BuildAsync_IncludeGenerated_AddsObjDocuments()
    {
        var solutionPath = GetSolutionPath();
        var builder = new WorkspaceFileIndexBuilder();

        var files = await builder.BuildAsync(solutionPath, includeGenerated: true);

        Assert.Contains(files, file => file.Path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_CurrentSolution_IsDeterministic_AndValidates()
    {
        var solutionPath = GetSolutionPath();
        var fileBuilder = new WorkspaceFileIndexBuilder();
        var symbolBuilder = new WorkspaceSymbolIndexBuilder();
        var edgeBuilder = new WorkspaceEdgeIndexBuilder();

        var firstFiles = await fileBuilder.BuildAsync(solutionPath);
        var firstSymbols = await symbolBuilder.BuildAsync(solutionPath, firstFiles);
        var firstEdges = await edgeBuilder.BuildAsync(solutionPath);

        var secondFiles = await fileBuilder.BuildAsync(solutionPath);
        var secondSymbols = await symbolBuilder.BuildAsync(solutionPath, secondFiles);
        var secondEdges = await edgeBuilder.BuildAsync(solutionPath);

        Assert.Equal(firstFiles, secondFiles);
        Assert.Equal(firstSymbols, secondSymbols);
        Assert.Equal(firstEdges, secondEdges);

        var snapshot = new CodeIndexSnapshot(
            new CodeIndexMeta("1.0", "0.1.0", "code-index", DateTimeOffset.UtcNow, Path.GetDirectoryName(solutionPath)!, solutionPath, "solution"),
            firstFiles,
            firstSymbols,
            firstEdges,
            Array.Empty<ReferenceRecord>(),
            Array.Empty<EmbeddingRecord>());

        var validator = new CodeIndexValidator();
        validator.ValidateOrThrow(snapshot);
    }

    [Fact]
    public async Task BuildAsync_FiltersGeneratedSuffixFiles_ByDefault()
    {
        var projectPath = CreateTempProject(
            ("RegularFile.cs", "namespace Sample; public class RegularFile {}"),
            ("GeneratedFile.g.cs", "namespace Sample; public class GeneratedFile {}"),
            ("GeneratedFile.g.i.cs", "namespace Sample; public class GeneratedFileIntermediate {}"),
            ("DesignerFile.designer.cs", "namespace Sample; public class DesignerFile {}"),
            ("Auto.generated.cs", "namespace Sample; public class AutoGeneratedFile {}"));

        try
        {
            var builder = new WorkspaceFileIndexBuilder();

            var filteredFiles = await builder.BuildAsync(projectPath);
            var allFiles = await builder.BuildAsync(projectPath, includeGenerated: true);

            Assert.Contains(filteredFiles, file => file.Path == "RegularFile.cs");
            Assert.DoesNotContain(filteredFiles, file => file.Path == "GeneratedFile.g.cs");
            Assert.DoesNotContain(filteredFiles, file => file.Path == "GeneratedFile.g.i.cs");
            Assert.DoesNotContain(filteredFiles, file => file.Path == "DesignerFile.designer.cs");
            Assert.DoesNotContain(filteredFiles, file => file.Path == "Auto.generated.cs");

            Assert.Contains(allFiles, file => file.Path == "RegularFile.cs");
            Assert.Contains(allFiles, file => file.Path == "GeneratedFile.g.cs");
            Assert.Contains(allFiles, file => file.Path == "GeneratedFile.g.i.cs");
            Assert.Contains(allFiles, file => file.Path == "DesignerFile.designer.cs");
            Assert.Contains(allFiles, file => file.Path == "Auto.generated.cs");
        }
        finally
        {
            var tempDirectory = Path.GetDirectoryName(projectPath);

            if (tempDirectory is not null && Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BuildAsync_FormatsSignatures_ExtractsXmlSummary_AndUsesCanonicalPartialDeclaration()
    {
        var projectPath = CreateTempProject(
            ("AGreeter.Part1.cs", """
namespace Sample;

/// <summary>Main greeter type.</summary>
public partial class Greeter : BaseGreeter, IGreeter
{
    public string Name { get; }
    public event EventHandler? Renamed;

    public Greeter(string name)
    {
        Name = name;
    }

    /// <summary>Returns a greeting.</summary>
    public override string Greet(string person) => $"Hello {person}";
}
"""),
            ("ZGreeter.Part2.cs", """
namespace Sample;

public partial class Greeter
{
    private readonly int count = 0;
}

public interface IGreeter
{
    string Greet(string person);
}

public class BaseGreeter
{
    public virtual string Greet(string person) => person;
}
"""));

        try
        {
            var fileBuilder = new WorkspaceFileIndexBuilder();
            var symbolBuilder = new WorkspaceSymbolIndexBuilder();

            var files = await fileBuilder.BuildAsync(projectPath);
            var symbols = await symbolBuilder.BuildAsync(projectPath, files);
            var fileIdByPath = files.ToDictionary(file => file.Path, file => file.Id, StringComparer.Ordinal);

            var greeter = Assert.Single(symbols, symbol => symbol.Kind == "class" && symbol.Name == "Greeter");
            var constructor = Assert.Single(symbols, symbol => symbol.Kind == "constructor" && symbol.ParentId == greeter.Id);
            var greetMethod = Assert.Single(symbols, symbol => symbol.Kind == "method" && symbol.Name == "Greet" && symbol.ParentId == greeter.Id);
            var property = Assert.Single(symbols, symbol => symbol.Kind == "property" && symbol.Name == "Name");
            var field = Assert.Single(symbols, symbol => symbol.Kind == "field" && symbol.Name == "count");
            var eventSymbol = Assert.Single(symbols, symbol => symbol.Kind == "event" && symbol.Name == "Renamed");

            Assert.Equal(fileIdByPath["AGreeter.Part1.cs"], greeter.FileId);
            Assert.Equal(4, greeter.Range.StartLine);
            Assert.Equal("Main greeter type.", greeter.Summary);
            Assert.Equal("class Sample.Greeter", greeter.Signature);

            Assert.Equal("Sample.Greeter(string name)", constructor.Signature);
            Assert.Equal("string Sample.Greeter.Greet(string person)", greetMethod.Signature);
            Assert.Equal("Returns a greeting.", greetMethod.Summary);
            Assert.Equal("string Sample.Greeter.Name", property.Signature);
            Assert.Equal("int Sample.Greeter.count", field.Signature);
            Assert.Equal("System.EventHandler Sample.Greeter.Renamed", eventSymbol.Signature);
        }
        finally
        {
            DeleteTempProject(projectPath);
        }
    }

    [Fact]
    public async Task BuildAsync_ExtractsInheritanceImplementation_AndOverrideEdges()
    {
        var projectPath = CreateTempProject(
            ("Greeter.cs", """
namespace Sample;

public interface IGreeter
{
    string Greet(string person);
}

public class BaseGreeter
{
    public virtual string Greet(string person) => person;
}

public class Greeter : BaseGreeter, IGreeter
{
    public override string Greet(string person) => $"Hello {person}";
}
"""));

        try
        {
            var builder = new WorkspaceEdgeIndexBuilder();
            var edges = await builder.BuildAsync(projectPath);

            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Inherits && edge.From == "s:T:Sample.Greeter" && edge.To == "s:T:Sample.BaseGreeter");
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Implements && edge.From == "s:T:Sample.Greeter" && edge.To == "s:T:Sample.IGreeter");
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Overrides && edge.From == "s:M:Sample.Greeter.Greet(System.String)" && edge.To == "s:M:Sample.BaseGreeter.Greet(System.String)");
        }
        finally
        {
            DeleteTempProject(projectPath);
        }
    }

    [Fact]
    public async Task BuildAsync_ExtractsCallerAndCalleeEdges()
    {
        var projectPath = CreateTempProject(
            ("Greeter.cs", """
namespace Sample;

public class Greeter
{
    public Greeter() {}

    public string Greet(string person) => $"Hello {person}";
}

public class Caller
{
    public string Run()
    {
        var greeter = new Greeter();
        return greeter.Greet("World");
    }
}
"""));

        try
        {
            var builder = new WorkspaceEdgeIndexBuilder();
            var edges = await builder.BuildAsync(projectPath);

            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == "s:M:Sample.Caller.Run" && edge.To == "s:M:Sample.Greeter.Greet(System.String)");
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == "s:M:Sample.Caller.Run" && edge.To == "s:M:Sample.Greeter.#ctor");
        }
        finally
        {
            DeleteTempProject(projectPath);
        }
    }

    [Fact]
    public async Task BuildAsync_AddsHeuristicTestLinks()
    {
        var projectPath = CreateTempProject(
            ("Greeter.cs", """
namespace Sample;

public class FriendlyGreeter
{
    public string CreateGreeting(string name) => $"Hello {name}";
}
"""),
            ("FriendlyGreeterTests.cs", """
namespace Xunit;

public sealed class FactAttribute : System.Attribute {}
"""),
            ("FriendlyGreeterBehaviorTests.cs", """
using Xunit;

namespace Sample.Tests;

public class FriendlyGreeterTests
{
    [Fact]
    public void CreateGreeting_ReturnsGreeting()
    {
        var greeter = new Sample.FriendlyGreeter();
        _ = greeter.CreateGreeting("World");
    }
}
"""));

        try
        {
            var builder = new WorkspaceEdgeIndexBuilder();
            var edges = await builder.BuildAsync(projectPath);

            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Tests && edge.From == "s:M:Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting" && edge.To == "s:M:Sample.FriendlyGreeter.CreateGreeting(System.String)");
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Tests && edge.From == "s:M:Sample.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting" && edge.To == "s:T:Sample.FriendlyGreeter");
        }
        finally
        {
            DeleteTempProject(projectPath);
        }
    }

    [Fact]
    public async Task BuildAsync_ExtractsReferencesForIndexedSymbols()
    {
        var projectPath = CreateTempProject(
            ("Greeter.cs", """
namespace Sample;

public class Greeter
{
    public string Greet(string name) => $"Hello {name}";
}
"""),
            ("Program.cs", """
namespace Sample;

public class Caller
{
    public string Run()
    {
        var greeter = new Greeter();
        return greeter.Greet("World");
    }
}
"""));

        try
        {
            var fileBuilder = new WorkspaceFileIndexBuilder();
            var symbolBuilder = new WorkspaceSymbolIndexBuilder();
            var referenceBuilder = new WorkspaceReferenceIndexBuilder();

            var files = await fileBuilder.BuildAsync(projectPath);
            var symbols = await symbolBuilder.BuildAsync(projectPath, files);
            var references = await referenceBuilder.BuildAsync(projectPath, files, symbols);

            var greetMethod = Assert.Single(symbols, symbol => symbol.Kind == SymbolKinds.Method && symbol.Name == "Greet");
            var callerMethod = Assert.Single(symbols, symbol => symbol.Kind == SymbolKinds.Method && symbol.Name == "Run");
            var programFile = Assert.Single(files, file => file.Path == "Program.cs");

            Assert.Contains(references, reference =>
                reference.TargetSymbolId == greetMethod.Id &&
                reference.SourceSymbolId == callerMethod.Id &&
                reference.FileId == programFile.Id &&
                reference.LineText.Contains("greeter.Greet", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempProject(projectPath);
        }
    }

    [Fact]
    public async Task BuildAsync_SampleSolution_IndexesExpectedSymbolsAndEdges()
    {
        var solutionPath = GetSampleSolutionPath();
        var fileBuilder = new WorkspaceFileIndexBuilder();
        var symbolBuilder = new WorkspaceSymbolIndexBuilder();
        var edgeBuilder = new WorkspaceEdgeIndexBuilder();

        var files = await fileBuilder.BuildAsync(solutionPath);
        var symbols = await symbolBuilder.BuildAsync(solutionPath, files);
        var edges = await edgeBuilder.BuildAsync(solutionPath);

        Assert.Contains(files, file => file.Path == "SampleLibrary/FriendlyGreeter.cs");
        Assert.Contains(symbols, symbol => symbol.Kind == "interface" && symbol.QualifiedName == "SampleLibrary.IGreeter");
        Assert.Contains(symbols, symbol => symbol.Kind == "class" && symbol.QualifiedName == "SampleLibrary.BaseGreeter");
        Assert.Contains(symbols, symbol => symbol.Kind == "class" && symbol.QualifiedName == "SampleLibrary.FriendlyGreeter");
        Assert.Contains(symbols, symbol => symbol.Kind == "method" && symbol.QualifiedName == "string SampleLibrary.FriendlyGreeter.CreateGreeting(string)" && symbol.Summary == "Creates an enthusiastic greeting and tracks how many times it has been used.");
        Assert.Contains(edges, edge => edge.Type == EdgeTypes.Inherits && edge.From == "s:T:SampleLibrary.FriendlyGreeter" && edge.To == "s:T:SampleLibrary.BaseGreeter");
        Assert.Contains(edges, edge => edge.Type == EdgeTypes.Implements && edge.From == "s:T:SampleLibrary.BaseGreeter" && edge.To == "s:T:SampleLibrary.IGreeter");
        Assert.Contains(edges, edge => edge.Type == EdgeTypes.Overrides && edge.From == "s:M:SampleLibrary.FriendlyGreeter.CreateGreeting(System.String)" && edge.To == "s:M:SampleLibrary.BaseGreeter.CreateGreeting(System.String)");
    }

    private static string GetSolutionPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../code-index.sln"));
    }

    private static string GetSampleSolutionPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/SampleSolution/SampleSolution.sln"));
    }

    private static string CreateTempProject(params (string FileName, string Content)[] files)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"code-index-roslyn-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        var projectPath = Path.Combine(tempDirectory, "Sample.csproj");
        File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
""");

        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(tempDirectory, file.FileName), file.Content);
        }

        return projectPath;
    }

    private static void DeleteTempProject(string projectPath)
    {
        var tempDirectory = Path.GetDirectoryName(projectPath);

        if (tempDirectory is not null && Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}