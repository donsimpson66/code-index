# CodeIndex

A .NET 10 C# project for a CLI tool that builds a compact code index
for C# solutions and projects using Roslyn.

The index is optimized for AI agents that need to:

- find symbols quickly
- reduce token usage
- reduce context size
- fetch only relevant source ranges

## Why

AI tools often do not need whole files or whole repositories.

They work better when they can first ask:

- where is this symbol
- what kind of thing is it
- what contains it
- what does it inherit or implement
- what is its signature
- what source lines should I read next

This tool generates deterministic JSON artifacts and an optional SQLite index to support that workflow.

## MVP Status

The MVP is complete.

Implemented and validated in the current repository:

- solution and project indexing
- deterministic JSON artifacts for `meta`, `files`, `symbols`, `edges`, and `references`
- deterministic embeddings for files and symbols
- SQLite-backed build, query, and benchmark flows
- AI-oriented query commands for symbol lookup, reference lookup, semantic search, call flow, test links, and excerpt retrieval
- repository-scale tests and benchmark coverage

## Current Scope

Initial implementation focuses on:

- C#
- Roslyn/MSBuild loading
- symbol extraction
- structural relationships
- cached references
- local variables
- heuristic test links
- deterministic vector embeddings
- compact JSON output
- SQLite-backed query storage
- CLI query commands

It does not aim to replace an IDE or full code intelligence platform.

## Tech Stack

- .NET 10
- C#
- Roslyn
- `System.Text.Json`
- `System.CommandLine`

## Current Status

The current implementation supports:

- Roslyn/MSBuild loading for `.sln` and `.csproj`
- `inspect` for project and source-document discovery
- `build` for `meta`, `files`, `symbols`, `edges`, `references`, and `embeddings` artifacts, plus optional SQLite output
- `benchmark` for comparing full-source reading volume versus index-first retrieval volume
- validation before artifact write
- artifact-backed and SQLite-backed query commands:
  - `find-symbol`
  - `semantic-search`
  - `find-references`
  - `get-symbol`
  - `get-children`
  - `get-callees`
  - `get-callers`
  - `get-test-targets`
  - `get-tests`
  - `get-excerpt`

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
- a restore/buildable C# solution or project to index

Verify:

```bash
dotnet --info
```

## Build

```bash
dotnet build code-index.sln
```

## Run

### Build the index for this repository

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
```

To also write the SQLite store used by database-backed queries:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index --db-out ./artifacts/code-index/code-index.db
```

This repository keeps a current index in `artifacts/code-index`.
Rebuild it after structural code changes so queries stay aligned with source.

### Build an index from a solution

```bash
dotnet run --project src/CodeIndex.Cli -- build ./samples/SampleSolution/SampleSolution.sln --out ./artifacts/code-index
```

### Build an index from a project

```bash
dotnet run --project src/CodeIndex.Cli -- build ./src/MyProject/MyProject.csproj --out ./artifacts/code-index
```

## CLI Commands

The commands below reflect the current implemented CLI surface.

## Agent Workflow

Before searching through source files directly, an agent should consult the
index for this repository in `./artifacts/code-index` or the SQLite store at
`./artifacts/code-index/code-index.db`.

Recommended flow:

