# C# AI Code Index Tool - Implementation Plan

## Objective

Build a C#-first code indexing tool that creates a compact JSON project index optimized for AI agents.

## Current Status

Completed today:

- repository bootstrap is complete and the solution builds/tests on .NET 10
- Roslyn/MSBuild loading works for real `.sln` and `.csproj` inputs
- CLI `inspect` command lists C# projects and relevant source documents
- CLI `build` command writes:
  - `code-index.meta.json`
  - `code-index.files.json`
  - `code-index.symbols.json`
- file indexing is implemented with normalized paths, stable file IDs, hashes, and improved source-aware summaries
- initial symbol extraction is implemented for namespaces, types, delegates, constructors, methods, properties, fields, and events
- XML doc `<summary>` extraction and fallback summaries are implemented
- structural edge extraction is implemented and `code-index.edges.json` is generated
- validation service is implemented and runs before build artifacts are written
- query commands are implemented: `find-symbol`, `get-symbol`, `get-children`, and `get-excerpt`
- query commands now support filtering, limits, and explicit sort modes for smaller result sets
- CLI tests now cover `inspect`, `build`, `find-symbol`, `get-symbol`, `get-children`, `get-excerpt`, and invalid-input error paths
- `build` now supports `--out`, `--include-generated`, and `--verbose`

Still pending:

- broader generated-file handling policy beyond the current build-time include/exclude switch

The tool must help AI agents:

- find symbols quickly
- reduce token usage
- reduce context size
- fetch only relevant code ranges
- avoid loading entire files or entire repositories when unnecessary

This tool is not intended to be a full IDE replacement or general-purpose code intelligence platform.

## Target Platform

- .NET 10
- C#
- Roslyn-based indexing

## Primary Goal

Create a CLI tool that:

1. Opens a `.sln` or `.csproj`
2. Indexes C# source files
3. Extracts key symbols and structural relationships
4. Writes deterministic JSON artifacts
5. Supports small, query-friendly outputs for AI workflows

## MVP Scope

### Include

- solution/project loading
- file discovery through Roslyn/MSBuild
- symbol extraction for:
  - namespaces
  - classes
  - interfaces
  - structs
  - records
  - enums
  - delegates
  - constructors
  - methods
  - properties
  - fields
  - events
- symbol metadata:
  - name
  - qualified name
  - kind
  - file path
  - line/column range
  - signature
  - XML doc summary
  - parent symbol
  - accessibility
  - static/abstract/virtual/override flags
- structural relationships:
  - contains
  - inherits
  - implements
  - overrides
- deterministic JSON output
- query CLI commands for AI-friendly lookup

### Exclude from MVP

- local variable indexing
- full call graph
- all references for all symbols
- comments not attached to declarations
- source code embedding/vector search
- non-C# languages
- database backend
- web API

## Why This Exists

AI agents do not need entire source files most of the time. They work better if they can first get compact metadata like:

- where a symbol is
- what it is
- what contains it
- what it inherits or implements
- what its signature is
- whether there is XML documentation

Only after that should the agent fetch exact code lines if needed.

## Tech Stack

Use the following unless there is a strong reason to change:

- .NET 10
- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- `Microsoft.Build.Locator`
- `System.CommandLine`
- `System.Text.Json`

## Suggested Solution Structure

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
    code-index.schema.md
  /samples
    /SampleSolution
  /artifacts
```

## Project Responsibilities

### `CodeIndex.Core`

Contains:

- domain models
- schema definitions
- JSON serialization
- path normalization helpers
- ID generation
- validation logic

### `CodeIndex.Roslyn`

Contains:

- MSBuild/Roslyn solution loading
- syntax and semantic model processing
- symbol extraction
- relationship extraction
- XML documentation extraction

### `CodeIndex.Cli`

Contains:

- command-line entry point
- command definitions
- argument parsing
- orchestration
- console output

## CLI Commands

Implement these commands.

Currently implemented:

- `inspect`
- `build`
- `find-symbol`
- `get-symbol`
- `get-children`
- `get-excerpt`

### `build`

Build the index from a `.sln` or `.csproj`.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./MySolution.sln --out ./artifacts/code-index
```

Options:

- `--out <path>`
- `--include-generated false|true`
- `--verbose`

