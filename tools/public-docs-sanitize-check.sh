#!/usr/bin/env bash
# Scans CoinLine public packaging paths for forbidden customer-facing phrases.
# Exit 1 if any banned term appears outside this script's allowlist header.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if command -v rg >/dev/null; then
  SEARCH=(rg -i)
else
  SEARCH=(grep -Ei)
fi

# Public documentation and setup paths.
PATHS=(
  "README.md"
  "CHANGELOG.md"
  "LICENSE.md"
  "release-checklist.md"
  ".env.example"
  "docs-site/docs"
  "docs"
  "docker"
  "examples"
  "deploy"
  "scripts"
)

BANNED_REGEX='(reverse[[:space:]]+engineering|decompiled?|(^|[^[:alnum:]_])IDA([^[:alnum:]_]|$)|Hex-Rays|firmware[[:space:]]+source|source[[:space:]]+code[[:space:]]+access|private[[:space:]]+firmware|legacy[[:space:]]+integration[[:space:]]+codename[[:space:]]+source|archaeology|lost[[:space:]]+source)'

FOUND=0
for p in "${PATHS[@]}"; do
  if [[ -e "$p" ]]; then
    while IFS= read -r -d '' f; do
      bn="$(basename "$f")"
      case "$bn" in
        public-docs-sanitize-check.sh|release-checklist.md) continue ;;
      esac
      if "${SEARCH[@]}" -q -e "$BANNED_REGEX" "$f"; then
        echo "FORBIDDEN term match in: $f"
        "${SEARCH[@]}" -n -e "$BANNED_REGEX" "$f"
        FOUND=1
      fi
    done < <(find "$p" -type f \( -name '*.md' -o -name '*.yml' -o -name '*.yaml' -o -name '*.json' -o -name '*.html' -o -name '*.tsx' -o -name '*.ts' -o -name '*.cs' -o -name '.env.example' \) -print0 2>/dev/null)
  fi
done

if [[ "$FOUND" -ne 0 ]]; then
  echo "public-docs-sanitize-check: FAILED"
  exit 1
fi

echo "public-docs-sanitize-check: OK"
exit 0
