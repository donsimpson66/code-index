# TASKS - C# AI Code Index Tool

## Goal

Build a .NET 10 CLI tool that uses Roslyn to generate compact code index
artifacts for C# solutions and projects, with JSON output and optional SQLite
storage.

The tool should help AI agents:

- find symbols quickly
- reduce token usage
- reduce context size
- fetch only relevant file ranges

## Milestone 0 - Repository Bootstrap

### Tasks

- [x] Create solution `code-index.sln`
- [x] Create projects:
  - [x] `CodeIndex.Core`
  - [x] `CodeIndex.Roslyn`
  - [x] `CodeIndex.Cli`
  - [x] `CodeIndex.Core.Tests`
  - [x] `CodeIndex.Roslyn.Tests`
  - [x] `CodeIndex.Cli.Tests`
- [x] Set all projects to target `net10.0`
- [x] Add project references:
  - [x] `CodeIndex.Roslyn -> CodeIndex.Core`
  - [x] `CodeIndex.Cli -> CodeIndex.Core`
  - [x] `CodeIndex.Cli -> CodeIndex.Roslyn`
  - [x] test projects -> corresponding production projects
- [x] Add NuGet packages:
  - [x] `Microsoft.CodeAnalysis.CSharp.Workspaces`
  - [x] `Microsoft.CodeAnalysis.Workspaces.MSBuild`
  - [x] `Microsoft.Build.Locator`
  - [x] `System.CommandLine`
- [x] Verify `dotnet build` passes

### Done Criteria

- [x] Solution builds successfully
- [x] Tests compile
- [x] CLI project can run

---

## Milestone 1 - Core Models and Schema

### Tasks

- [x] Create `CodeIndexMeta`
- [x] Create `FileRecord`
- [x] Create `TextRangeRecord`
- [x] Create `SymbolRecord`
- [x] Create `EdgeRecord`
- [x] Create enums or constants for:
  - [x] symbol kinds
  - [x] edge types
- [x] Create JSON serialization helpers
- [x] Create path normalization helper
- [x] Create deterministic ID generation helper
- [x] Create validation service for output consistency

### Done Criteria

- [x] Models serialize correctly with `System.Text.Json`
- [x] Paths normalize to `/`
- [x] IDs are deterministic
- [x] Validation catches broken references and duplicates

---

## Milestone 2 - Solution and Project Loading Spike

### Tasks

- [x] Register MSBuild using `Microsoft.Build.Locator`
- [x] Implement solution loader
- [x] Implement project loader
- [x] Enumerate C# projects
- [x] Enumerate Roslyn documents
- [x] Print project and document paths from CLI spike command
- [x] Handle load failures clearly

### Done Criteria

- [x] Can open a real `.sln`
- [x] Can open a real `.csproj`
- [x] Can enumerate C# source documents
- [x] Errors are concise and actionable

---

## Milestone 3 - File Record Extraction

### Tasks

- [x] Create file indexing service
- [x] Normalize relative file paths
- [x] Compute file hashes
- [x] Record project name
- [x] Record language as `C#`
- [x] Add default file summary strategy
- [x] Filter generated files by default:
  - [x] `bin/**`
  - [x] `obj/**`
  - [x] `*.g.cs`
  - [x] `*.g.i.cs`
  - [x] `*.designer.cs`
  - [x] `*.generated.cs`

### Done Criteria

- [x] `code-index.files.json` is generated
- [x] File IDs are stable
- [x] Generated-file filtering works

---

## Milestone 4 - Symbol Extraction MVP

### Tasks

- [x] Implement declaration walker
- [x] Extract namespaces
- [x] Extract classes
- [x] Extract interfaces
- [x] Extract structs
- [x] Extract records
- [x] Extract enums
- [x] Extract delegates
- [x] Extract constructors
- [x] Extract methods
- [x] Extract properties
- [x] Extract fields
- [x] Extract events
- [x] Map Roslyn symbols to internal symbol records
- [x] Capture:
  - [x] `Id`
  - [x] `Name`
  - [x] `QualifiedName`
  - [x] `Kind`
  - [x] `FileId`
  - [x] `Range`
  - [x] `Signature`
  - [x] `Summary`
  - [x] `ParentId`
  - [x] `Accessibility`
  - [x] `IsStatic`
  - [x] `IsAbstract`
  - [x] `IsVirtual`
  - [x] `IsOverride`

### Done Criteria

- [x] `code-index.symbols.json` is generated
- [x] Supported symbol kinds appear correctly
- [x] File/range information is valid
- [x] Signatures are useful and readable

---

## Milestone 5 - XML Documentation and Signature Formatting

### Tasks

- [x] Extract XML documentation via Roslyn
- [x] Parse `<summary>`
- [x] Trim and normalize summary whitespace
- [x] Add fallback summary generator if no XML docs exist
- [x] Implement normalized signature formatter for:
  - [x] types
  - [x] methods
  - [x] constructors
  - [x] properties
  - [x] fields
  - [x] events

### Done Criteria

- [x] XML summaries appear when available
- [x] Fallback summaries are short and useful
- [x] Signatures reduce need to inspect full source

---

## Milestone 6 - Structural Edge Extraction

### Tasks

- [x] Implement `contains` edges
- [x] Implement `inherits` edges
- [x] Implement `implements` edges
- [x] Implement `overrides` edges
- [x] Avoid duplicate edges
- [x] Sort edges deterministically

