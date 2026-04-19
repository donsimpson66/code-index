# TASKS - C# AI Code Index Tool

## Goal

Build a .NET 10 CLI tool that uses Roslyn for `.sln` and `.csproj` inputs and
lightweight source parsing for supported directory inputs to generate compact
JSON code index artifacts.

The tool should help AI agents:

- find symbols quickly
- resolve references and caller/callee relationships
- query semantic matches without loading full files
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
- [x] incremental indexing
- [ ] API server
- [x] multi-language support
- [x] local variable indexing
- [x] vector embeddings
- [x] test linking heuristics

## Current Post-MVP Status

Implemented beyond the original MVP:

- [x] references index and `find-references`
- [x] caller/callee analysis via `calls` edges plus `get-callers` and `get-callees`
- [x] test linking heuristics via `get-tests` and `get-test-targets`
- [x] semantic embeddings and `semantic-search`
- [x] incremental indexing from prior JSON baselines
- [x] benchmark coverage for JSON size, speed, and estimated token usage
- [x] directory-based multi-language indexing for Java, Go, TypeScript, Python, and PHP

Still open:

- [ ] API server

---

## Production MCP Server Track

### Phase 1 - Remove Secondary Store

### Tasks

- [x] Remove the retired secondary store implementation and production code
- [x] Remove secondary-store output support from the CLI build flow
- [x] Remove secondary-store query paths from CLI commands
- [x] Remove secondary-store benchmark branches and output
- [x] Remove secondary-store package references from project files
- [x] Remove or rewrite storage-specific documentation and plan files
- [x] Keep JSON-backed build and query behavior working end to end

### Done Criteria

- [x] No production path depends on a secondary store backend
- [x] One JSON-backed storage/query model remains
- [x] README and tests no longer describe retired secondary-store usage

---

### Phase 2 - Multi-Language Refactor Cleanup

### Tasks

- [x] Split `MultiLanguageSourceIndexing.cs` by concern
- [x] Move per-language parser logic into separate files
- [x] Move per-language usage/reference logic into separate files
- [x] Extract shared multi-language parsing helpers
- [x] Centralize input-kind/indexing-strategy selection in one place
- [x] Reduce repeated `Directory.Exists(path)` branching in `CliRuntime`
- [x] Add dedicated multi-language tests for Java, Go, TypeScript, Python, and PHP

### Done Criteria

- [x] Multi-language support no longer depends on one monolithic implementation file
- [x] One language can change without editing one giant parser/usage file
- [x] Multi-language behavior has focused automated coverage

---

### Phase 3 - Shared Build And Query Service Layer

### Tasks

- [x] Extract build orchestration out of CLI command handlers
- [x] Extract query orchestration out of CLI command handlers
- [x] Define a reusable internal service API for build and query operations
- [x] Keep the CLI as a thin adapter over the shared service layer
- [x] Standardize result shapes used by both CLI and MCP surfaces

### Done Criteria

- [x] Build/query logic can run without `System.CommandLine`
- [x] CLI and MCP can share one service boundary

---

### Phase 4 - MCP Server Project

### Tasks

- [ ] Add `src/CodeIndex.Mcp`
- [ ] Add the project to `code-index.sln`
- [ ] Add official .NET MCP SDK dependencies
- [ ] Configure stdio transport
- [ ] Keep logs on stderr only
- [ ] Wire the MCP server to the shared build/query service layer

### Done Criteria

- [ ] MCP server starts over stdio
- [ ] MCP tools can be listed by a client
- [ ] No CLI shell-out is needed for tool execution

---

### Phase 5 - MCP Tool Surface

### Tasks

- [ ] Implement `build_index`
- [ ] Implement `find_symbol`
- [ ] Implement `get_symbol`
- [ ] Implement `get_children`
- [ ] Implement `find_references`
- [ ] Implement `semantic_search`
- [ ] Implement `get_callees`
- [ ] Implement `get_callers`
- [ ] Implement `get_tests`
- [ ] Implement `get_test_targets`
- [ ] Implement `get_excerpt`
- [ ] Keep tool responses compact and agent-friendly

### Done Criteria

- [ ] MCP tool surface covers the index-first workflow
- [ ] MCP server can build indexes, not just query them

---

### Phase 6 - Workspace Defaults, Docs, And Hardening

### Tasks

- [ ] Define a default local artifact directory such as `.code-index/`
- [ ] Support safe workspace-relative path resolution
- [ ] Add README documentation for MCP setup and workflow
- [ ] Add VS Code MCP configuration example
- [ ] Add OpenCode MCP configuration example
- [ ] Add MCP integration tests for build and query flows
- [ ] Harden cancellation, path validation, and error handling

### Done Criteria

- [ ] Local client setup is documented
- [ ] MCP build and query flows are covered by automated tests
- [ ] The server is reliable enough for repeated editor use

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