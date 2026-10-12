$ErrorActionPreference = 'Stop'
$application = Join-Path $PSScriptRoot 'GestionLibros\bin-maestro\Debug\net10.0-windows10.0.19041.0\win-x64\GestionLibros.exe'
if (Get-Process GestionLibros -ErrorAction SilentlyContinue) {
    Write-Host 'FREDI ya está abierto. Continúe en esa ventana; no se cerrará ni se recompilará.'
    exit 0
}
if (-not (Test-Path -LiteralPath $application)) {
    throw 'No existe una compilación. Ejecute Compilar-Fredi.ps1 y luego Iniciar-Fredi.ps1.'
}
$bundledRuntime = Join-Path $PSScriptRoot '.tools\dotnet'
if (Test-Path -LiteralPath (Join-Path $bundledRuntime 'dotnet.exe')) { $env:DOTNET_ROOT = $bundledRuntime }
# Visible window intentionally requested: this script launches the interactive accounting application.
Start-Process -FilePath $application -WorkingDirectory (Split-Path -Parent $application)




