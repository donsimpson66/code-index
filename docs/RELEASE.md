# Releases

## v0.1.0 (initial public release)

**Requirements:** .NET 10 SDK (`global.json`), MIT license.

**Includes:**

- CLI: `build`, `inspect`, symbol/reference/semantic/call/test queries, `benchmark`
- JSON artifacts: `meta`, `files`, `symbols`, `edges`, `references`, `embeddings`
- Roslyn indexing for `.sln` / `.csproj`
- Directory-based multi-language indexing (Java, Go, TypeScript, Python, PHP)
- Experimental stdio MCP server with `build_index` and query tools
- Portable MCP launcher: `scripts/run-code-index-mcp.sh`

**Not included:**

- HTTP API server
- NuGet/global-tool distribution (clone and `dotnet run` for now)

### Tagging this release

After confirming CI is green on `main`:

```bash
git tag -a v0.1.0 -m "Initial public release: CLI, JSON index, experimental MCP."
git push origin v0.1.0
```

On GitHub, create a release from the tag and paste the bullets above into the
release description. Suggested topics: `dotnet`, `roslyn`, `mcp`, `code-index`,
`ai-agents`, `csharp`.
