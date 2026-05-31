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

This project is licensed under the [MIT License](LICENSE).

## Project Layout

```text
/code-index
  code-index.sln
  /src
    /CodeIndex.Core
    /CodeIndex.Roslyn
    /CodeIndex.Cli
    /CodeIndex.Mcp
  /tests
    /CodeIndex.Core.Tests
    /CodeIndex.Roslyn.Tests
    /CodeIndex.Cli.Tests
  /scripts
    run-code-index-mcp.sh
  /samples
  /artifacts
  AGENTS.md
  global.json
```

## Checked-In Index Artifacts

This repository keeps a prebuilt JSON index under `artifacts/code-index` so
agents can query the project immediately after cloning. That directory is a
reference snapshot, not a substitute for rebuilding on your machine.

- Use `artifacts/code-index` for CLI examples in this README and for
  `AGENTS.md` workflows in this repository.
- After you change code, rebuild the index you query against so symbols,
  references, and excerpts stay accurate.
- MCP clients should build into `.code-index` at the workspace root (or pass an
  explicit `indexDirectory`) and rebuild after code changes.

To refresh the checked-in snapshot after substantive changes:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json` for the pinned version)
- a restoreable solution, project, or supported source directory to index

## Build

```bash
dotnet build code-index.sln
```

## Experimental MCP Server

The repository now includes a local stdio MCP server in `src/CodeIndex.Mcp`.
It uses the official .NET MCP SDK and calls the shared build/query services
directly instead of shelling out to the CLI.

Run it locally with:

```bash
dotnet run --project src/CodeIndex.Mcp
```

Current MCP tools:

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

`build_index` now defaults to a local `.code-index` artifact directory rooted at
the indexed workspace. For a solution or project input, that means a sibling
`.code-index` directory next to the `.sln` or `.csproj`. For a source directory
input, that means `<source-directory>/.code-index`.

### Recommended MCP Workflow

1. Start the MCP server from the workspace root.
2. Call `build_index` with the workspace path and let it write to the default `.code-index` directory.
3. After creating or changing code, rebuild the index before ending the agent session.
4. Call query tools with `indexDirectory` pointing at that explicit `.code-index` directory.

The server intentionally does not keep an implicit "last built index" cache for
queries. Agent clients should rebuild when they change the codebase and continue
to pass the explicit index path on every query so the target index stays
deterministic.

Example build request for this repository:

```json
{
  "path": "${workspaceFolder}/code-index.sln"
}
```

Example query request after building:

```json
{
  "query": "WorkspaceSymbolIndexBuilder",
  "indexDirectory": "${workspaceFolder}/.code-index",
  "limit": 10
}
```

### VS Code MCP Example

This repository includes `.vscode/mcp.json` that launches the MCP server through
`scripts/run-code-index-mcp.sh`. The wrapper finds `dotnet` on `PATH`, via
`DOTNET`, or common install locations (including mise shims) before running
`src/CodeIndex.Mcp`.

To set it up in another workspace, create `.vscode/mcp.json`:

```json
{
  "servers": {
    "codeIndex": {
      "type": "stdio",
      "command": "${workspaceFolder}/scripts/run-code-index-mcp.sh",
      "args": []
    }
  }
}
```

After adding the server, restart it from VS Code if needed, call `build_index`
for the workspace, rebuild it again after code changes, and point query tools at
`${workspaceFolder}/.code-index` explicitly.

### OpenCode MCP Example

This repository includes `opencode.json` using the same wrapper script. For
another workspace, add the server to `opencode.json` or `opencode.jsonc`:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "codeIndex": {
      "type": "local",
      "command": ["./scripts/run-code-index-mcp.sh"],
      "enabled": true,
      "timeout": 15000
    }
  }
}
```

Use the same workflow there: build first, rebuild after code changes, then query
against `./.code-index` for solution or project inputs rooted at the workspace.

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
