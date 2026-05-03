#!/usr/bin/env bash
# Scans CoinLine public packaging paths for forbidden customer-facing phrases.
# Exit 1 if any banned term appears outside this script's allowlist header.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

# Paths to scan (public customer surface inside coinline/)
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

BANNED_REGEX='(?i)(reverse\s+engineering|decompiled?|\bIDA\b|Hex-Rays|firmware\s+source|source\s+code\s+access|private\s+firmware|MTR212\s+source|archaeology|lost\s+source|private\s+firmware\s+repo)'

FOUND=0
for p in "${PATHS[@]}"; do
  if [[ -e "$p" ]]; then
    while IFS= read -r -d '' f; do
      bn="$(basename "$f")"
      case "$bn" in
        public-docs-sanitize-check.sh|release-checklist.md) continue ;;
      esac
      if rg -l --no-messages -P "$BANNED_REGEX" "$f" 2>/dev/null | grep -q .; then
        echo "FORBIDDEN term match in: $f"
        rg -n -P "$BANNED_REGEX" "$f" || true
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
