#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TEST_DIR="$(mktemp -d)"
mkdir -p "$TEST_DIR/tools" "$TEST_DIR/docs" "$TEST_DIR/backend/src"
cp "$ROOT/tools/public-docs-sanitize-check.sh" "$TEST_DIR/tools/"
git -C "$TEST_DIR" init -q
printf '%s\n' 'Known record lengths and separate terminal acknowledgement evidence.' > "$TEST_DIR/docs/evidence.md"
printf '%s\n' 'var internalMarker = 1;' > "$TEST_DIR/backend/src/Fixture.cs"
printf '%s\n' 'example private marker' > "$TEST_DIR/rules.txt"
printf '%s\n' 'INTERNALMARKER' > "$TEST_DIR/cased-rules.txt"
git -C "$TEST_DIR" add docs/evidence.md backend/src/Fixture.cs
bash "$TEST_DIR/tools/public-docs-sanitize-check.sh" "$TEST_DIR/rules.txt" "$TEST_DIR/cased-rules.txt" >/dev/null

# A harmless test rule stands in for the maintainer's private review policy.
disallowed='Example Private Marker'
printf '%s\n' "$disallowed" > "$TEST_DIR/docs/evidence.md"
if report="$(bash "$TEST_DIR/tools/public-docs-sanitize-check.sh" "$TEST_DIR/rules.txt" "$TEST_DIR/cased-rules.txt" 2>&1)"; then
  echo 'Public reference language regression: expected a rejection.'
  exit 1
fi
[[ "$report" == *'docs/evidence.md'* ]] && [[ "$report" != *"$disallowed"* ]] || {
  echo 'Public reference language regression: expected a path-only diagnostic.'
  exit 1
}
printf '%s\n' 'Safe text.' > "$TEST_DIR/docs/evidence.md"
printf '%s\n' 'INTERNALMARKER' > "$TEST_DIR/backend/src/Fixture.cs"
if report="$(bash "$TEST_DIR/tools/public-docs-sanitize-check.sh" "$TEST_DIR/rules.txt" "$TEST_DIR/cased-rules.txt" 2>&1)"; then
  echo 'Public reference language regression: expected a case-sensitive rejection.'
  exit 1
fi
[[ "$report" == *'backend/src/Fixture.cs'* ]] && [[ "$report" != *'INTERNALMARKER'* ]] || {
  echo 'Public reference language regression: expected a path-only case-sensitive diagnostic.'
  exit 1
}

printf '%s\n' '[' > "$TEST_DIR/invalid-rules.txt"
if bash "$TEST_DIR/tools/public-docs-sanitize-check.sh" "$TEST_DIR/invalid-rules.txt" >/dev/null 2>&1; then
  echo 'Public reference language regression: invalid rules must fail.'
  exit 1
fi
if bash "$TEST_DIR/tools/public-docs-sanitize-check.sh" >/dev/null 2>&1; then
  echo 'Public reference language regression: missing rules must fail.'
  exit 1
fi
echo 'Public reference language regression passed.'
