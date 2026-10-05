param([Parameter(Mandatory)][string]$IpServidor,[Parameter(Mandatory)][string]$SubredClientes,[string]$PanelPath='C:\Proyectos\Vision_red\outputs\MonitorRed.Panel')
$ErrorActionPreference='Stop'
$panel=[IO.Path]::GetFullPath($PanelPath)
$exe=Join-Path $panel 'MonitorRed.Panel.exe'
function IsPrivate([Net.IPAddress]$ip){$b=$ip.GetAddressBytes();return $b.Length -eq 4 -and ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))}
$address=[Net.IPAddress]::Parse($IpServidor)
$parts=$SubredClientes.Split('/')
if(!(IsPrivate $address) -or $parts.Length -ne 2 -or !(IsPrivate ([Net.IPAddress]::Parse($parts[0]))) -or [int]$parts[1] -lt 16 -or [int]$parts[1] -gt 32){throw 'Usar una IP privada y la subred IPv4 privada autorizada de los clientes.'}
if(!(Get-NetIPAddress -AddressFamily IPv4|Where-Object IPAddress -eq $IpServidor)){throw 'La IP no está asignada a este servidor.'}
if(!(Test-Path -LiteralPath $exe)){throw 'Falta el ejecutable de Vision en el servidor.'}
if(Get-NetFirewallRule -Name VisionServidorVPN -ErrorAction SilentlyContinue){throw 'Ya existe la regla VisionServidorVPN. Revisar su alcance antes de modificarla.'}
@{vpnAddress=$IpServidor;allowedSubnet=$SubredClientes}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $panel 'datos\mobile-vpn-settings.json') -Encoding utf8
New-NetFirewallRule -Name VisionServidorVPN -DisplayName 'Vision - Consulta privada por VPN' -Direction Inbound -Action Allow -Protocol TCP -LocalAddress $IpServidor -LocalPort 5081 -RemoteAddress $SubredClientes -Program $exe -Profile Any|Out-Null
Write-Host "Consulta privada: http://${IpServidor}:5081/ . No se habilitó el editor en la red ni se abrió un puerto público."
