$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$ApiDll = Join-Path $Root "backend/src/HostPlatform.Api/bin/Debug/net9.0/HostPlatform.Api.dll"
$OutDir = Join-Path $Root "docs-site/docs/api/openapi"
$SwaggerCli = Join-Path $Root "backend/.tools/swagger"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Push-Location (Join-Path $Root "backend")
try {
  dotnet build src/HostPlatform.Api/HostPlatform.Api.csproj -v q
  $env:DOTNET_ENVIRONMENT = "Testing"
  & $SwaggerCli tofile --output (Join-Path $OutDir "coinline.openapi.json") $ApiDll v1
  & $SwaggerCli tofile --output (Join-Path $OutDir "coinline.openapi.yaml") --yaml $ApiDll v1
  Write-Host "OpenAPI written to $OutDir"
}
finally {
  Pop-Location
}
