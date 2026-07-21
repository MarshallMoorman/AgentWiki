#!/usr/bin/env bash
# Zip the entire repository, excluding bin/ and obj/ folders.
# Usage: ./zip-repo.sh [output.zip]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

REPO_NAME="$(basename "$ROOT")"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
OUT="${1:-${REPO_NAME}-${TIMESTAMP}.zip}"

# Resolve to an absolute path so we can exclude it while zipping from ROOT.
if [[ "$OUT" != /* ]]; then
  OUT="${ROOT}/${OUT}"
fi

# Avoid packing a previous run of this script's output sitting in the repo root.
OUT_BASENAME="$(basename "$OUT")"

echo "Creating archive: $OUT"
echo "Source:          $ROOT"
echo "Excluding:       **/bin/**, **/obj/**, ${OUT_BASENAME}"

# -r recursive, -q quieter progress (remove -q for verbose)
# Patterns exclude nested bin/obj anywhere in the tree.
zip -r "$OUT" . \
  -x '*/bin/*' \
  -x '*/obj/*' \
  -x 'bin/*' \
  -x 'obj/*' \
  -x 'artifacts/*' \
  -x '.git/*' \
  -x "./${OUT_BASENAME}" \
  -x "${OUT_BASENAME}"

echo "Done: $OUT ($(du -h "$OUT" | awk '{print $1}'))"
