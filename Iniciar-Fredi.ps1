$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$localDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $localDotnet)) { $localDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path -Parent $localDotnet
# Close a running copy so the build can overwrite bin-detalles instead of needing a new folder.
Get-Process GestionLibros -ErrorAction SilentlyContinue | Stop-Process -Force
& $localDotnet run --project 'GestionLibros\GestionLibros.csproj' '-p:TargetFrameworks=net10.0-windows10.0.19041.0' -f net10.0-windows10.0.19041.0 -p:BaseOutputPath=bin-detalles\
exit $LASTEXITCODE
