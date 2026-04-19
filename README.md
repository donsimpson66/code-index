# CodeIndex

CodeIndex is a .NET 10 CLI that builds deterministic JSON code-index artifacts for solutions, projects, and supported source directories.

It is designed for index-first agent workflows:

- find symbols before opening files
- fetch only the source lines you need
- trace references, callers, callees, and test links from compact metadata
- reduce token usage compared with broad source reads

## Current Scope

The repository currently supports:

- Roslyn/MSBuild indexing for `.sln` and `.csproj`
- directory-based indexing for supported multi-language source trees
- deterministic JSON artifacts for `meta`, `files`, `symbols`, `edges`, `references`, and `embeddings`
- CLI query commands for symbol lookup, structural navigation, references, semantic search, call flow, test links, and excerpts
- incremental rebuilds from an existing JSON artifact directory

The project does not aim to replace an IDE or full code intelligence platform.

## Project Layout

```text
/code-index
  code-index.sln
  /src
    /CodeIndex.Core
    /CodeIndex.Roslyn
    /CodeIndex.Cli
  /tests
    /CodeIndex.Core.Tests
    /CodeIndex.Roslyn.Tests
    /CodeIndex.Cli.Tests
  /schema
  /samples
  /artifacts
```

## Prerequisites

- .NET 10 SDK
- a restoreable solution, project, or supported source directory to index

## Build

```bash
dotnet build code-index.sln
```

## Quick Start

Build an index for this repository:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
```

Rebuild after structural code changes so queries stay aligned with source.

Build an index from the sample solution:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./samples/SampleSolution/SampleSolution.sln --out ./artifacts/code-index
```

## Recommended Workflow

1. Rebuild the index if it may be stale.

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
```

2. Find likely symbols first.

```bash
dotnet run --project src/CodeIndex.Cli -- find-symbol "WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index
```

3. Confirm the owning symbol or inspect its children.

```bash
dotnet run --project src/CodeIndex.Cli -- get-symbol "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-children "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index --kind method --sort declaration
```

4. Fetch references, semantic matches, or call/test relationships as needed.

```bash
dotnet run --project src/CodeIndex.Cli -- find-references "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index --limit 20
dotnet run --project src/CodeIndex.Cli -- semantic-search "workspace inspection" --index ./artifacts/code-index --type symbol --limit 10
dotnet run --project src/CodeIndex.Cli -- get-callers "CodeIndex.Roslyn.WorkspaceLoader.LoadAsync" --index ./artifacts/code-index --kind method --limit 20
dotnet run --project src/CodeIndex.Cli -- get-tests "CodeIndex.Roslyn.WorkspaceLoader.LoadAsync" --index ./artifacts/code-index --kind method --limit 20
```

5. Read only the exact source lines you still need.

```bash
dotnet run --project src/CodeIndex.Cli -- get-excerpt "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --index ./artifacts/code-index --start 1 --end 80
```

## CLI Commands

### `inspect`

Loads a `.sln` or `.csproj` and lists discovered C# projects and source documents.

```bash
dotnet run --project src/CodeIndex.Cli -- inspect ./samples/SampleSolution/SampleSolution.sln
```

### `build`

Creates JSON index artifacts from a `.sln`, `.csproj`, or supported source directory.

```bash
dotnet run --project src/CodeIndex.Cli -- build ./samples/SampleSolution/SampleSolution.sln --out ./artifacts/code-index
```

Options:

- `--out <path>`
- `--incremental-from-index <path>`
- `--include-generated`
- `--verbose`

Notes:

- generated files are excluded by default
- validation runs before artifacts are written
- when an incremental JSON baseline is provided and file paths and hashes are unchanged, the build reuses existing symbols, edges, references, and embeddings instead of rebuilding them

### `find-symbol`

Find symbols by simple name or qualified name.

```bash
dotnet run --project src/CodeIndex.Cli -- find-symbol "FriendlyGreeter" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- find-symbol "FriendlyGreeter" --index ./artifacts/code-index --kind class --accessibility public --limit 5
```

Sort modes:

- `ranked`
- `name`
- `accessibility`

### `semantic-search`

Searches file and symbol embeddings using deterministic cosine similarity.

```bash
dotnet run --project src/CodeIndex.Cli -- semantic-search "workspace inspection" --index ./artifacts/code-index --type symbol --limit 10
```

### `find-references`

Find cached reference locations for a symbol ID or qualified name.

```bash
dotnet run --project src/CodeIndex.Cli -- find-references "SampleLibrary.FriendlyGreeter.CreateGreeting(System.String)" --index ./artifacts/code-index --limit 10
```

### `benchmark`

Compares full-source reading volume against an index-first retrieval flow.

```bash
dotnet run --project src/CodeIndex.Cli -- benchmark --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- benchmark --index ./artifacts/code-index --symbol "WorkspaceSymbolIndexBuilder" --file "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --start 1 --end 20
```

### `get-symbol`

Gets one symbol by ID or qualified name.

```bash
dotnet run --project src/CodeIndex.Cli -- get-symbol "SampleLibrary.FriendlyGreeter.CreateGreeting" --index ./artifacts/code-index
```

### `get-children`

Gets child members of a symbol.

```bash
dotnet run --project src/CodeIndex.Cli -- get-children "SampleLibrary.FriendlyGreeter" --index ./artifacts/code-index --kind method --sort declaration --limit 10
```

### `get-callees`

Gets direct call targets for a method or constructor.

```bash
dotnet run --project src/CodeIndex.Cli -- get-callees "SampleLibrary.FriendlyGreeter.CreateGreeting" --index ./artifacts/code-index --kind method --sort declaration --limit 10
```

### `get-callers`

Gets direct callers for a method or constructor.

```bash
dotnet run --project src/CodeIndex.Cli -- get-callers "SampleLibrary.FriendlyGreeter.CreateGreeting" --index ./artifacts/code-index --kind method --sort declaration --limit 10
```

### `get-test-targets`

Gets heuristic production targets for a test symbol.

```bash
dotnet run --project src/CodeIndex.Cli -- get-test-targets "SampleLibrary.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting" --index ./artifacts/code-index
```

### `get-tests`

Gets heuristic tests for a production symbol.

```bash
dotnet run --project src/CodeIndex.Cli -- get-tests "SampleLibrary.FriendlyGreeter.CreateGreeting(System.String)" --index ./artifacts/code-index
```

### `get-excerpt`

Gets exact source lines from a file.

```bash
dotnet run --project src/CodeIndex.Cli -- get-excerpt "SampleLibrary/FriendlyGreeter.cs" --index ./artifacts/code-index --start 1 --end 20
```

## Output Files

The `build` command writes:

- `code-index.meta.json`
- `code-index.files.json`
- `code-index.symbols.json`
- `code-index.edges.json`
- `code-index.references.json`
- `code-index.embeddings.json`