Current implementation supports `--out <path>`.
Current implementation supports `--out <path>`, `--include-generated`, and `--verbose`.

### `find-symbol`

Find symbols by simple name or qualified name.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- find-symbol "OrderService"
dotnet run --project src/CodeIndex.Cli -- find-symbol "MyApp.Services.OrderService.SubmitOrder"
```

### `get-symbol`

Return a single symbol record by ID or qualified name.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-symbol "MyApp.Services.OrderService.SubmitOrder"
```

### `get-children`

Return child symbols for a symbol.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-children "MyApp.Services.OrderService"
```

### `get-excerpt`

Return a file excerpt by path and line range.

Example:

```bash
dotnet run --project src/CodeIndex.Cli -- get-excerpt "src/Services/OrderService.cs" --start 42 --end 78
```

## Output Files

Write these files into the output directory.

- `code-index.meta.json`
- `code-index.files.json`
- `code-index.symbols.json`
- `code-index.edges.json`

Current implementation writes all four files.

## JSON Schema Design

Keep the schema compact and AI-friendly.

### `code-index.meta.json`

```json
{
  "schemaVersion": "1.0",
  "toolVersion": "0.1.0",
  "repoName": "MySolution",
  "generatedAtUtc": "2026-04-18T05:00:00Z",
  "sourceRoot": "/repo",
  "solutionPath": "/repo/MySolution.sln"
}
```

### `code-index.files.json`

```json
[
  {
    "id": "f:src/Services/OrderService.cs",
    "path": "src/Services/OrderService.cs",
    "projectName": "MyApp",
    "language": "C#",
    "hash": "sha256:...",
    "summary": "Service layer for order submission and fulfillment."
  }
]
```

### `code-index.symbols.json`

```json
[
  {
    "id": "s:MyApp.Services.OrderService",
    "name": "OrderService",
    "qualifiedName": "MyApp.Services.OrderService",
    "kind": "class",
    "fileId": "f:src/Services/OrderService.cs",
    "range": {
      "startLine": 10,
      "startColumn": 1,
      "endLine": 120,
      "endColumn": 2
    },
    "signature": "public class OrderService : IOrderService",
    "summary": "Coordinates order validation, persistence, and fulfillment.",
    "parentId": null,
    "accessibility": "public",
    "isStatic": false,
    "isAbstract": false,
    "isVirtual": false,
    "isOverride": false
  },
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
]
```

### `code-index.edges.json`

```json
[
  {
    "type": "contains",
    "from": "s:MyApp.Services.OrderService",
    "to": "s:MyApp.Services.OrderService.SubmitOrder"
  },
  {
    "type": "implements",
    "from": "s:MyApp.Services.OrderService",
    "to": "s:MyApp.Services.IOrderService"
  }
]
```

## Schema Rules

### File IDs

Use normalized relative paths and prefix with `f:`.

Example:

- `f:src/Services/OrderService.cs`

### Symbol IDs

Use deterministic IDs. Do not use line numbers as identity.

Format:

- `s:<qualifiedName>`

If needed for uniqueness, append symbol kind:

- `s:<qualifiedName>|method`

### Range Rules

Store:

- start line
- start column
- end line
- end column

Use 1-based line and column values in JSON output.

## Symbol Kinds

Support these values:

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

Optional later:

- `enum_member`

## Edge Types

Support these values:

- `contains`
- `inherits`
- `implements`
- `overrides`

Do not add more edge types in MVP unless there is a strong reason.

## Implementation Constraints

### Use Roslyn, not regex

All symbol extraction must be based on Roslyn syntax trees and semantic models.

### Prefer semantic symbols over syntax-only names

Use Roslyn declared symbols whenever possible.

### Use XML doc summaries only

For summaries:

1. Use XML documentation `<summary>` if available
2. If absent, generate a short fallback summary from symbol kind and signature
3. Keep summaries short

### Deterministic output

The tool must generate stable output for the same codebase.

Sort:

- files by normalized path
- symbols by file path, then start line, then qualified name
- edges by type, then `from`, then `to`

### Normalize paths

Use forward slashes in all JSON output.

Examples:

- `src/Services/OrderService.cs`
- not `src\Services\OrderService.cs`

## Generated File Filtering

Exclude these by default:

- `bin/**`
- `obj/**`
- `*.g.cs`
- `*.g.i.cs`
- `*.designer.cs`
- `*.generated.cs`

Allow override via CLI option later if needed.

## Partial Class Handling

For MVP:

- index each declaration based on Roslyn symbol identity
- prefer one logical symbol record per declared symbol
- use the first source location for primary file/range

Document this limitation clearly.

## Roslyn APIs to Use

Use these Roslyn capabilities where applicable:

- `MSBuildLocator.RegisterDefaults()`
- `MSBuildWorkspace.Create()`
- `OpenSolutionAsync(...)`
- `OpenProjectAsync(...)`
- `GetSyntaxRootAsync(...)`
- `GetSemanticModelAsync(...)`
- `GetDeclaredSymbol(...)`
- `GetDocumentationCommentXml()`
- `Locations`
- `ContainingType`
- `ContainingNamespace`
- `BaseType`
- `Interfaces`
- `IsOverride`

## Recommended Extraction Flow

### Phase 1: Load

- register MSBuild
- open solution or project
- enumerate C# projects
- enumerate source documents

### Phase 2: File Records

For each document:

- normalize relative path
- compute file hash
- create file record

### Phase 3: Symbol Records

Walk declarations and create records for supported symbol kinds.

For each symbol capture:

- ID
- simple name
- qualified name
- kind
- file
- range
- signature
- summary
- parent ID
- accessibility
- flags

### Phase 4: Edge Records

Build relationships for:

- containing type/member
- base type
- implemented interfaces
- overrides

### Phase 5: Write Output

Write deterministic JSON files.

## Query Behavior for AI Use

The CLI should return compact JSON by default for query commands.

The goal is to give AI agents small results, not huge dumps.

### `find-symbol` result example

```json
[
  {
    "id": "s:MyApp.Services.OrderService",
    "qualifiedName": "MyApp.Services.OrderService",
    "kind": "class",
    "filePath": "src/Services/OrderService.cs",
    "startLine": 10,
    "endLine": 120,
    "signature": "public class OrderService : IOrderService",
    "summary": "Coordinates order validation, persistence, and fulfillment."
  }
]
```

### `get-symbol` result example

```json
{
  "id": "s:MyApp.Services.OrderService.SubmitOrder",
  "qualifiedName": "MyApp.Services.OrderService.SubmitOrder",
  "kind": "method",
  "filePath": "src/Services/OrderService.cs",
  "range": {
    "startLine": 42,
    "startColumn": 5,
    "endLine": 78,
    "endColumn": 6
  },
  "signature": "public async Task<Result> SubmitOrder(Order order, CancellationToken cancellationToken)",
  "summary": "Validates the order, saves it, and triggers fulfillment.",
  "parentId": "s:MyApp.Services.OrderService"
}
```

## Acceptance Criteria

### Build Command

- can index a real `.sln`
- can index a single `.csproj`
- produces all four JSON files
- output is deterministic across repeated runs

### Symbols

- extracts supported symbol kinds correctly
- includes valid file/range information
- includes useful signature text
- includes XML doc summary where present

### Edges

- `contains` is accurate for parent-child relationships
- `inherits` is accurate for base types
- `implements` is accurate for interfaces
- `overrides` is accurate where applicable

### Queries

- `find-symbol` returns compact results
- `get-symbol` works with qualified name
- `get-children` returns child members
- `get-excerpt` returns exact source lines

### Quality

- all output paths are normalized
- no duplicate IDs
- no broken file references
- no broken parent references
- no broken edge references

## Non-Goals for Initial Delivery

Do not implement these in the first pass:

- `FindReferencesAsync` for all symbols
- caller/callee analysis
- SQLite storage
- HTTP server
- vector embeddings
- support for non-C# projects
- indexing local variables

## Development Phases

## Phase 0 - Bootstrap

Create the solution and projects.

Tasks:

- create solution
- create `Core`, `Roslyn`, and `Cli` projects
- add package references
- wire project references
- set target framework to `net10.0`

## Phase 1 - Solution Loading Spike

Tasks:

- register MSBuild
- open `.sln`
- list projects
- list documents
- verify C# documents can be read

Deliverable:

- CLI command that prints project and document paths

## Phase 2 - Symbol Extraction MVP

Tasks:

- extract classes and methods first
- add file/range/signature/summary
- serialize `symbols.json`

Deliverable:

- first usable symbol index

## Phase 3 - Full MVP Symbol Coverage

Tasks:

- add interfaces, structs, records, enums, delegates
- add constructors, properties, fields, events
- add namespace support
- add parent/child relationships

Deliverable:

- complete MVP symbol set

## Phase 4 - Edge Extraction

Tasks:

- add `contains`
- add `inherits`
- add `implements`
- add `overrides`

Deliverable:

- `edges.json`

## Phase 5 - Query Commands

Tasks:

- implement `find-symbol`
- implement `get-symbol`
- implement `get-children`
- implement `get-excerpt`

Deliverable:

- AI-friendly CLI queries

## Phase 6 - Validation and Cleanup

Tasks:

- add validation checks
- add output ordering
- add generated-file filtering
- improve error messages
- test against sample and real solutions

Deliverable:

- stable MVP release

## Suggested Domain Models

Implement these records or classes in `CodeIndex.Core`.

### Meta

- `CodeIndexMeta`
  - `SchemaVersion`
  - `ToolVersion`
  - `RepoName`
  - `GeneratedAtUtc`
  - `SourceRoot`
  - `SolutionPath`

### File

- `FileRecord`
  - `Id`
  - `Path`
  - `ProjectName`
  - `Language`
  - `Hash`
  - `Summary`

### Range

- `TextRangeRecord`
  - `StartLine`
  - `StartColumn`
  - `EndLine`
  - `EndColumn`

### Symbol

- `SymbolRecord`
  - `Id`
  - `Name`
  - `QualifiedName`
  - `Kind`
  - `FileId`
  - `Range`
  - `Signature`
  - `Summary`
  - `ParentId`
  - `Accessibility`
  - `IsStatic`
  - `IsAbstract`
  - `IsVirtual`
  - `IsOverride`

### Edge

- `EdgeRecord`
  - `Type`
  - `From`
  - `To`

## Validation Rules

Implement validation before writing output.

Checks:

- every `fileId` in symbols exists
- every `parentId` in symbols exists or is null
- every edge `from` exists
- every edge `to` exists
- no duplicate file IDs
- no duplicate symbol IDs
- ranges are valid and positive

## Error Handling Expectations

The tool should fail clearly when:

- solution cannot load
- restore/build context is missing
- output directory is invalid
- JSON cannot be written

Errors should be concise and actionable.

## Test Strategy

Create tests for:

### Core

- path normalization
- deterministic ID generation
- JSON serialization
- validation rules

### Roslyn

- symbol extraction from sample code
- signature generation
- XML summary extraction
- edge extraction
- generated-file filtering

### CLI

- build command success
- query command success
- error output for bad inputs

## Performance Expectations

For MVP:

- correctness matters more than maximum speed
- full-solution indexing is acceptable
- no incremental indexing required yet

Still, avoid obvious inefficiencies such as:

- recomputing semantic models unnecessarily
- repeated file reads when document text is already available
- loading output files multiple times inside one command

## Coding Guidelines

- keep methods small
- use clear names
- avoid unnecessary abstractions
- prefer records for data models where appropriate
- keep JSON schema stable
- document assumptions in code comments only where useful
- keep console output concise

## Deliverables

The implementation is complete when all of the following exist:

- .NET 10 solution
- buildable CLI
- Roslyn-based indexer
- deterministic JSON outputs
- query commands
- tests for core behavior
- sample solution for validation
- short README with usage examples

## Nice-to-Have After MVP

Do not block MVP on these.

- references on demand using Roslyn `SymbolFinder`
- cached query index
- SQLite backend
- incremental indexing
- test-file linking
- per-project summaries
- optional NDJSON output
- LSP-style query mode

## Final Instruction to Implementing Agent

Build the smallest correct version first.

Priority order:

1. load solution/project
2. extract file records
3. extract symbol records
4. extract edges
5. write deterministic JSON
6. add query commands
7. add validation and tests

Do not over-engineer v1.
Do not add features outside MVP unless required to make MVP work.
Prefer correctness, determinism, and compact output over complexity.