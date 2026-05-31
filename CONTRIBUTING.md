# Contributing to CodeIndex

Thank you for helping improve CodeIndex. This project targets index-first agent
workflows: small JSON artifacts, compact query output, and predictable CLI/MCP
behavior.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) matching `global.json`
- Git

Optional for MCP/editor work:

- VS Code or Cursor with MCP support, or [OpenCode](https://opencode.ai/)

## Getting started

```bash
git clone https://github.com/donsimpson66/code-index.git
cd code-index
dotnet build code-index.sln
dotnet test code-index.sln
```

CI runs the same build and test steps in Release configuration on every push and
pull request to `main`.

## Project structure

| Path | Purpose |
|------|---------|
| `src/CodeIndex.Core` | Models, validation, multi-language indexing |
| `src/CodeIndex.Roslyn` | MSBuild/Roslyn workspace indexing |
| `src/CodeIndex.Cli` | Command-line interface |
| `src/CodeIndex.Mcp` | Stdio MCP server |
| `tests/*` | Unit and integration tests |
| `artifacts/code-index` | Checked-in JSON index for this repo (see below) |
| `scripts/run-code-index-mcp.sh` | Portable MCP launcher for editors |

Agent-oriented usage for this repository is documented in [AGENTS.md](AGENTS.md).

## Refreshing the checked-in index

When you change symbols, references, or file structure in this repository, update
the canonical snapshot under `artifacts/code-index`:

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
```

Include those JSON updates in the same PR when your change affects indexed
structure or query behavior tests rely on.

Local MCP builds write to `.code-index/` at the workspace root. That directory is
gitignored; do not commit it.

## Running the MCP server locally

```bash
./scripts/run-code-index-mcp.sh
```

Or use the committed `.vscode/mcp.json` / `opencode.json` configurations.

## Pull requests

1. Branch from `main`.
2. Keep changes focused; match existing naming and patterns in nearby code.
3. Run `dotnet test code-index.sln` before opening a PR.
4. Update README or AGENTS.md when behavior or setup changes.
5. Refresh `artifacts/code-index` when indexing output for this repo changes.

## Code style

- C# with nullable reference types enabled
- Prefer extending existing services (`CodeIndexBuildService`, `CodeIndexQueryService`) rather than duplicating CLI logic
- Avoid drive-by refactors unrelated to the issue you are solving

## Reporting issues

Use [GitHub Issues](https://github.com/donsimpson66/code-index/issues) for bugs
and feature requests. Include:

- .NET SDK version (`dotnet --version`)
- Input type (`.sln`, `.csproj`, or directory path)
- Command or MCP tool invoked
- Expected vs actual behavior

## License

By contributing, you agree that your contributions will be licensed under the
[MIT License](LICENSE).
