# Agent Instructions

## Index First

When working in this repository, consult the checked-in index in
`artifacts/code-index` before searching through project files directly.
Use the code-index commands to search the project code first, then fall back
to file or text search only when the index does not answer the question.

Preferred workflow:

1. Rebuild the repository index if it may be stale.

```bash
dotnet run --project src/CodeIndex.Cli -- build ./code-index.sln --out ./artifacts/code-index
```

2. Use code-index symbol queries before searching project files.

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

- Search project code with the code-index commands before using `rg`, editor file search, or broad file reads.
- Prefer `find-symbol` when you know a type, member, or namespace name.
- Prefer `get-symbol` to confirm the owning file, signature, summary, and parent.
- Prefer `get-children` before opening an entire type file.
- Prefer `get-excerpt` for the smallest necessary source slice.
- Use broad text search only when the index does not answer the question.