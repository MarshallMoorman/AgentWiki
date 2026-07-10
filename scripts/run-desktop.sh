#!/usr/bin/env bash
# Run AgentWiki Desktop companion from source (macOS / Linux / Windows via bash).
#
# For the installed global tool (after pack):
#   ./scripts/pack-and-install-tool.sh --desktop-only
#   agent-wiki-ui
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "Building AgentWiki.Desktop…"
dotnet build src/AgentWiki.Desktop/AgentWiki.Desktop.csproj -c Debug --nologo -v q

echo "Starting AgentWiki Desktop from source (tool command: agent-wiki-ui)…"
exec dotnet run --project src/AgentWiki.Desktop/AgentWiki.Desktop.csproj -c Debug --no-build
