$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$ApiDll = Join-Path $Root "backend/src/HostPlatform.Api/bin/Debug/net9.0/HostPlatform.Api.dll"
$OutDir = Join-Path $Root "docs-site/docs/api/openapi"
$SwaggerCli = Join-Path $Root "backend/.tools/swagger"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$Saved = @{}
foreach ($Name in @("DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT", "COINLINE_OPERATOR_TOKEN_KEY", "COINLINE_NCC_LISTEN", "COINLINE_SIP_MODEM")) {
  $Saved[$Name] = [Environment]::GetEnvironmentVariable($Name, "Process")
}
Push-Location (Join-Path $Root "backend")
try {
  dotnet build src/HostPlatform.Api/HostPlatform.Api.csproj -v q
  $env:DOTNET_ENVIRONMENT = "Testing"
  $env:ASPNETCORE_ENVIRONMENT = "Testing"
  $Bytes = New-Object byte[] 32
  $Generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  try { $Generator.GetBytes($Bytes) } finally { $Generator.Dispose() }
  $env:COINLINE_OPERATOR_TOKEN_KEY = [BitConverter]::ToString($Bytes).Replace("-", "")
  $env:COINLINE_NCC_LISTEN = "0"
  $env:COINLINE_SIP_MODEM = "0"
  & $SwaggerCli tofile --output (Join-Path $OutDir "coinline.openapi.json") $ApiDll v1
  & $SwaggerCli tofile --output (Join-Path $OutDir "coinline.openapi.yaml") --yaml $ApiDll v1
  Write-Host "OpenAPI written to $OutDir"
}
finally {
  foreach ($Name in $Saved.Keys) { [Environment]::SetEnvironmentVariable($Name, $Saved[$Name], "Process") }
  Pop-Location
}
