$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $taskRoot 'work/tooling/activar-desarrollo.ps1')
Set-Location -LiteralPath $PSScriptRoot
Write-Host 'Abre http://127.0.0.1:5080 en tu navegador. Ctrl+C para detener.'
dotnet run --project $PSScriptRoot --configuration Release
