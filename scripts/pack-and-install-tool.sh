#!/usr/bin/env bash
# Pack AgentWiki as a local dotnet tool and install/update it globally.
# Mirrors the README "Install as a local dotnet tool" flow.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="${1:-Release}"
ARTIFACTS_DIR="${ARTIFACTS_DIR:-$ROOT/artifacts}"
PACKAGE_ID="AgentWiki.Cli"
TOOL_COMMAND="agent-wiki"

echo "==> Building and packing $PACKAGE_ID ($CONFIG)"
mkdir -p "$ARTIFACTS_DIR"
dotnet pack src/AgentWiki.Cli -c "$CONFIG" -o "$ARTIFACTS_DIR"

echo "==> Package(s) in $ARTIFACTS_DIR"
ls -la "$ARTIFACTS_DIR"/*.nupkg 2>/dev/null || true

if dotnet tool list -g | awk '{print $1}' | grep -qx "$PACKAGE_ID"; then
  echo "==> Updating global tool $PACKAGE_ID"
  dotnet tool update --global --add-source "$ARTIFACTS_DIR" "$PACKAGE_ID"
else
  echo "==> Installing global tool $PACKAGE_ID"
  dotnet tool install --global --add-source "$ARTIFACTS_DIR" "$PACKAGE_ID"
fi

echo "==> Verifying install"
if command -v "$TOOL_COMMAND" >/dev/null 2>&1; then
  "$TOOL_COMMAND" --version
  "$TOOL_COMMAND" --help | head -20
else
  echo "Warning: '$TOOL_COMMAND' not on PATH yet."
  echo "Ensure global tools path is configured, e.g.:"
  echo "  export PATH=\"\$PATH:\$HOME/.dotnet/tools\""
  exit 1
fi

echo "==> Done. Use: $TOOL_COMMAND generate --repo-path . --force"
