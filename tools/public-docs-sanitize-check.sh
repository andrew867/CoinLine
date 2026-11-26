#!/usr/bin/env bash
# Scans tracked public project material for disallowed reference language.
# Report file paths only, so a failed check does not repeat the offending text.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# The maintainer's policy stays outside the public tree. Each file contains
# one extended regular expression per line; empty lines are not permitted.
RULES="${1:-}"
CASED_RULES="${2:-}"
if [[ -z "$RULES" || $# -gt 2 ]]; then
  echo 'Usage: public-docs-sanitize-check.sh <private-rules-file> [private-case-sensitive-rules-file]'
  exit 2
fi

if command -v rg >/dev/null; then
  SEARCH=(rg -i)
  SEARCH_CASED=(rg)
else
  SEARCH=(grep -Ei)
  SEARCH_CASED=(grep -E)
fi

# Public documentation, implementation, tests and setup paths.
PATHS=(
  "README.md"
  "CHANGELOG.md"
  "LICENSE"
  "LICENSE.md"
  "release-checklist.md"
  ".env.example"
  "docs-site/docs"
  "docs"
  "docker"
  "examples"
  "deploy"
  "scripts"
  "tools"
  "backend/src"
  "backend/tests"
  "web/src"
  "web/e2e"
  "fixtures"
  ".github"
)

resolve_rules() {
  local file="$1" result=0
  if [[ ! -s "$file" ]] || grep -q '^[[:space:]]*$' "$file"; then
    echo 'Public reference language rules are missing or contain empty lines.' >&2
    return 2
  fi
  # Suppress diagnostics because some search tools quote malformed patterns.
  "${SEARCH_CASED[@]}" -q -f "$file" /dev/null 2>/dev/null || result=$?
  if (( result > 1 )); then
    echo 'Public reference language rules are invalid or unreadable.' >&2
    return 2
  fi
  printf '%s/%s\n' "$(cd "$(dirname "$file")" && pwd)" "$(basename "$file")"
}
RULES="$(resolve_rules "$RULES")"
if [[ -n "$CASED_RULES" ]]; then
  CASED_RULES="$(resolve_rules "$CASED_RULES")"
fi
cd "$ROOT"

FOUND=0
scan_file() {
  local f="$1"
  case "$(basename "$f")" in
    *.md|*.yml|*.yaml|*.json|*.html|*.tsx|*.ts|*.cs|*.py|*.sh|*.ps1|*.txt|.env.example|LICENSE) ;;
    *) return ;;
  esac
  [[ -f "$f" ]] || return 0
  local result=0 cased_result=1
  "${SEARCH[@]}" -q -f "$RULES" "$f" 2>/dev/null || result=$?
  if [[ -n "$CASED_RULES" ]]; then
    cased_result=0
    "${SEARCH_CASED[@]}" -q -f "$CASED_RULES" "$f" 2>/dev/null || cased_result=$?
  fi
  if (( result > 1 || cased_result > 1 )); then
    echo "Public reference language check could not read: $f"
    FOUND=1
  elif (( result == 0 || cased_result == 0 )); then
    echo "Public reference language check failed in: $f"
    FOUND=1
  fi
}

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  while IFS= read -r -d '' f; do
    scan_file "$f"
  done < <(git ls-files -z -- "${PATHS[@]}")
else
  for p in "${PATHS[@]}"; do
    if [[ -e "$p" ]]; then
      while IFS= read -r -d '' f; do
        scan_file "$f"
      done < <(find "$p" -type f -print0 2>/dev/null)
    fi
  done
fi

if [[ "$FOUND" -ne 0 ]]; then
  echo "public-docs-sanitize-check: FAILED"
  exit 1
fi

echo "public-docs-sanitize-check: OK"
exit 0
