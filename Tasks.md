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

- [ ] Create solution `CodeIndex.sln`
- [ ] Create projects:
  - [ ] `CodeIndex.Core`
  - [ ] `CodeIndex.Roslyn`
  - [ ] `CodeIndex.Cli`
  - [ ] `CodeIndex.Core.Tests`
  - [ ] `CodeIndex.Roslyn.Tests`
  - [ ] `CodeIndex.Cli.Tests`
- [ ] Set all projects to target `net10.0`
- [ ] Add project references:
  - [ ] `CodeIndex.Roslyn -> CodeIndex.Core`
  - [ ] `CodeIndex.Cli -> CodeIndex.Core`
  - [ ] `CodeIndex.Cli -> CodeIndex.Roslyn`
  - [ ] test projects -> corresponding production projects
- [ ] Add NuGet packages:
  - [ ] `Microsoft.CodeAnalysis.CSharp.Workspaces`
  - [ ] `Microsoft.CodeAnalysis.Workspaces.MSBuild`
  - [ ] `Microsoft.Build.Locator`
  - [ ] `System.CommandLine`
- [ ] Verify `dotnet build` passes

### Done Criteria

- [ ] Solution builds successfully
- [ ] Tests compile
- [ ] CLI project can run

---

## Milestone 1 - Core Models and Schema

### Tasks

- [ ] Create `CodeIndexMeta`
- [ ] Create `FileRecord`
- [ ] Create `TextRangeRecord`
- [ ] Create `SymbolRecord`
- [ ] Create `EdgeRecord`
- [ ] Create enums or constants for:
  - [ ] symbol kinds
  - [ ] edge types
- [ ] Create JSON serialization helpers
- [ ] Create path normalization helper
- [ ] Create deterministic ID generation helper
- [ ] Create validation service for output consistency

### Done Criteria

- [ ] Models serialize correctly with `System.Text.Json`
- [ ] Paths normalize to `/`
- [ ] IDs are deterministic
- [ ] Validation catches broken references and duplicates

---

## Milestone 2 - Solution and Project Loading Spike

### Tasks

- [ ] Register MSBuild using `Microsoft.Build.Locator`
- [ ] Implement solution loader
- [ ] Implement project loader
- [ ] Enumerate C# projects
- [ ] Enumerate Roslyn documents
- [ ] Print project and document paths from CLI spike command
- [ ] Handle load failures clearly

### Done Criteria

- [ ] Can open a real `.sln`
- [ ] Can open a real `.csproj`
- [ ] Can enumerate C# source documents
- [ ] Errors are concise and actionable

---

## Milestone 3 - File Record Extraction

### Tasks

- [ ] Create file indexing service
- [ ] Normalize relative file paths
- [ ] Compute file hashes
- [ ] Record project name
- [ ] Record language as `C#`
- [ ] Add default file summary strategy
- [ ] Filter generated files by default:
  - [ ] `bin/**`
  - [ ] `obj/**`
  - [ ] `*.g.cs`
  - [ ] `*.g.i.cs`
  - [ ] `*.designer.cs`
  - [ ] `*.generated.cs`

### Done Criteria

- [ ] `code-index.files.json` is generated
- [ ] File IDs are stable
- [ ] Generated-file filtering works

---

## Milestone 4 - Symbol Extraction MVP

### Tasks

- [ ] Implement declaration walker
- [ ] Extract namespaces
- [ ] Extract classes
- [ ] Extract interfaces
- [ ] Extract structs
- [ ] Extract records
- [ ] Extract enums
- [ ] Extract delegates
- [ ] Extract constructors
- [ ] Extract methods
- [ ] Extract properties
- [ ] Extract fields
- [ ] Extract events
- [ ] Map Roslyn symbols to internal symbol records
- [ ] Capture:
  - [ ] `Id`
  - [ ] `Name`
  - [ ] `QualifiedName`
  - [ ] `Kind`
  - [ ] `FileId`
  - [ ] `Range`
  - [ ] `Signature`
  - [ ] `Summary`
  - [ ] `ParentId`
  - [ ] `Accessibility`
  - [ ] `IsStatic`
  - [ ] `IsAbstract`
  - [ ] `IsVirtual`
  - [ ] `IsOverride`

### Done Criteria

- [ ] `code-index.symbols.json` is generated
- [ ] Supported symbol kinds appear correctly
- [ ] File/range information is valid
- [ ] Signatures are useful and readable

---

## Milestone 5 - XML Documentation and Signature Formatting

### Tasks

- [ ] Extract XML documentation via Roslyn
- [ ] Parse `<summary>`
- [ ] Trim and normalize summary whitespace
- [ ] Add fallback summary generator if no XML docs exist
- [ ] Implement normalized signature formatter for:
  - [ ] types
  - [ ] methods
  - [ ] constructors
  - [ ] properties
  - [ ] fields
  - [ ] events

### Done Criteria

- [ ] XML summaries appear when available
- [ ] Fallback summaries are short and useful
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

- [ ] Implement `build` CLI command
- [ ] Accept `.sln` or `.csproj`
- [ ] Add `--out <path>`
- [ ] Add `--include-generated true|false`
- [ ] Add `--verbose`
- [ ] Write output files:
  - [ ] `code-index.meta.json`
  - [ ] `code-index.files.json`
  - [ ] `code-index.symbols.json`
  - [ ] `code-index.edges.json`
- [ ] Ensure deterministic ordering before writing
- [ ] Run validation before final write

### Done Criteria

- [ ] `build` works end-to-end
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