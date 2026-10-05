# Ejecutar con dot-sourcing: . ./work/tooling/activar-desarrollo.ps1
$env:DOTNET_ROOT = Join-Path $PSScriptRoot 'dotnet'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