1. rebuild the repository index if it may be stale:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --db-out ./artifacts/code-index/code-index.db
```

2. find likely symbols first:

```bash
dotnet run --project src/CodeIndex.Cli -- find-symbol "WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- find-symbol "WorkspaceSymbolIndexBuilder" --db ./artifacts/code-index/code-index.db
```

3. fetch one symbol record or its children:

```bash
dotnet run --project src/CodeIndex.Cli -- get-symbol "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-children "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index --kind method --sort declaration
dotnet run --project src/CodeIndex.Cli -- get-symbol "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --db ./artifacts/code-index/code-index.db
dotnet run --project src/CodeIndex.Cli -- get-children "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --db ./artifacts/code-index/code-index.db --kind method --sort declaration
```

4. fetch cached references when you need usage sites:

```bash
dotnet run --project src/CodeIndex.Cli -- find-references "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index --limit 20
dotnet run --project src/CodeIndex.Cli -- find-references "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --db ./artifacts/code-index/code-index.db --limit 20
```

5. use semantic search when you know the concept but not the exact symbol name:

```bash
dotnet run --project src/CodeIndex.Cli -- semantic-search "workspace inspection" --index ./artifacts/code-index --type symbol --limit 10
dotnet run --project src/CodeIndex.Cli -- semantic-search "sqlite storage" --db ./artifacts/code-index/code-index.db --type file --limit 10
```

6. inspect call relationships when behavior flow matters:

```bash
dotnet run --project src/CodeIndex.Cli -- get-callees "CodeIndex.Cli.CliApplication.RunAsync" --index ./artifacts/code-index --kind method --limit 20
dotnet run --project src/CodeIndex.Cli -- get-callers "CodeIndex.Roslyn.WorkspaceLoader.LoadAsync" --db ./artifacts/code-index/code-index.db --kind method --limit 20
```

7. inspect likely test coverage when behavior matters:

```bash
dotnet run --project src/CodeIndex.Cli -- get-tests "CodeIndex.Roslyn.WorkspaceLoader.LoadAsync" --index ./artifacts/code-index --kind method --limit 20
dotnet run --project src/CodeIndex.Cli -- get-test-targets "CodeIndex.Roslyn.Tests.WorkspaceIndexBuildersTests.BuildAsync_AddsHeuristicTestLinks" --db ./artifacts/code-index/code-index.db --limit 20
```

8. read only the exact file lines you still need:

```bash
dotnet run --project src/CodeIndex.Cli -- get-excerpt "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --index ./artifacts/code-index --start 1 --end 80
dotnet run --project src/CodeIndex.Cli -- get-excerpt "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --db ./artifacts/code-index/code-index.db --start 1 --end 80
```

Index-first guidance:

- use `find-symbol` before broad text search when you know or suspect a symbol name
- use `get-symbol` to confirm file, signature, summary, and parent relationship
- use `get-children` to inspect a type or namespace surface before opening full files
- use `find-references` when you need concrete usage sites before opening more files
- use `semantic-search` when you know behavior or terminology but not the precise symbol spelling
- use `get-callees` and `get-callers` to trace behavior flow before reading whole implementations
- use `get-tests` and `get-test-targets` to estimate which behavior is covered before changing code
- use `get-excerpt` for the smallest source slice that answers the current question
- fall back to broad file or text search only when the index does not cover the needed detail

### `inspect`

Loads a `.sln` or `.csproj` and lists discovered C# projects and source documents.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- inspect ./samples/SampleSolution/SampleSolution.sln
```

### `build`

Creates JSON index artifacts and optional SQLite storage from a `.sln` or `.csproj`.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./samples/SampleSolution/SampleSolution.sln --out ./artifacts/code-index
```

Options:

- `--out <path>`
- `--db-out <path>`
- `--incremental-from-index <path>`
- `--incremental-from-db <path>`
- `--include-generated`
- `--verbose`

Notes:

- generated files are excluded by default
- validation runs before artifacts are written
- pass `--out`, `--db-out`, or both depending on which backing store you want
- when an incremental baseline is provided and file paths and hashes are unchanged, the build reuses existing symbols, edges, and references instead of rebuilding them
- embeddings are generated deterministically from file and symbol text and are reused when an incremental baseline is unchanged

### `semantic-search`

Searches file and symbol embeddings using deterministic cosine similarity over indexed text.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- semantic-search "workspace inspection" --index ./artifacts/code-index --type symbol --limit 10
```

Options:

- `--index <path>` or `--db <path>`
- `--type <symbol|file>`
- `--limit <n>`

### `find-symbol`

Find symbols by simple name or qualified name.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- find-symbol "FriendlyGreeter"
dotnet run --project src/CodeIndex.Cli -- find-symbol "SampleLibrary.FriendlyGreeter.CreateGreeting"
dotnet run --project src/CodeIndex.Cli -- find-symbol "FriendlyGreeter" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- find-symbol "FriendlyGreeter" --db ./artifacts/code-index/code-index.db
dotnet run --project src/CodeIndex.Cli -- find-symbol "FriendlyGreeter" --index ./artifacts/code-index --kind class --accessibility public --limit 5
dotnet run --project src/CodeIndex.Cli -- find-symbol "FriendlyGreeter" --index ./artifacts/code-index --sort accessibility --limit 5

Sort modes:

