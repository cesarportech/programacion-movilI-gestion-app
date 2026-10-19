$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (Get-Process GestionLibros -ErrorAction SilentlyContinue) {
    throw 'FREDI está abierto. Guarde o descarte los cambios y cierre la aplicación antes de publicar. No se terminará el proceso.'
}
$localDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $localDotnet)) { $localDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path -Parent $localDotnet
# Paquete para equipos de clientes: Release, win-x64, con .NET y Windows App SDK incluidos.
$output = Join-Path $PSScriptRoot 'publicar\FREDI'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
& $localDotnet publish 'GestionLibros\GestionLibros.csproj' '-p:TargetFrameworks=net10.0-windows10.0.19041.0' -f net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained true '-p:WindowsAppSDKSelfContained=true' '-p:BaseOutputPath=bin-publicar\' -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($required in 'GestionLibros.exe', 'coreclr.dll', 'hostfxr.dll', 'Microsoft.WindowsAppRuntime.Bootstrap.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $output $required))) { throw "La publicación no contiene $required." }
}
Write-Host "Paquete listo en $output. Entregue la carpeta completa; GestionLibros.exe no funciona por sí solo."
exit 0
