param([Parameter(Mandatory)][string]$CorreoAutor,
    [string]$PanelPath = 'C:\Proyectos\Vision_red\outputs\MonitorRed.Panel')
$ErrorActionPreference = 'Stop'
$address = $CorreoAutor.Trim()
try { $parsed = New-Object System.Net.Mail.MailAddress($address) } catch { throw 'Correo del autor inválido.' }
if ($parsed.Address -ne $address -or $address -match '[\r\n]' -or $address.Length -gt 254) { throw 'Correo del autor inválido.' }
$panel = [IO.Path]::GetFullPath($PanelPath)
$data = Join-Path $panel 'datos'
if (!(Test-Path -LiteralPath (Join-Path $panel 'MonitorRed.Panel.exe')) -or !(Test-Path -LiteralPath $data)) { throw 'Ruta del panel inválida.' }
$service = Get-Service -Name Vision -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') { throw 'Detener el servicio Vision antes de configurar los avisos al autor.' }
$settingsPath = Join-Path $data 'email-settings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$recipients = @($settings.recipients)
if (!$settings.PSObject.Properties['recipients']) { $recipients = @($settings.testRecipient) }
if ($recipients -notcontains $address) { $recipients += $address }
if ($recipients.Count -gt 50) { throw 'La lista ya contiene 50 destinatarios. Reservar un lugar para el autor.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $data "email-settings-antes-autor-$stamp.json")
$settings | Add-Member -NotePropertyName recipients -NotePropertyValue $recipients -Force
$settings | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath ($settingsPath + '.tmp') -Encoding UTF8
Move-Item -LiteralPath ($settingsPath + '.tmp') -Destination $settingsPath -Force
$ownerPath = Join-Path $data 'owner-notifications.json'
if (Test-Path -LiteralPath $ownerPath) { Copy-Item -LiteralPath $ownerPath -Destination (Join-Path $data "owner-notifications-antes-$stamp.json") }
@{ recipientAddress = $address; enabled = $true } | ConvertTo-Json | Set-Content -LiteralPath ($ownerPath + '.tmp') -Encoding UTF8
Move-Item -LiteralPath ($ownerPath + '.tmp') -Destination $ownerPath -Force
Write-Host 'Avisos al autor configurados y correo del autor incluido en las alertas de red. Iniciar Vision y verificar en Configuración → Correo.'
