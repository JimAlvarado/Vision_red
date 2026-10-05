param([string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$panelPath = Join-Path $projectRoot 'outputs\MonitorRed.Panel'
$staging = Join-Path $PSScriptRoot ('publicacion-' + [guid]::NewGuid().ToString('N'))
if (!$DotnetPath) {
    $bundled = Join-Path $PSScriptRoot 'dotnet\dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $bundled) { $bundled } else { 'dotnet' }
}
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'cli-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $projectRoot
try {
    & $DotnetPath publish (Join-Path $panelPath 'MonitorRed.Panel.csproj') -c Release -r win-x64 --self-contained true -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'La publicacion fallo; no se reemplazo el programa.' }
    $files = @(Get-ChildItem -LiteralPath $staging -Recurse -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($staging.Length + 1)
        if ($relative -match '^(datos|logs)[\\/]' -or $file.Extension -in '.dpapi','.pfx','.p12','.pem','.key') {
            throw "La publicacion contiene un archivo privado: $relative"
        }
    }
    if (!(Test-Path -LiteralPath (Join-Path $staging 'MonitorRed.Panel.exe'))) { throw 'Falta el ejecutable publicado.' }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($staging.Length + 1)
        $destination = [IO.Path]::GetFullPath((Join-Path $panelPath $relative))
        if (!$destination.StartsWith($panelPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta de destino invalida.' }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    Write-Host 'Publicacion preparada en outputs\MonitorRed.Panel. Revisar los cambios antes de subirlos.'
    Write-Host ('Copia de compilacion conservada en: ' + $staging)
} finally { Pop-Location }
