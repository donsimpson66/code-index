# MCP Server Production Plan

## Objective

Prepare `code-index` for a production-ready local MCP server that can build and
query code indexes for arbitrary projects through a simple stdio integration.

The server should:

- build indexes from a solution, project, or supported source directory
- expose the current index-first query workflow as MCP tools
- stay local-first so editors and agents can use repository files directly
- keep responses compact and predictable for agent consumption

## Guiding Direction

The current repository has two storage paths:

- JSON artifacts
- SQLite storage and query support

For the MCP server direction, JSON artifacts are the better primary shape for
this project because they are already the canonical artifact set, are easier to
inspect and debug, and avoid carrying dual storage/query implementations into
the production server surface.

SQLite removal should happen first so the MCP server is built on one storage
model instead of preserving a second backend that adds maintenance cost without
clear speed or token-efficiency benefit for the intended agent workflow.

## Phase 1: Remove SQLite End To End

### Goal

Simplify the codebase to a single JSON-backed indexing and query model before
adding the MCP server.

### Tasks

1. Remove SQLite storage from `CodeIndex.Core`.
2. Remove SQLite query paths from `CodeIndex.Cli`.
3. Remove SQLite-specific tests, docs, and benchmark branches.
4. Remove SQLite package references and any related schema notes.
5. Reconfirm the repository still supports the full JSON-backed index-first flow.

### Concrete Work

- remove `SqliteCodeIndexStore` from [src/CodeIndex.Core/SqliteCodeIndexStore.cs](src/CodeIndex.Core/SqliteCodeIndexStore.cs)
- remove `--db-out` and `--db` command options from [src/CodeIndex.Cli/CliApplication.cs](src/CodeIndex.Cli/CliApplication.cs)
- remove dual-source guards such as `EnsureSingleStoreSource` and database-only code paths from [src/CodeIndex.Cli/CliApplication.cs](src/CodeIndex.Cli/CliApplication.cs)
- remove SQLite package usage from project files where it is no longer needed
- update README command examples to use JSON artifacts only
- remove or rewrite SQLite-specific plan and benchmark notes

### Acceptance Criteria

- no production code depends on SQLite packages
- the CLI builds indexes and answers queries using JSON artifacts only
- README and tests no longer describe or validate SQLite behavior
- one indexing path and one query path remain

## Phase 2: Refactor Multi-Language Support For Maintainability

### Goal

Reduce structural risk introduced by the directory-based multi-language support
before the MCP server depends on it as a production build path.

### Why This Matters

The current multi-language implementation is functionally useful, but too much
logic is concentrated in [src/CodeIndex.Core/MultiLanguageSourceIndexing.cs](src/CodeIndex.Core/MultiLanguageSourceIndexing.cs).
That creates avoidable maintenance risk for:

- adding new languages
- fixing one language without regressing others
- testing parser and usage-resolution behavior in isolation
- keeping CLI and MCP behavior aligned on one build/query service layer

### Tasks

1. Split multi-language code by concern instead of keeping it in one large file.
2. Split language-specific parsing and usage logic into separate units.
3. Centralize input-kind and strategy selection behind a single abstraction.
4. Add focused tests for multi-language indexing behavior.

### Concrete Work

- break up [src/CodeIndex.Core/MultiLanguageSourceIndexing.cs](src/CodeIndex.Core/MultiLanguageSourceIndexing.cs) into smaller files for:
  - language catalog and file discovery
  - symbol building
  - edge building
  - reference building
  - shared parsing helpers
  - per-language parsing and usage resolution
- extract language-specific logic for Java, Go, TypeScript, Python, and PHP so one language can evolve without editing a single monolithic parser file
- remove repeated `Directory.Exists(path)` branching from [src/CodeIndex.Cli/CliApplication.cs](src/CodeIndex.Cli/CliApplication.cs) by introducing one indexing-strategy selection boundary
- define cleaner internal seams between:
  - file discovery
  - symbol extraction
  - edge/reference derivation
  - cross-file usage resolution
- add dedicated multi-language tests under `tests/` instead of relying only on broad repository-scale coverage

### Acceptance Criteria

- multi-language support no longer depends on one monolithic implementation file
- language-specific behavior can be tested in isolation
- input-kind selection happens in one place instead of repeated branching
- the future MCP server can depend on the same multi-language build path with lower maintenance risk

## Phase 3: Extract A Reusable Query/Build Service Layer

### Goal

Move the reusable orchestration logic out of the CLI command wiring so the MCP
server can call the same code directly.

### Tasks

1. Introduce a service layer for build and query operations.
2. Keep CLI commands as a thin adapter over that service layer.
3. Standardize result shapes for symbol, reference, semantic, and excerpt tools.

### Concrete Work

- extract the shared build flow from [src/CodeIndex.Cli/CliApplication.cs](src/CodeIndex.Cli/CliApplication.cs)
- extract the shared query logic from [src/CodeIndex.Cli/CliApplication.cs](src/CodeIndex.Cli/CliApplication.cs)
- keep [src/CodeIndex.Core/CodeIndexReader.cs](src/CodeIndex.Core/CodeIndexReader.cs) as the JSON snapshot reader
- define a small service API for:
  - `BuildIndexAsync`
  - `FindSymbolsAsync`
  - `GetSymbolAsync`
  - `GetChildrenAsync`
  - `FindReferencesAsync`
  - `SemanticSearchAsync`
  - `GetCalleesAsync`
  - `GetCallersAsync`
  - `GetTestsAsync`
  - `GetTestTargetsAsync`
  - `GetExcerptAsync`