- `ranked` (default)
- `name`
- `accessibility`
```

### `find-references`

Find cached reference locations for a symbol ID or qualified name.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- find-references "SampleLibrary.FriendlyGreeter" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- find-references "SampleLibrary.FriendlyGreeter" --db ./artifacts/code-index/code-index.db
dotnet run --project src/CodeIndex.Cli -- find-references "SampleLibrary.FriendlyGreeter.CreateGreeting(System.String)" --index ./artifacts/code-index --limit 10
```

### `benchmark`

Compare the indexed project corpus against its code-index artifacts, and
optionally measure a representative symbol-plus-excerpt retrieval flow.

Examples:

```bash
dotnet run --project src/CodeIndex.Cli -- benchmark --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- benchmark --db ./artifacts/code-index/code-index.db
dotnet run --project src/CodeIndex.Cli -- benchmark --index ./artifacts/code-index --symbol "WorkspaceSymbolIndexBuilder"
dotnet run --project src/CodeIndex.Cli -- benchmark --index ./artifacts/code-index --symbol "WorkspaceSymbolIndexBuilder" --file "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --start 1 --end 80
dotnet run --project src/CodeIndex.Cli -- benchmark --db ./artifacts/code-index/code-index.db --symbol "WorkspaceSymbolIndexBuilder" --file "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --start 1 --end 20
```

The output includes:

- raw source file count, byte count, and line count for the indexed project
- total artifact bytes for `meta`, `files`, `symbols`, and `edges`
- a whole-project ratio comparing index bytes to source bytes
- optional targeted retrieval costs for a symbol query and excerpt flow
- a recommendation indicating whether the index should be treated as a selective navigation tool or whether whole-index reads may be competitive

When `--db` is used, the whole-project benchmark path reads database metadata,
file rows, and symbol and edge counts directly instead of materializing the
full SQLite snapshot before reporting metrics.

Current repository SQLite benchmark:

- database bytes: `331776`
- raw source bytes: `146664`
- whole database to source ratio: `2.262`
- representative index-first flow bytes: `26609`
- representative flow vs full source ratio: `0.181`

### `get-symbol`

Get one symbol by ID or qualified name.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-symbol "SampleLibrary.FriendlyGreeter.CreateGreeting"
dotnet run --project src/CodeIndex.Cli -- get-symbol "s:T:SampleLibrary.FriendlyGreeter" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-symbol "s:T:SampleLibrary.FriendlyGreeter" --db ./artifacts/code-index/code-index.db
```

### `get-children`

Get child members of a symbol.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-children "SampleLibrary.FriendlyGreeter"
dotnet run --project src/CodeIndex.Cli -- get-children "SampleLibrary.FriendlyGreeter" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-children "SampleLibrary.FriendlyGreeter" --db ./artifacts/code-index/code-index.db
dotnet run --project src/CodeIndex.Cli -- get-children "SampleLibrary.FriendlyGreeter" --index ./artifacts/code-index --kind method --limit 10
dotnet run --project src/CodeIndex.Cli -- get-children "SampleLibrary.FriendlyGreeter" --index ./artifacts/code-index --kind method --sort declaration --limit 10

Sort modes:

- `name` (default)
- `accessibility`
- `declaration`
```

### `get-callees`

Get direct call targets for a method or constructor.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-callees "SampleLibrary.FriendlyGreeter.CreateGreeting" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-callees "SampleLibrary.FriendlyGreeter.CreateGreeting" --db ./artifacts/code-index/code-index.db
dotnet run --project src/CodeIndex.Cli -- get-callees "SampleLibrary.FriendlyGreeter.CreateGreeting" --index ./artifacts/code-index --kind method --sort declaration --limit 10
```

### `get-callers`

Get direct callers for a method or constructor.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-callers "SampleLibrary.FriendlyGreeter.CreateGreeting" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-callers "SampleLibrary.FriendlyGreeter.CreateGreeting" --db ./artifacts/code-index/code-index.db
dotnet run --project src/CodeIndex.Cli -- get-callers "SampleLibrary.FriendlyGreeter.CreateGreeting" --index ./artifacts/code-index --kind method --sort declaration --limit 10
```

### `get-test-targets`

Get heuristic production targets for a test symbol.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-test-targets "SampleLibrary.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-test-targets "SampleLibrary.Tests.FriendlyGreeterTests.CreateGreeting_ReturnsGreeting" --db ./artifacts/code-index/code-index.db
```

### `get-tests`

Get heuristic tests for a production symbol.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-tests "SampleLibrary.FriendlyGreeter.CreateGreeting(System.String)" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-tests "SampleLibrary.FriendlyGreeter.CreateGreeting(System.String)" --db ./artifacts/code-index/code-index.db
```

