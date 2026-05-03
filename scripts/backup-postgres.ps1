param(
  [string]$OutFile = "hostplatform-backup-$(Get-Date -Format 'yyyyMMddTHHmmssZ').sql",
  [string]$Conn = $env:PGURL
)
if (-not $Conn) { $Conn = "postgresql://host:host@127.0.0.1:5432/hostplatform" }
Write-Host "Writing $OutFile"
& pg_dump $Conn --no-owner --format=p --file=$OutFile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Done."
