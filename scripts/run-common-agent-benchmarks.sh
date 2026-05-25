#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cli_project="$root/src/CodeIndex.Cli"
solution="$root/code-index.sln"
index_dir="$root/artifacts/code-index"

resolve_dotnet() {
  if [[ -n "${DOTNET:-}" && -x "$DOTNET" ]]; then
    printf '%s\n' "$DOTNET"
    return 0
  fi

  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return 0
  fi

  local candidate
  for candidate in \
    "$HOME/.local/share/mise/shims/dotnet" \
    "/opt/homebrew/bin/dotnet" \
    "/usr/local/share/dotnet/dotnet" \
    "/usr/share/dotnet/dotnet"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

dotnet_path="$(resolve_dotnet)" || {
  echo "code-index benchmarks: dotnet not found. Install .NET 10 SDK or set DOTNET." >&2
  exit 1
}

run_cli() {
  "$dotnet_path" run --project "$cli_project" -- "$@"
}

# Mirrors CommonAgentBenchmarkSearches in tests/CodeIndex.Cli.Tests/CliSmokeTests.cs
# symbol_query|selected_qualified_name|file|start|end|label
readonly -a scenarios=(
  "WorkspaceSymbolIndexBuilder|CodeIndex.Roslyn.WorkspaceSymbolIndexBuilder|src/CodeIndex.Roslyn/WorkspaceSymbolIndexBuilder.cs|1|20|WorkspaceSymbolIndexBuilder"
  "CliApplication|CodeIndex.Cli.CliApplication|src/CodeIndex.Cli/CliApplication.cs|1|20|CliApplication"
)

ensure_index() {
  if run_cli build "$solution" --out "$index_dir" >/dev/null 2>&1; then
    echo "Built index at $index_dir"
    return 0
  fi

  if [[ ! -f "$index_dir/code-index.meta.json" ]]; then
    echo "Failed to build index and no existing artifacts found at $index_dir" >&2
    exit 1
  fi

  echo "Index build failed; using existing artifacts at $index_dir" >&2
  echo "Patching sourceRoot to $root for benchmark source reads ..." >&2

  python3 - "$index_dir/code-index.meta.json" "$root" <<'PY'
import json
import sys

meta_path, source_root = sys.argv[1:3]
with open(meta_path, encoding="utf-8") as handle:
    meta = json.load(handle)

meta["sourceRoot"] = source_root

with open(meta_path, "w", encoding="utf-8") as handle:
    json.dump(meta, handle, indent=2)
    handle.write("\n")
PY
}

echo "Ensuring index at $index_dir ..."
ensure_index

results_dir="$(mktemp -d "${TMPDIR:-/tmp}/code-index-benchmarks.XXXXXX")"
trap 'rm -rf "$results_dir"' EXIT

echo
echo "Running CommonAgentBenchmarkSearches scenarios ..."
echo

for scenario in "${scenarios[@]}"; do
  IFS='|' read -r symbol_query _selected_qualified_name file start end label <<<"$scenario"

  output_file="$results_dir/${label}.json"
  echo "==> $label"
  echo "    symbol=$symbol_query file=$file lines=$start-$end"

  run_cli benchmark \
    --index "$index_dir" \
    --symbol "$symbol_query" \
    --file "$file" \
    --start "$start" \
    --end "$end" >"$output_file"

  python3 - "$output_file" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, encoding="utf-8") as handle:
    data = json.load(handle)

raw_tokens = data["rawSource"]["totalEstimatedTokens"]
index_first_tokens = data["indexFirstFlowEstimatedTokens"]
ratio = data["indexFirstVsFullSourceRatio"]
saved = raw_tokens - index_first_tokens
saved_pct = 0 if raw_tokens == 0 else round(100 * saved / raw_tokens, 1)

print(f"    full source est. tokens: {raw_tokens:,}")
print(f"    index-first est. tokens: {index_first_tokens:,}")
print(f"    estimated savings:     {saved:,} tokens ({saved_pct}%)")
print(f"    index-first ratio:       {ratio}")
print()
PY
done

python3 - "$results_dir" <<'PY'
import json
import sys
from pathlib import Path

results_dir = Path(sys.argv[1])
rows = []

for path in sorted(results_dir.glob("*.json")):
    with path.open(encoding="utf-8") as handle:
        data = json.load(handle)

    label = path.stem
    raw_tokens = data["rawSource"]["totalEstimatedTokens"]
    index_first_tokens = data["indexFirstFlowEstimatedTokens"]
    ratio = data["indexFirstVsFullSourceRatio"]
    saved = raw_tokens - index_first_tokens
    saved_pct = 0 if raw_tokens == 0 else round(100 * saved / raw_tokens, 1)

    rows.append(
        {
            "label": label,
            "symbol": data["symbolQuery"]["query"],
            "file": data["excerptQuery"]["file"],
            "lines": f'{data["excerptQuery"]["start"]}-{data["excerptQuery"]["end"]}',
            "raw_tokens": raw_tokens,
            "index_first_tokens": index_first_tokens,
            "saved_tokens": saved,
            "saved_pct": saved_pct,
            "ratio": ratio,
        }
    )

print("Summary")
print("-------")
print(
    f"{'Scenario':<30} {'Full source':>12} {'Index-first':>12} {'Saved':>10} {'Saved %':>8} {'Ratio':>8}"
)
print("-" * 86)

for row in rows:
    print(
        f"{row['label']:<30} "
        f"{row['raw_tokens']:>12,} "
        f"{row['index_first_tokens']:>12,} "
        f"{row['saved_tokens']:>10,} "
        f"{row['saved_pct']:>7.1f}% "
        f"{row['ratio']:>8.3f}"
    )

if rows:
    avg_saved_pct = sum(row["saved_pct"] for row in rows) / len(rows)
    print("-" * 86)
    print(f"Average estimated savings across scenarios: {avg_saved_pct:.1f}%")
PY

echo
echo "Raw JSON results:"
for scenario in "${scenarios[@]}"; do
  IFS='|' read -r _ _ _ _ _ label <<<"$scenario"
  echo "  $results_dir/${label}.json"
done
