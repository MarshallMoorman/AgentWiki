#!/usr/bin/env bash
# Pack AgentWiki as local dotnet tool(s) and install/update them globally.
#
#   ./scripts/pack-and-install-tool.sh           # CLI + Desktop (default)
#   ./scripts/pack-and-install-tool.sh Release
#   ./scripts/pack-and-install-tool.sh Release --cli-only
#   ./scripts/pack-and-install-tool.sh Release --desktop-only
#
# Not published to NuGet.org; local artifacts/ + Azure Artifacts later.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="Release"
PACK_CLI=1
PACK_DESKTOP=1

for arg in "$@"; do
  case "$arg" in
    Debug|Release)
      CONFIG="$arg"
      ;;
    --cli-only)
      PACK_CLI=1
      PACK_DESKTOP=0
      ;;
    --desktop-only)
      PACK_CLI=0
      PACK_DESKTOP=1
      ;;
    --help|-h)
      sed -n '2,12p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      exit 1
      ;;
  esac
done

ARTIFACTS_DIR="${ARTIFACTS_DIR:-$ROOT/artifacts}"
mkdir -p "$ARTIFACTS_DIR"

install_or_update_tool() {
  local package_id="$1"
  local tool_command="$2"

  # Prefer local artifacts; ignore failing private feeds (e.g. Azure Artifacts auth)
  # so offline pack/install keeps working without interactive NuGet login.
  local nuget_flags=(--global --add-source "$ARTIFACTS_DIR" --ignore-failed-sources)

  # Case-insensitive match: `dotnet tool list` prints package ids lowercase.
  local package_id_lc
  package_id_lc="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
  local already_installed=0
  if dotnet tool list -g | awk '{print tolower($1)}' | grep -qx "$package_id_lc"; then
    already_installed=1
  fi

  # Always reinstall from local artifacts so same-version rebuilds (e.g. 1.5.0 → 1.5.0)
  # actually pick up code changes. `dotnet tool update` skips when versions match.
  if [[ "$already_installed" -eq 1 ]]; then
    echo "==> Reinstalling global tool $package_id (local pack)"
    dotnet tool uninstall --global "$package_id"
  else
    echo "==> Installing global tool $package_id"
  fi
  dotnet tool install "${nuget_flags[@]}" "$package_id"

  if command -v "$tool_command" >/dev/null 2>&1; then
    echo "==> Verified: $tool_command"
    # Desktop has no --version CLI contract; only probe CLI.
    if [[ "$tool_command" == "agent-wiki" ]]; then
      "$tool_command" --version || true
    fi
  else
    echo "Warning: '$tool_command' not on PATH yet."
    echo "Ensure global tools path is configured, e.g.:"
    echo "  export PATH=\"\$PATH:\$HOME/.dotnet/tools\""
    return 1
  fi
}

if [[ "$PACK_CLI" -eq 1 ]]; then
  echo "==> Building and packing AgentWiki.Cli ($CONFIG) → agent-wiki"
  dotnet pack src/AgentWiki.Cli -c "$CONFIG" -o "$ARTIFACTS_DIR"
fi

if [[ "$PACK_DESKTOP" -eq 1 ]]; then
  echo "==> Building and packing AgentWiki.Desktop ($CONFIG) → agent-wiki-ui"
  # Desktop must build for pack (not --no-build) so Avalonia assets are included.
  dotnet pack src/AgentWiki.Desktop -c "$CONFIG" -o "$ARTIFACTS_DIR"
fi

echo "==> Package(s) in $ARTIFACTS_DIR"
ls -la "$ARTIFACTS_DIR"/*.nupkg 2>/dev/null || true

if [[ "$PACK_CLI" -eq 1 ]]; then
  install_or_update_tool "AgentWiki.Cli" "agent-wiki"
fi

if [[ "$PACK_DESKTOP" -eq 1 ]]; then
  install_or_update_tool "AgentWiki.Desktop" "agent-wiki-ui"
fi

echo "==> Done."
if [[ "$PACK_CLI" -eq 1 ]]; then
  echo "    CLI:     agent-wiki generate --repo-path . --force"
fi
if [[ "$PACK_DESKTOP" -eq 1 ]]; then
  echo "    Desktop: agent-wiki-ui"
fi
