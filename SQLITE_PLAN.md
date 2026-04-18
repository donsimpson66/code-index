# SQLite Backend Plan

## Objective

Add a SQLite-backed index store so the CLI can run symbol and excerpt queries
against a database instead of requiring agents to consume large JSON artifacts.

## Scope

1. Add a SQLite writer for the existing snapshot model.
2. Extend `build` so it can emit a database file.
3. Extend query commands so they can read from `--db <path>`.
4. Extend `benchmark` so it can compare raw source volume with database storage
   and database-backed query flows.
5. Add CLI tests that build and query a database from the current repository.

## Schema

- `meta`
  - `schema_version`
  - `tool_version`
  - `repo_name`
  - `generated_at_utc`
  - `source_root`
  - `input_path`
  - `input_kind`
- `files`
  - `id` primary key
  - `path` unique
  - `project_name`
  - `language`
  - `hash`
  - `summary`
- `symbols`
  - `id` primary key
  - `name`
  - `qualified_name`
  - `kind`
  - `file_id`
  - `start_line`
  - `start_column`
  - `end_line`
  - `end_column`
  - `signature`
  - `summary`
  - `parent_id`
  - `accessibility`
  - `is_static`
  - `is_abstract`
  - `is_virtual`
  - `is_override`
- `edges`
  - `type`
  - `from_id`
  - `to_id`

## Query Surface

- `build --db-out <path>` writes a SQLite database alongside or instead of JSON.
- `find-symbol --db <path>` queries `symbols` directly.
- `get-symbol --db <path>` resolves one symbol directly.
- `get-children --db <path>` joins `edges` and `symbols` directly.
- `get-excerpt --db <path>` resolves the file path from `files` and reads source
  from disk using `meta.source_root`.
- `benchmark --db <path>` compares database size and database-backed query flow
  against raw source volume.

## Validation

- Build a database from `code-index.sln`.
- Run `find-symbol`, `get-symbol`, `get-children`, and `get-excerpt` against it.
- Run `benchmark --db` against the repository database.
- Keep existing JSON-backed behavior working.

## Current Notes

- `benchmark --db` now reads `meta`, `files`, and symbol and edge counts
  directly for whole-project metrics instead of loading a full snapshot first.
- Repository build outputs under `bin/` and `obj/` are ignored at the root so
  SQLite package runtime files do not overwhelm diffs.