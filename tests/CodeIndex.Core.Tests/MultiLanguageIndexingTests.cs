using System.Text.Json;

namespace CodeIndex.Core.Tests;

public class MultiLanguageIndexingTests
{
    [Fact]
    public async Task BuildAsync_ExtractsJavaAndGoCallEdgesAndReferences()
    {
        var sourceDirectory = CreateTempSourceDirectory("java-go");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDirectory, "src"));

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.java"),
                "package demo;\npublic class Greeter\n{\n    public void greet()\n    {\n        helper();\n        new Greeter();\n    }\n\n    private void helper() {}\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "greeter.go"),
                "package demo\n\ntype Greeter struct{}\n\nfunc (g *Greeter) Greet() {\n    helper()\n    g.log()\n    _ = Greeter{}\n}\n\nfunc (g *Greeter) log() {}\n\nfunc helper() {}\n");

            var (files, symbols, edges, references) = await BuildIndexAsync(sourceDirectory);

            Assert.Contains(files, file => file.Path == "src/Greeter.java" && file.Language == "Java");
            Assert.Contains(files, file => file.Path == "src/greeter.go" && file.Language == "Go");

            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "demo.Greeter.greet", "f:src/Greeter.java") && edge.To == GetSymbolId(symbols, "demo.Greeter.helper", "f:src/Greeter.java"));
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "demo.Greeter.Greet", "f:src/greeter.go") && edge.To == GetSymbolId(symbols, "demo.helper", "f:src/greeter.go"));
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "demo.Greeter.Greet", "f:src/greeter.go") && edge.To == GetSymbolId(symbols, "demo.Greeter.log", "f:src/greeter.go"));

            Assert.Contains(references, reference => reference.TargetSymbolId == GetSymbolId(symbols, "demo.Greeter.helper", "f:src/Greeter.java") && reference.SourceSymbolId == GetSymbolId(symbols, "demo.Greeter.greet", "f:src/Greeter.java"));
            Assert.Contains(references, reference => reference.TargetSymbolId == GetSymbolId(symbols, "demo.Greeter", "f:src/Greeter.java") && reference.SourceSymbolId == GetSymbolId(symbols, "demo.Greeter.greet", "f:src/Greeter.java"));
            Assert.Contains(references, reference => reference.TargetSymbolId == GetSymbolId(symbols, "demo.Greeter.log", "f:src/greeter.go") && reference.SourceSymbolId == GetSymbolId(symbols, "demo.Greeter.Greet", "f:src/greeter.go"));
        }
        finally
        {
            DeleteTempSourceDirectory(sourceDirectory);
        }
    }

    [Fact]
    public async Task BuildAsync_ExtractsTypeScriptCallEdgesAndReferences()
    {
        var sourceDirectory = CreateTempSourceDirectory("typescript");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "ui"));

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "helper.ts"),
                "export class Helper {\n    log(): string {\n        return 'ok';\n    }\n}\n\nexport function boot(): Helper {\n    return new Helper();\n}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "ui", "greeter.ts"),
                "import { Helper, boot } from './helper';\n\nexport class Greeter {\n    private helper: Helper;\n\n    constructor() {\n        this.helper = boot();\n    }\n\n    greet(): string {\n        return this.helper.log();\n    }\n}\n");

            var (files, symbols, edges, references) = await BuildIndexAsync(sourceDirectory);

            Assert.Contains(files, file => file.Path == "src/ui/helper.ts" && file.Language == "TypeScript");
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "ui.greeter.Greeter.constructor", "f:src/ui/greeter.ts") && edge.To == GetSymbolId(symbols, "ui.helper.boot", "f:src/ui/helper.ts"));
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "ui.greeter.Greeter.greet", "f:src/ui/greeter.ts") && edge.To == GetSymbolId(symbols, "ui.helper.Helper.log", "f:src/ui/helper.ts"));
            Assert.Contains(references, reference => reference.TargetSymbolId == GetSymbolId(symbols, "ui.helper.boot", "f:src/ui/helper.ts") && reference.SourceSymbolId == GetSymbolId(symbols, "ui.greeter.Greeter.constructor", "f:src/ui/greeter.ts"));
            Assert.Contains(references, reference => reference.TargetSymbolId == GetSymbolId(symbols, "ui.helper.Helper.log", "f:src/ui/helper.ts") && reference.SourceSymbolId == GetSymbolId(symbols, "ui.greeter.Greeter.greet", "f:src/ui/greeter.ts"));
        }
        finally
        {
            DeleteTempSourceDirectory(sourceDirectory);
        }
    }

    [Fact]
    public async Task BuildAsync_ExtractsPythonAndPhpCallEdgesAndReferences()
    {
        var sourceDirectory = CreateTempSourceDirectory("python-php");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDirectory, "src", "pkg"));

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "pkg", "helper.py"),
                "class Helper:\n    def log(self):\n        return 'ok'\n\ndef boot():\n    return Helper()\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "pkg", "greeter.py"),
                "from pkg.helper import Helper, boot\n\nclass Greeter:\n    def greet(self):\n        return boot()\n\n    def build(self):\n        return Helper()\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Helper.php"),
                "<?php\nnamespace Demo\\Support;\n\nclass Helper\n{\n    public static function log() {}\n}\n\nfunction boot() {}\n");

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "src", "Greeter.php"),
                "<?php\nnamespace Demo;\n\nuse Demo\\Support\\Helper;\nuse function Demo\\Support\\boot;\n\nclass Greeter\n{\n    public function greet()\n    {\n        boot();\n        Helper::log();\n        $this->format();\n    }\n\n    private function format()\n    {\n    }\n}\n");

            var (files, symbols, edges, references) = await BuildIndexAsync(sourceDirectory);

            Assert.Contains(files, file => file.Path == "src/pkg/helper.py" && file.Language == "Python");
            Assert.Contains(files, file => file.Path == "src/Greeter.php" && file.Language == "PHP");

            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "pkg.greeter.Greeter.greet", "f:src/pkg/greeter.py") && edge.To == GetSymbolId(symbols, "pkg.helper.boot", "f:src/pkg/helper.py"));
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "Demo.Greeter.greet", "f:src/Greeter.php") && edge.To == GetSymbolId(symbols, "Demo.Support.boot", "f:src/Helper.php"));
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "Demo.Greeter.greet", "f:src/Greeter.php") && edge.To == GetSymbolId(symbols, "Demo.Support.Helper.log", "f:src/Helper.php"));
            Assert.Contains(edges, edge => edge.Type == EdgeTypes.Calls && edge.From == GetSymbolId(symbols, "Demo.Greeter.greet", "f:src/Greeter.php") && edge.To == GetSymbolId(symbols, "Demo.Greeter.format", "f:src/Greeter.php"));

            Assert.Contains(references, reference => reference.TargetSymbolId == GetSymbolId(symbols, "pkg.helper.Helper", "f:src/pkg/helper.py") && reference.SourceSymbolId == GetSymbolId(symbols, "pkg.greeter.Greeter.build", "f:src/pkg/greeter.py"));
        }
        finally
        {
            DeleteTempSourceDirectory(sourceDirectory);
        }
    }

    private static async Task<(IReadOnlyList<FileRecord> Files, IReadOnlyList<SymbolRecord> Symbols, IReadOnlyList<EdgeRecord> Edges, IReadOnlyList<ReferenceRecord> References)> BuildIndexAsync(string sourceDirectory)
    {
        var fileBuilder = new MultiLanguageFileIndexBuilder();
        var symbolBuilder = new MultiLanguageSymbolIndexBuilder();
        var edgeBuilder = new MultiLanguageEdgeIndexBuilder();
        var referenceBuilder = new MultiLanguageReferenceIndexBuilder();

        var files = await fileBuilder.BuildAsync(sourceDirectory);
        var symbols = await symbolBuilder.BuildAsync(sourceDirectory, files);
        var edges = await edgeBuilder.BuildAsync(sourceDirectory);
        var references = await referenceBuilder.BuildAsync(sourceDirectory, files, symbols);

        return (files, symbols, edges, references);
    }

    private static string GetSymbolId(IReadOnlyList<SymbolRecord> symbols, string qualifiedName, string fileId)
    {
        return symbols.Single(symbol =>
            string.Equals(symbol.QualifiedName, qualifiedName, StringComparison.Ordinal) &&
            string.Equals(symbol.FileId, fileId, StringComparison.Ordinal)).Id;
    }

    private static string CreateTempSourceDirectory(string suffix)
    {
        return Path.Combine(Path.GetTempPath(), $"code-index-core-multi-language-{suffix}-{Guid.NewGuid():N}");
    }

    private static void DeleteTempSourceDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}