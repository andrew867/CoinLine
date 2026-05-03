param(
  [Parameter(Mandatory=$true)][string]$File,
  [string]$Conn = $env:PGURL
)
if (-not $Conn) { $Conn = "postgresql://host:host@127.0.0.1:5432/hostplatform" }
Write-Host "Restoring $File"
& psql $Conn -v ON_ERROR_STOP=1 -f $File
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Done."
