# Agent Instructions

## Index First

When working in this repository, consult the checked-in index in
`artifacts/code-index` before doing broad source searches.

Preferred workflow:

1. Rebuild the repository index if it may be stale.

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
```

2. Use symbol queries before raw code search.

```bash
dotnet run --project src/CodeIndex.Cli -- find-symbol "WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-symbol "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index
dotnet run --project src/CodeIndex.Cli -- get-children "CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder" --index ./artifacts/code-index --kind method --sort declaration
```

3. Use exact excerpts for follow-up source reads.

```bash
dotnet run --project src/CodeIndex.Cli -- get-excerpt "src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs" --index ./artifacts/code-index --start 1 --end 80
```

## Search Order

- Prefer `find-symbol` when you know a type, member, or namespace name.
- Prefer `get-symbol` to confirm the owning file, signature, summary, and parent.
- Prefer `get-children` before opening an entire type file.
- Prefer `get-excerpt` for the smallest necessary source slice.
- Use broad text search only when the index does not answer the question.