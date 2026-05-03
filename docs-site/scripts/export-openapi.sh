#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
API_DLL="$ROOT/backend/src/HostPlatform.Api/bin/Debug/net9.0/HostPlatform.Api.dll"
OUT_DIR="$ROOT/docs-site/docs/api/openapi"
SWAGGER_CLI="$ROOT/backend/.tools/swagger"

mkdir -p "$OUT_DIR"
(cd "$ROOT/backend" && dotnet build src/HostPlatform.Api/HostPlatform.Api.csproj -v q)
export DOTNET_ENVIRONMENT=Testing
"$SWAGGER_CLI" tofile --output "$OUT_DIR/coinline.openapi.json" "$API_DLL" v1
"$SWAGGER_CLI" tofile --output "$OUT_DIR/coinline.openapi.yaml" --yaml "$API_DLL" v1
echo "OpenAPI written to $OUT_DIR"
