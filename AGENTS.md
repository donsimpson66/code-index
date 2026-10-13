# Agent Instructions

## Index First

Consult the checked-in index in `artifacts/code-index` before searching project
files directly. Prefer MCP `codeIndex` tools when available; otherwise use the
`scripts/code-index` wrapper (or a locally installed `code-index` tool). Fall
back to `rg` or file reads only when the index cannot answer.

Preferred workflow:

1. Rebuild only when the index may be stale (after structural edits). Prefer
   `--if-changed` so unchanged trees skip rewriting.

```bash
./scripts/code-index build ./code-index.sln --out ./artifacts/code-index --if-changed
```

2. Use compact symbol queries first (`find-symbol`, `outline`, `get-children`).
   Defaults return list projections, not full records.

```bash
./scripts/code-index find-symbol "WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index --limit 10
./scripts/code-index outline "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index --kind method
./scripts/code-index get-symbol "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index
```

3. Fetch the smallest excerpt still needed.

```bash
./scripts/code-index get-excerpt "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --index ./artifacts/code-index --start 88 --end 120
```

## Search Order

- Prefer `find-symbol` / `outline` when you know a type, member, or namespace name.
- Prefer `get-symbol` for one full record after a list hit.
- Prefer `get-children` / `outline` before opening an entire type file.
- Prefer `get-excerpt` for the smallest necessary source slice.
- Cap list queries with `--limit` (defaults are already modest).
- Use `--format text` when a denser line-oriented view helps.
- Use `--fields` to keep only the columns you need.
- Use broad text search only when the index does not answer the question.

## Token Budget Notes

- Locals are excluded from builds by default; pass `--include-locals` only if needed.
- List commands return compact projections; use `get-symbol` or `--full` for full records.
- Embeddings are optional at build time (`--skip-embeddings`); skip them when semantic search is not needed.