### Done Criteria

- [x] `code-index.edges.json` is generated
- [x] Parent-child edges are accurate
- [x] Base type and interface edges are accurate
- [x] Override edges are accurate

---

## Milestone 7 - Build Command

### Tasks

- [x] Implement `build` CLI command
- [x] Accept `.sln` or `.csproj`
- [x] Add `--out <path>`
- [x] Add `--include-generated true|false`
- [x] Add `--verbose`
- [x] Write output files:
  - [x] `code-index.meta.json`
  - [x] `code-index.files.json`
  - [x] `code-index.symbols.json`
  - [x] `code-index.edges.json`
- [x] Ensure deterministic ordering before writing
- [x] Run validation before final write

### Done Criteria

- [x] `build` works end-to-end
- [x] Output is deterministic across repeated runs
- [x] Output validates successfully

---

## Milestone 8 - Query Commands for AI Use

### Tasks

- [x] Implement index reader
- [x] Implement `find-symbol`
- [x] Implement `get-symbol`
- [x] Implement `get-children`
- [x] Implement `get-excerpt`
- [x] Return compact JSON by default
- [x] Support lookup by qualified name
- [x] Support lookup by symbol ID where practical
- [x] Add query filters and limits for symbol lookups
- [x] Add query sort controls for symbol and child results

### Done Criteria

- [x] AI-friendly queries return small JSON results
- [x] `find-symbol` works for name and qualified name
- [x] `get-symbol` returns one clear symbol payload
- [x] `get-children` supports compact filtering and sorting
- [x] `get-excerpt` returns exact file lines

---

## Milestone 9 - Validation and Robustness

### Tasks

- [x] Validate file references
- [x] Validate parent references
- [x] Validate edge references
- [x] Validate range values
- [x] Validate duplicate IDs
- [x] Add partial class handling rule
- [x] Improve error messages
- [x] Add clear non-zero exit codes for failures

### Done Criteria

- [x] Invalid output is blocked
- [x] Errors explain what failed and where
- [x] Partial-class behavior is documented

---

## Milestone 10 - Tests

### Core Tests

- [x] Path normalization tests
- [x] ID generation tests
- [x] JSON serialization tests
- [x] validation rule tests

### Roslyn Tests

- [x] class extraction tests
- [x] method extraction tests
- [x] namespace extraction tests
- [x] inheritance extraction tests
- [x] interface implementation tests
- [x] override extraction tests
- [x] XML summary extraction tests
- [x] generated-file filter tests

### CLI Tests

- [x] `build` command test
- [x] `find-symbol` command test
- [x] `get-symbol` command test
- [x] `get-children` command test
- [x] `get-excerpt` command test
- [x] invalid input error tests
- [x] query command test using current repository solution index

### Done Criteria

- [x] Core behavior is broadly covered
- [x] Main CLI flows are covered
- [x] Current repository solution can be indexed in tests

---

## Milestone 11 - Sample Solution

### Tasks

- [x] Create `samples/SampleSolution`
- [x] Add sample project with:
  - [x] interface
  - [x] base class
  - [x] derived class
  - [x] override method
  - [x] properties and fields
  - [x] XML docs
- [x] Use current repository solution in tests
- [x] Use sample solution in README examples

### Done Criteria

- [x] Sample solution indexes successfully
- [x] Sample output is predictable
- [x] Tests use sample solution consistently

---

## Backlog - Post MVP

Do not implement these until MVP is stable.

- [x] `FindReferencesAsync`
- [x] cached references index
- [x] caller/callee analysis
- [x] SQLite backend
- [x] incremental indexing
- [ ] API server
- [ ] multi-language support
- [x] local variable indexing
- [x] vector embeddings
- [x] test linking heuristics

## Milestone 12 - SQLite Store and Query Path

### Tasks

- [x] Add SQLite package support in core storage layer
- [x] Create SQLite schema for `meta`, `files`, `symbols`, and `edges`
- [x] Write full snapshot to SQLite during `build --db-out`
- [x] Support `find-symbol --db`
- [x] Support `get-symbol --db`
- [x] Support `get-children --db`
- [x] Support `get-excerpt --db`
- [x] Support `benchmark --db`
- [x] Use direct metadata, file, and count queries for `benchmark --db`
- [x] Validate mutual exclusivity of `--index` and `--db`
- [x] Add CLI tests for SQLite build/query/benchmark flows
- [x] Benchmark SQLite mode against the current repository
- [x] Ignore generated `bin/` and `obj/` outputs at the repository root

### Done Criteria

- [x] SQLite-backed commands return the same shape as JSON-backed commands
- [x] CLI tests cover both storage paths
- [x] Current repository builds and benchmarks through the SQLite path
- [x] Database benchmark metrics do not require a full snapshot read

---

## Priority Order

Implement in this order:

1. repository bootstrap
2. core models
3. solution loading
4. file extraction
5. symbol extraction
6. edge extraction
7. build command
8. query commands
9. validation
10. tests
11. documentation polish

---

## Definition of MVP Done

MVP is done when all of the following are true:

- [x] A `.sln` or `.csproj` can be indexed
- [x] The four JSON files are produced
- [x] Supported C# symbol kinds are indexed
- [x] Structural edges are produced correctly
- [x] Query commands return compact JSON
- [x] Output is deterministic
- [x] Basic tests pass
- [x] README usage examples work