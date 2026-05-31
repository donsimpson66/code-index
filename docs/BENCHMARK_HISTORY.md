# Benchmark History

This file captures repository-scale benchmark snapshots over time so we can
track how the JSON artifact set changes as the project evolves.

## Notes

- `No index` means the workflow reads repository source directly without using generated artifacts for discovery.
- `JSON` means the workflow loads the current artifact directory and measures index-first retrieval against raw source reads.

## Current Direction

The production path is the JSON artifact set. Historical secondary-store comparisons were removed as part of the MCP preparation cleanup so this file now tracks JSON-backed benchmark runs only.
