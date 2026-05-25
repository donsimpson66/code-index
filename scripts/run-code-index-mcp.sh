#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/CodeIndex.Mcp"

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
  echo "code-index MCP: dotnet not found. Install .NET 10 SDK or set DOTNET to the dotnet executable." >&2
  exit 1
}

exec "$dotnet_path" run --project "$project" "$@"
