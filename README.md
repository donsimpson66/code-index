# CodeIndex

A .NET 10 C# project for a CLI tool that will build a compact JSON code index
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

This tool generates a deterministic JSON index to support that workflow.

## Current Scope

Initial implementation focuses on:

- C#
- Roslyn/MSBuild loading
- symbol extraction
- structural relationships
- compact JSON output
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
- `build` for `meta`, `files`, `symbols`, and `edges` artifacts
- validation before artifact write
- artifact-backed query commands:
  - `find-symbol`
  - `get-symbol`
  - `get-children`
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

### `inspect`

Loads a `.sln` or `.csproj` and lists discovered C# projects and source documents.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- inspect ./code-index.sln
```

### `build`

Creates JSON index artifacts from a `.sln` or `.csproj`.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./MySolution.sln --out ./artifacts/code-index
```

Options:

- `--out <path>`
- `--include-generated`
- `--verbose`

Notes:

- generated files are excluded by default
- validation runs before artifacts are written

### `find-symbol`

Find symbols by simple name or qualified name.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- find-symbol "OrderService"
dotnet run --project src/CodeIndex.Cli -- find-symbol "MyApp.Services.OrderService.SubmitOrder"
dotnet run --project src/CodeIndex.Cli -- find-symbol "OrderService" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- find-symbol "OrderService" --index ./artifacts/code-index --kind class --accessibility public --limit 5
dotnet run --project src/CodeIndex.Cli -- find-symbol "OrderService" --index ./artifacts/code-index --sort accessibility --limit 5

Sort modes:

- `ranked` (default)
- `name`
- `accessibility`
```

### `get-symbol`

Get one symbol by ID or qualified name.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-symbol "MyApp.Services.OrderService.SubmitOrder"
dotnet run --project src/CodeIndex.Cli -- get-symbol "s:T:MyApp.Services.OrderService" --index ./artifacts/code-index
```

### `get-children`

Get child members of a symbol.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-children "MyApp.Services.OrderService"
dotnet run --project src/CodeIndex.Cli -- get-children "MyApp.Services.OrderService" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-children "MyApp.Services.OrderService" --index ./artifacts/code-index --kind method --limit 10
dotnet run --project src/CodeIndex.Cli -- get-children "MyApp.Services.OrderService" --index ./artifacts/code-index --kind method --sort declaration --limit 10

Sort modes:

- `name` (default)
- `accessibility`
- `declaration`
```

### `get-excerpt`

Get exact source lines from a file.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-excerpt "src/Services/OrderService.cs" --start 42 --end 78
dotnet run --project src/CodeIndex.Cli -- get-excerpt "src/Services/OrderService.cs" --index ./artifacts/code-index --start 42 --end 78
```

## Output Files

The `build` command writes:

- `code-index.meta.json`
- `code-index.files.json`
- `code-index.symbols.json`
- `code-index.edges.json`

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

- references on demand
- cached query index
- incremental indexing
- richer relationships
- optional storage backends

## Non-Goals for MVP

- full call graph
- all references for all symbols
- local variable indexing
- vector embeddings
- non-C# support
- HTTP service
- database backend

## Notes

If solution loading fails, verify:

- the target solution restores successfully
- required SDKs/workloads are installed
- the project builds normally outside the tool

## License

Add your preferred license here.