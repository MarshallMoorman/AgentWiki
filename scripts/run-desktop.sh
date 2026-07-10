#!/usr/bin/env bash
# Run AgentWiki Desktop companion from source (macOS / Linux / Windows via bash).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "Building AgentWiki.Desktop…"
dotnet build src/AgentWiki.Desktop/AgentWiki.Desktop.csproj -c Debug --nologo -v q

echo "Starting AgentWiki Desktop (CLI remains primary for CI)…"
exec dotnet run --project src/AgentWiki.Desktop/AgentWiki.Desktop.csproj -c Debug --no-build
