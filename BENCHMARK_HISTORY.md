# Benchmark History

This file tracks repository benchmark baselines over time.

Method notes:

- Query benchmarks come from `dotnet run --project src/CodeIndex.Cli -- benchmark ...`.
- `Model` records the agent/model expected to consume the benchmarked index-first workflow and interpret the token estimates.
- `No index` means the workflow reads repository source directly without using JSON artifacts or the SQLite index for discovery.
- Token counts are estimated as `ceil(utf8_bytes / 4)` and should be treated as a rough proxy, not a model-specific tokenizer count.
- The current baseline uses a fresh artifact build in `/tmp/code-index-bench` for consistency.
- This first entry records repository size, artifact size, and query-path speed. A repeatable full-build timing number was not recorded here because a timed rerun hit a Roslyn/MSBuild host disconnect even though the untimed build completed successfully.

## 2026-04-18

Revision: `91c837c`

Model: `GPT-5.4`

### Corpus Summary

| Date | Revision | Model | Files | Symbols | Edges | References | Embeddings | Source Bytes | Source Token Est. | Source Scan ms |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-04-18 | `91c837c` | `GPT-5.4` | 28 | 1,172 | 1,484 | 1,709 | 1,200 | 280,302 | 70,076 | 1 |

### No-Index Baseline

| Date | Model | Access Path | Bytes Read | Token Est. | Lines Read | Scan ms | Flow vs Source |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 2026-04-18 | `GPT-5.4` | Raw source only | 280,302 | 70,076 | 6,734 | 1 | 1.000 |

### Artifact Size Summary

| Date | Model | Store | Meta Bytes | Files Bytes | Symbols Bytes | Edges Bytes | References Bytes | Embeddings Bytes | Total Bytes | Token Est. | Size vs Source |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-04-18 | `GPT-5.4` | No index / raw source | - | - | - | - | - | - | 280,302 | 70,076 | 1.000x |
| 2026-04-18 | `GPT-5.4` | JSON artifacts | 278 | 10,434 | 1,070,483 | 457,192 | 912,592 | 1,466,338 | 3,917,317 | 979,330 | 13.975x |
| 2026-04-18 | `GPT-5.4` | SQLite DB | - | - | - | - | - | - | 4,374,528 | n/a | 15.606x |

### Query Benchmark Summary

| Date | Model | Store | Query | File | Index Load ms | Symbol Query ms | Excerpt ms | Flow ms | Flow Bytes | Flow Token Est. | Flow vs Source |
| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-04-18 | `GPT-5.4` | JSON | `WorkspaceSymbolIndexBuilder` | `src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs` | 54 | 6 | 2 | 8 | 29,776 | 7,444 | 0.106 |
| 2026-04-18 | `GPT-5.4` | JSON | `CliApplication` | `src/CodeIndex.Cli/CliApplication.cs` | 60 | 6 | 2 | 8 | 22,302 | 5,576 | 0.080 |
| 2026-04-18 | `GPT-5.4` | JSON | `SqliteCodeIndexStore` | `src/CodeIndex.Core/SqliteCodeIndexStore.cs` | 59 | 6 | 2 | 8 | 48,061 | 12,016 | 0.171 |
| 2026-04-18 | `GPT-5.4` | SQLite | `WorkspaceSymbolIndexBuilder` | `src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs` | 31 | 22 | 5 | 27 | 29,776 | 7,444 | 0.106 |
| 2026-04-18 | `GPT-5.4` | SQLite | `CliApplication` | `src/CodeIndex.Cli/CliApplication.cs` | 32 | 22 | 5 | 27 | 22,302 | 5,576 | 0.080 |
| 2026-04-18 | `GPT-5.4` | SQLite | `SqliteCodeIndexStore` | `src/CodeIndex.Core/SqliteCodeIndexStore.cs` | 36 | 24 | 7 | 31 | 48,061 | 12,016 | 0.171 |

### Query Payload Breakdown

| Date | Model | Store | Query | `find-symbol` Bytes | `find-symbol` Token Est. | `get-symbol` Bytes | `get-symbol` Token Est. | `get-children` Bytes | `get-children` Token Est. | Excerpt Bytes | Excerpt Token Est. |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-04-18 | `GPT-5.4` | JSON | `WorkspaceSymbolIndexBuilder` | 6,290 | 1,573 | 633 | 159 | 21,050 | 5,263 | 1,803 | 451 |
| 2026-04-18 | `GPT-5.4` | JSON | `CliApplication` | 4,162 | 1,041 | 541 | 136 | 16,083 | 4,021 | 1,516 | 379 |
| 2026-04-18 | `GPT-5.4` | JSON | `SqliteCodeIndexStore` | 4,557 | 1,140 | 581 | 146 | 41,569 | 10,393 | 1,354 | 339 |
| 2026-04-18 | `GPT-5.4` | SQLite | `WorkspaceSymbolIndexBuilder` | 6,290 | 1,573 | 633 | 159 | 21,050 | 5,263 | 1,803 | 451 |
| 2026-04-18 | `GPT-5.4` | SQLite | `CliApplication` | 4,162 | 1,041 | 541 | 136 | 16,083 | 4,021 | 1,516 | 379 |
| 2026-04-18 | `GPT-5.4` | SQLite | `SqliteCodeIndexStore` | 4,557 | 1,140 | 581 | 146 | 41,569 | 10,393 | 1,354 | 339 |

### Current Read

| Date | Model | Observation | Value |
| --- | --- | --- | --- |
| 2026-04-18 | `GPT-5.4` | No-index baseline for a discovery task is the full source corpus read | 280,302 B, about 70,076 tokens |
| 2026-04-18 | `GPT-5.4` | JSON artifact set is much larger than raw source | 3,917,317 B vs 280,302 B |
| 2026-04-18 | `GPT-5.4` | SQLite is larger than JSON artifact total for this corpus | 4,374,528 B vs 3,917,317 B |
| 2026-04-18 | `GPT-5.4` | Index-first query flows are still much smaller than full-source reads | 8.0% to 17.1% of raw source bytes |
| 2026-04-18 | `GPT-5.4` | JSON-backed query flow is faster than SQLite on these benchmark scenarios | 8 ms vs 27-31 ms total flow time |