### `get-excerpt`

Get exact source lines from a file.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-excerpt "SampleLibrary/FriendlyGreeter.cs" --start 1 --end 20
dotnet run --project src/CodeIndex.Cli -- get-excerpt "SampleLibrary/FriendlyGreeter.cs" --index ./artifacts/code-index --start 1 --end 20
dotnet run --project src/CodeIndex.Cli -- get-excerpt "SampleLibrary/FriendlyGreeter.cs" --db ./artifacts/code-index/code-index.db --start 1 --end 20
```

## Output Files

The `build` command writes:

- `code-index.meta.json`
- `code-index.files.json`
- `code-index.symbols.json`
- `code-index.edges.json`
- `code-index.references.json`
- optional `code-index.db`

## Output Design

The JSON is designed to be:

- deterministic
- compact
- easy to query
- useful for AI retrieval workflows

### Example file record

```json
{
  "id": "f:src/Services/OrderService.cs",
  "path": "src/Services/OrderService.cs",
  "projectName": "MyApp",
  "language": "C#",
  "hash": "sha256:...",
  "summary": "Service layer for order submission and fulfillment."
}
```

### Example symbol record

```json
{
  "id": "s:MyApp.Services.OrderService.SubmitOrder",
  "name": "SubmitOrder",
  "qualifiedName": "MyApp.Services.OrderService.SubmitOrder",
  "kind": "method",
  "fileId": "f:src/Services/OrderService.cs",
  "range": {
    "startLine": 42,
    "startColumn": 5,
    "endLine": 78,
    "endColumn": 6
  },
  "signature": "public async Task<Result> SubmitOrder(Order order, CancellationToken cancellationToken)",
  "summary": "Validates the order, saves it, and triggers fulfillment.",
  "parentId": "s:MyApp.Services.OrderService",
  "accessibility": "public",
  "isStatic": false,
  "isAbstract": false,
  "isVirtual": false,
  "isOverride": false
}
```

### Example edge record

```json
{
  "type": "implements",
  "from": "s:MyApp.Services.OrderService",
  "to": "s:MyApp.Services.IOrderService"
}
```

## Symbol Kinds

Initial supported kinds:

- `namespace`
- `class`
- `interface`
- `struct`
- `record`
- `enum`
- `delegate`
- `constructor`
- `method`
- `property`
- `field`
- `event`

## Edge Types

Initial supported edge types:

- `contains`
- `inherits`
- `implements`
- `overrides`

## Generated File Filtering

By default, generated files should be excluded:

- `bin/**`
- `obj/**`
- `*.g.cs`
- `*.g.i.cs`
- `*.designer.cs`
- `*.generated.cs`

The repository root `.gitignore` also ignores `bin/` and `obj/` directories so
generated build outputs do not pollute code review diffs.

## Design Rules

### Use Roslyn, not regex

All extraction should be based on Roslyn syntax trees and semantic models.

### Prefer semantic symbols

Use Roslyn declared symbols whenever possible.

### Keep summaries short

Use XML `<summary>` first. If not available, fall back to a short generated
summary.

### Keep output deterministic

Sort consistently before writing:

- files by path
- symbols by file, line, qualified name
- edges by type, from, to

### Normalize paths

Always use forward slashes in JSON output.

## Development

### Restore

```bash
dotnet restore CodeIndex.sln
```

### Build

```bash
dotnet build CodeIndex.sln
```

### Test

```bash
dotnet test CodeIndex.sln
```

## Intended Workflow for AI Agents

1. build the index
2. search for a symbol
3. inspect symbol metadata
4. inspect structural edges
5. fetch exact source lines only when needed

That keeps token use lower than loading full files up front.

## Status

This project starts with a C# MVP first.

Planned later, if needed:

- richer relationships
- optional storage backends

## Non-Goals for MVP

- full call graph
- local variable indexing
- vector embeddings
- non-C# support
- HTTP service

## Notes

If solution loading fails, verify:

- the target solution restores successfully
- required SDKs/workloads are installed
- the project builds normally outside the tool

## License

Add your preferred license here.