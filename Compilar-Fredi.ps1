$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (Get-Process GestionLibros -ErrorAction SilentlyContinue) {
    throw 'FREDI está abierto. Guarde o descarte los cambios y cierre la aplicación antes de compilar. No se terminará el proceso.'
}
$localDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $localDotnet)) { $localDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path -Parent $localDotnet
& $localDotnet build 'GestionLibros\GestionLibros.csproj' '-p:TargetFrameworks=net10.0-windows10.0.19041.0' -f net10.0-windows10.0.19041.0 '-p:BaseOutputPath=bin-maestro\'
exit $LASTEXITCODE




