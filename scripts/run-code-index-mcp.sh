#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/CodeIndex.Mcp"

dotnet_works() {
  local candidate="$1"
  [[ -n "$candidate" && -x "$candidate" ]] || return 1
  (cd "$root" && "$candidate" --version >/dev/null 2>&1)
}

resolve_dotnet() {
  if [[ -n "${DOTNET:-}" ]] && dotnet_works "$DOTNET"; then
    printf '%s\n' "$DOTNET"
    return 0
  fi

  local candidate path_dotnet
  path_dotnet="$(command -v dotnet 2>/dev/null || true)"
  for candidate in \
    "$HOME/.dotnet/dotnet" \
    "$path_dotnet" \
    "$HOME/.local/share/mise/shims/dotnet" \
    "/opt/homebrew/bin/dotnet" \
    "/usr/local/share/dotnet/dotnet" \
    "/usr/share/dotnet/dotnet"; do
    if dotnet_works "$candidate"; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

dotnet_path="$(resolve_dotnet)" || {
  echo "code-index MCP: no .NET SDK matching global.json was found. Install the pinned SDK or set DOTNET to that executable." >&2
  exit 1
}

cd "$root"
exec "$dotnet_path" run --project "$project" "$@"
