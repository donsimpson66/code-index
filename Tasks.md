# TASKS - C# AI Code Index Tool

## Goal

Build a .NET 10 CLI tool that uses Roslyn to generate a compact JSON code
index for C# solutions and projects.

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
- [ ] Create `EdgeRecord`
- [ ] Create enums or constants for:
  - [ ] symbol kinds
  - [ ] edge types
- [x] Create JSON serialization helpers
- [x] Create path normalization helper
- [x] Create deterministic ID generation helper
- [ ] Create validation service for output consistency

### Done Criteria

- [x] Models serialize correctly with `System.Text.Json`
- [x] Paths normalize to `/`
- [x] IDs are deterministic
- [ ] Validation catches broken references and duplicates

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
- [ ] Filter generated files by default:
  - [x] `bin/**`
  - [x] `obj/**`
  - [ ] `*.g.cs`
  - [ ] `*.g.i.cs`
  - [ ] `*.designer.cs`
  - [ ] `*.generated.cs`

### Done Criteria

- [x] `code-index.files.json` is generated
- [x] File IDs are stable
- [ ] Generated-file filtering works

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
- [ ] Implement normalized signature formatter for:
  - [ ] types
  - [ ] methods
  - [ ] constructors
  - [ ] properties
  - [ ] fields
  - [ ] events

### Done Criteria

- [x] XML summaries appear when available
- [x] Fallback summaries are short and useful
- [ ] Signatures reduce need to inspect full source

---

## Milestone 6 - Structural Edge Extraction

### Tasks

- [ ] Implement `contains` edges
- [ ] Implement `inherits` edges
- [ ] Implement `implements` edges
- [ ] Implement `overrides` edges
- [ ] Avoid duplicate edges
- [ ] Sort edges deterministically

### Done Criteria

- [ ] `code-index.edges.json` is generated
- [ ] Parent-child edges are accurate
- [ ] Base type and interface edges are accurate
- [ ] Override edges are accurate

---

## Milestone 7 - Build Command

### Tasks

- [x] Implement `build` CLI command
- [x] Accept `.sln` or `.csproj`
- [x] Add `--out <path>`
- [ ] Add `--include-generated true|false`
- [ ] Add `--verbose`
- [ ] Write output files:
  - [x] `code-index.meta.json`
  - [x] `code-index.files.json`
  - [x] `code-index.symbols.json`
  - [ ] `code-index.edges.json`
- [x] Ensure deterministic ordering before writing
- [ ] Run validation before final write

### Done Criteria

- [x] `build` works end-to-end
- [ ] Output is deterministic across repeated runs
- [ ] Output validates successfully

---

## Milestone 8 - Query Commands for AI Use

### Tasks

- [ ] Implement index reader
- [ ] Implement `find-symbol`
- [ ] Implement `get-symbol`
- [ ] Implement `get-children`
- [ ] Implement `get-excerpt`
- [ ] Return compact JSON by default
- [ ] Support lookup by qualified name
- [ ] Support lookup by symbol ID where practical

### Done Criteria

- [ ] AI-friendly queries return small JSON results
- [ ] `find-symbol` works for name and qualified name
- [ ] `get-symbol` returns one clear symbol payload
- [ ] `get-excerpt` returns exact file lines

---

## Milestone 9 - Validation and Robustness

### Tasks

- [ ] Validate file references
- [ ] Validate parent references
- [ ] Validate edge references
- [ ] Validate range values
- [ ] Validate duplicate IDs
- [ ] Add partial class handling rule
- [ ] Improve error messages
- [ ] Add clear non-zero exit codes for failures

### Done Criteria

- [ ] Invalid output is blocked
- [ ] Errors explain what failed and where
- [ ] Partial-class behavior is documented

---

## Milestone 10 - Tests

### Core Tests

- [ ] Path normalization tests
- [ ] ID generation tests
- [ ] JSON serialization tests
- [ ] validation rule tests

### Roslyn Tests

- [ ] class extraction tests
- [ ] method extraction tests
- [ ] namespace extraction tests
- [ ] inheritance extraction tests
- [ ] interface implementation tests
- [ ] override extraction tests
- [ ] XML summary extraction tests
- [ ] generated-file filter tests

### CLI Tests

- [ ] `build` command test
- [ ] `find-symbol` command test
- [ ] `get-symbol` command test
- [ ] `get-children` command test
- [ ] `get-excerpt` command test
- [ ] invalid input error tests

### Done Criteria

- [ ] Core behavior is covered
- [ ] Main CLI flows are covered
- [ ] Sample solution can be indexed in tests

---

## Milestone 11 - Sample Solution

### Tasks

- [ ] Create `samples/SampleSolution`
- [ ] Add sample project with:
  - [ ] interface
  - [ ] base class
  - [ ] derived class
  - [ ] override method
  - [ ] properties and fields
  - [ ] XML docs
- [ ] Use sample solution in tests
- [ ] Use sample solution in README examples

### Done Criteria

- [ ] Sample solution indexes successfully
- [ ] Sample output is predictable
- [ ] Tests use sample solution consistently

---

## Backlog - Post MVP

Do not implement these until MVP is stable.

- [ ] `FindReferencesAsync`
- [ ] cached references index
- [ ] caller/callee analysis
- [ ] SQLite backend
- [ ] incremental indexing
- [ ] API server
- [ ] multi-language support
- [ ] local variable indexing
- [ ] vector embeddings
- [ ] test linking heuristics

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

- [ ] A `.sln` or `.csproj` can be indexed
- [ ] The four JSON files are produced
- [ ] Supported C# symbol kinds are indexed
- [ ] Structural edges are produced correctly
- [ ] Query commands return compact JSON
- [ ] Output is deterministic
- [ ] Basic tests pass
- [ ] README usage examples work