### Acceptance Criteria

- CLI behavior is unchanged from the user point of view
- build and query behavior can be invoked without going through `System.CommandLine`
- the MCP server can consume a single internal service boundary

## Phase 4: Add A Dedicated MCP Server Project

Status: complete

### Goal

Create a local stdio MCP server that editors and coding agents can launch as a
 child process.

### Tasks

1. Add a new server project, likely `src/CodeIndex.Mcp`.
2. Use the official .NET MCP SDK over stdio transport.
3. Register tool types that map directly to the shared service layer.

### Concrete Work

- add a new project to [code-index.sln](code-index.sln)
- reference the shared build/query libraries from the new MCP project
- configure stdio hosting with the official `ModelContextProtocol` package
- keep logging on stderr so stdout remains protocol-safe
- make the process single-purpose and local-first

### Acceptance Criteria

- the MCP server starts over stdio and advertises its tools
- the process can be launched locally by VS Code, OpenCode, or another MCP client
- no CLI shelling-out is required for normal MCP tool execution

## Phase 5: Define The MCP Tool Surface

Status: complete

### Goal

Expose a clean tool set that mirrors the index-first workflow without leaking
CLI-specific options into the protocol unnecessarily.

### Initial Tool Set

- `build_index`
- `find_symbol`
- `get_symbol`
- `get_children`
- `find_references`
- `semantic_search`
- `get_callees`
- `get_callers`
- `get_tests`
- `get_test_targets`
- `get_excerpt`

### Tool Design Rules

- build must be explicit, not automatic
- responses should return compact JSON data, not console-style text
- build should return paths, counts, elapsed time, and warnings
- query tools should support limits and relevant filters
- file paths should remain repository-relative when possible
- errors should be precise and actionable for agent clients

### Suggested `build_index` Inputs

- `path`
- `outputDirectory` optional, defaults to `.code-index` at the indexed workspace root
- `includeGenerated`
- `incrementalFromIndex`

### Suggested `build_index` Output

- `inputPath`
- `inputKind`
- `outputDirectory`
- `fileCount`
- `symbolCount`
- `edgeCount`
- `referenceCount`
- `embeddingCount`
- `elapsedMs`
- `warnings`

### Acceptance Criteria

- the tool set is stable and documented
- tool inputs align with the current repository workflow
- tool outputs are small enough for agents to chain effectively

## Phase 6: Add Workspace-Aware Defaults

Status: complete

### Goal

Make the MCP server easy to use against any project without requiring repetitive
manual path setup.

### Tasks

1. Support a default index directory convention such as `.code-index/`.
2. Support resolving paths relative to the client working directory.
3. Decide whether the server keeps any in-memory snapshot cache for the active index.

### Concrete Work

- define the default artifact location for `build_index`
- keep query tools on explicit `indexDirectory` inputs instead of reusing an implicit recent build target
- document the expected agent workflow: rebuild after code changes, then query with the explicit index path
- ensure the server never guesses across repositories in a way that risks reading the wrong project

### Acceptance Criteria

- a client can build an index for a workspace with minimal arguments
- query requests stay explicit and deterministic across agent sessions
- path resolution remains deterministic and safe

## Phase 7: Documentation And Client Setup

### Goal

Document how to run the MCP server from local editors and agent clients.

Status: complete

### Tasks

1. Add a README section for the MCP server.
2. Expand the README with the recommended local workflow: build first, then query.
3. Provide an example VS Code MCP configuration.
4. Provide an example OpenCode configuration.

### Acceptance Criteria

- a developer can configure the server without reading source
- the docs show one recommended local setup path
- the docs clearly explain how indexes are built and refreshed

## Phase 8: Production Hardening

### Goal

Make the server reliable enough for repeated daily editor usage.

Status: in progress

### Tasks

1. Add focused MCP/server integration tests.
2. Extend MCP integration coverage from tool listing/query flow into build flow and failure cases.
3. Harden cancellation, path validation, and error handling.
4. Confirm large-repository behavior is predictable.
5. Ensure logs and protocol traffic stay separated.

### Validation Focus

- list tools and run a real query against a live stdio server session
- build a repository index through MCP
- run each query tool against the built index
- verify invalid path, missing index, and bad symbol inputs return clean errors
- verify stderr logging does not corrupt stdout MCP responses

### Acceptance Criteria

- the server handles normal failures without crashing the session
- editor clients can reconnect and reuse the server reliably
- production behavior is covered by automated tests, not just manual checks

## Recommended Delivery Order

1. Remove SQLite completely.
2. Refactor the multi-language implementation into maintainable units.
3. Extract the shared build/query service layer.
4. Add the MCP server project and stdio hosting.
5. Implement `build_index` first.
6. Implement the read/query tools.
7. Add docs and client configuration examples.
8. Add integration tests and hardening.

## Non-Goals For The First MCP Release

- hosted multi-tenant API service
- remote/shared index storage
- automatic background reindexing
- HTTP transport
- editor-specific UI features beyond standard MCP integration

## Definition Of Done

The project is ready for production MCP usage when:

- SQLite has been fully removed
- one JSON-backed index model remains
- the multi-language implementation has been cleaned up into maintainable units
- the MCP server can build indexes itself
- the MCP server can answer the full index-first query workflow
- the CLI and MCP server both use the same internal build/query services
- setup instructions for local clients are documented and validated