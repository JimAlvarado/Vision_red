param(
    [Parameter(Mandatory = $true)][string]$Remitente,
    [Parameter(Mandatory = $true)][string]$DestinatarioPrueba,
    [string]$ClientId,
    [string]$TenantId = 'organizations',
    [string]$PanelPath
)
$ErrorActionPreference = 'Stop'
if (!$PanelPath) { $PanelPath = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) 'outputs\MonitorRed.Panel' }
$panel = [IO.Path]::GetFullPath($PanelPath)
$data = Join-Path $panel 'datos'
$settingsPath = Join-Path $data 'email-settings.json'
if (!(Test-Path -LiteralPath (Join-Path $panel 'MonitorRed.Panel.exe')) -or !(Test-Path -LiteralPath $settingsPath)) { throw 'Ruta del panel inválida.' }
$service = Get-Service -Name Vision -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') { throw 'Detener Vision antes de cambiar el buzón.' }
$running = @(Get-Process -Name MonitorRed.Panel -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $panel 'MonitorRed.Panel.exe') })
if ($running.Count) { throw 'Cerrar el proceso de este panel antes de cambiar el buzón.' }
foreach ($address in @($Remitente, $DestinatarioPrueba)) {
    try { $parsed = New-Object Net.Mail.MailAddress($address) } catch { throw 'Dirección de correo inválida.' }
    if ($parsed.Address -ne $address -or $address -match '[\r\n]' -or $address.Length -gt 254) { throw 'Usa únicamente la dirección de correo.' }
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if (!$ClientId) { $ClientId = $settings.clientId }
$parsedGuid = [guid]::Empty
if (![guid]::TryParse($ClientId, [ref]$parsedGuid)) { throw 'Id. de cliente inválido.' }
if ($TenantId -ne 'organizations' -and ![guid]::TryParse($TenantId, [ref]$parsedGuid)) { throw 'Organización inválida.' }
$ownerPath = Join-Path $data 'owner-notifications.json'
$owner = if (Test-Path -LiteralPath $ownerPath) { Get-Content -LiteralPath $ownerPath -Raw | ConvertFrom-Json } else { $null }
$changed = $settings.senderAddress -ine $Remitente -or $settings.authMode -ne 'organizational-device-code' -or
    $settings.clientId -ne $ClientId -or $settings.tenantId -ne $TenantId -or $settings.testRecipient -ine $DestinatarioPrueba
$backup = Join-Path $data ('respaldo-correo-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($backup)
foreach ($name in @('email-settings.json','owner-notifications.json','email-session.dpapi','email-test-receipt.json')) {
    $path = Join-Path $data $name
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $backup $name) }
}
function Save-Json($Value, [string]$Path) {
    [IO.File]::WriteAllText($Path + '.tmp', ($Value | ConvertTo-Json -Depth 40), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath ($Path + '.tmp') -Destination $Path -Force
}
if ($owner) { $owner | Add-Member NoteProperty enabled $false -Force; Save-Json $owner $ownerPath }
foreach ($entry in @{
    provider='microsoft-graph'; authMode='organizational-device-code'; clientId=$ClientId; tenantId=$TenantId;
    senderAddress=$Remitente; testRecipient=$DestinatarioPrueba; automaticAlertsEnabled=$false
}.GetEnumerator()) { $settings | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value -Force }
if ($changed) {
    # Backups are private. Archive the previous identity and receipt before installing the new settings.
    foreach ($name in @('email-session.dpapi','email-test-receipt.json')) {
        $path = Join-Path $data $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    $settings | Add-Member NoteProperty testRequestId ([guid]::NewGuid().ToString()) -Force
}
Save-Json $settings $settingsPath
Write-Host 'Correo institucional configurado. Destinatarios e historial conservados; ambos canales automáticos desactivados.'
Write-Host 'Iniciar Vision con su cuenta habitual de Windows, autorizar el nuevo buzón en Configuración → Correo y enviar una sola prueba.'
Write-Host ('Respaldo privado: ' + $backup)
