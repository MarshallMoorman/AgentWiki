#!/usr/bin/env bash
# Bump AgentWiki version across Directory.Build.props and AgentWikiConstants.cs
# Usage:
#   bump-version.sh              # patch +0.0.1
#   bump-version.sh patch|minor|major
#   bump-version.sh 1.2.3
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Prefer walking up to Directory.Build.props (works from skill path or anywhere nearby)
ROOT="$SCRIPT_DIR"
while [[ "$ROOT" != "/" && ! -f "$ROOT/Directory.Build.props" ]]; do
  ROOT="$(dirname "$ROOT")"
done

PROPS="$ROOT/Directory.Build.props"
CONSTANTS="$ROOT/src/AgentWiki.Core/Constants/AgentWikiConstants.cs"

if [[ ! -f "$PROPS" ]]; then
  echo "error: Directory.Build.props not found (run inside AgentWiki repo)" >&2
  exit 1
fi

CURRENT="$(grep -oE '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>' "$PROPS" | head -1 | sed -E 's/<\/?Version>//g')"
if [[ -z "$CURRENT" ]]; then
  echo "error: could not parse current version from $PROPS" >&2
  exit 1
fi

IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT"
MODE="${1:-patch}"

case "$MODE" in
  patch) PATCH=$((PATCH + 1)); NEW="$MAJOR.$MINOR.$PATCH" ;;
  minor) MINOR=$((MINOR + 1)); PATCH=0; NEW="$MAJOR.$MINOR.$PATCH" ;;
  major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0; NEW="$MAJOR.$MINOR.$PATCH" ;;
  [0-9]*.[0-9]*.[0-9]*) NEW="$MODE" ;;
  *)
    echo "usage: $0 [patch|minor|major|X.Y.Z]" >&2
    exit 1
    ;;
esac

echo "Bumping version: $CURRENT → $NEW"

if [[ "$(uname)" == "Darwin" ]]; then
  sed_i() { sed -i '' "$@"; }
else
  sed_i() { sed -i "$@"; }
fi

sed_i -E "s#<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>#<Version>$NEW</Version>#" "$PROPS"
sed_i -E "s#<AssemblyVersion>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+</AssemblyVersion>#<AssemblyVersion>$NEW.0</AssemblyVersion>#" "$PROPS"
sed_i -E "s#<FileVersion>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+</FileVersion>#<FileVersion>$NEW.0</FileVersion>#" "$PROPS"
sed_i -E "s#<InformationalVersion>[^<]+</InformationalVersion>#<InformationalVersion>$NEW</InformationalVersion>#" "$PROPS"

if [[ -f "$CONSTANTS" ]]; then
  sed_i -E "s#public const string Version = \"[^\"]+\";#public const string Version = \"$NEW\";#" "$CONSTANTS"
fi

echo "Updated:"
echo "  $PROPS"
grep -E 'Version|AssemblyVersion|FileVersion|InformationalVersion' "$PROPS" | head -10
if [[ -f "$CONSTANTS" ]]; then
  echo "  $CONSTANTS"
  grep 'Version =' "$CONSTANTS" || true
fi
echo "New version: $NEW"
