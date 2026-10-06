# Configura las redes que pueden abrir la consulta móvil (VPN, VLAN de Arzyz) y su regla de firewall.
# Ejecutar como administrador con la lista COMPLETA de redes; reemplaza la lista anterior. Reiniciar Vision para aplicar.
# Ejemplo: .\Configurar-Acceso-Privado.ps1 -IpServidor <IP del servidor> -SubredClientes <red VPN>/24,<VLAN>/24
param([Parameter(Mandatory)][string]$IpServidor,[Parameter(Mandatory)][string[]]$SubredClientes,[string]$PanelPath='C:\Proyectos\Vision_red\outputs\MonitorRed.Panel')
$ErrorActionPreference='Stop'
$panel=[IO.Path]::GetFullPath($PanelPath)
$exe=Join-Path $panel 'MonitorRed.Panel.exe'
function IsPrivate([Net.IPAddress]$ip){$b=$ip.GetAddressBytes();return $b.Length -eq 4 -and ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))}
$address=[Net.IPAddress]::Parse($IpServidor)
if(!(IsPrivate $address)){throw 'Usar la IP privada del servidor.'}
# Acepta la lista como varios argumentos o separada por comas (powershell -File entrega un solo texto).
$subnets=@($SubredClientes|ForEach-Object{$_ -split ','}|ForEach-Object{$_.Trim()}|Where-Object{$_}|Select-Object -Unique)
if($subnets.Count -lt 1 -or $subnets.Count -gt 20){throw 'Indicar entre 1 y 20 subredes autorizadas.'}
foreach($subnet in $subnets){
    $parts=$subnet.Split('/');$prefix=0;$net=$null
    if($parts.Length -ne 2 -or ![int]::TryParse($parts[1],[ref]$prefix) -or $prefix -lt 16 -or $prefix -gt 32 -or ![Net.IPAddress]::TryParse($parts[0],[ref]$net) -or !(IsPrivate $net)){
        throw "Subred inválida: $subnet. Usar subredes IPv4 privadas con prefijo /16 a /32."}
}
if(!(Get-NetIPAddress -AddressFamily IPv4|Where-Object IPAddress -eq $IpServidor)){throw 'La IP no está asignada a este servidor.'}
if(!(Test-Path -LiteralPath $exe)){throw 'Falta el ejecutable de Vision en el servidor.'}
# Firewall primero: si falla, la configuración de Vision queda como estaba.
$rule=Get-NetFirewallRule -Name VisionServidorVPN -ErrorAction SilentlyContinue
if($rule){
    $port=$rule|Get-NetFirewallPortFilter;$app=$rule|Get-NetFirewallApplicationFilter
    if($port.LocalPort -ne '5081' -or $app.Program -ne $exe){throw 'La regla VisionServidorVPN no corresponde a Vision en el puerto 5081. Revisarla antes de modificarla.'}
    Set-NetFirewallRule -Name VisionServidorVPN -LocalAddress $IpServidor -RemoteAddress $subnets
}else{
    New-NetFirewallRule -Name VisionServidorVPN -DisplayName 'Vision - Consulta privada por VPN' -Direction Inbound -Action Allow -Protocol TCP -LocalAddress $IpServidor -LocalPort 5081 -RemoteAddress $subnets -Program $exe -Profile Any|Out-Null
}
$settingsPath=Join-Path $panel 'datos\mobile-vpn-settings.json'
$json=(@{vpnAddress=$IpServidor;allowedSubnets=$subnets}|ConvertTo-Json)
[IO.File]::WriteAllText($settingsPath+'.tmp',$json,(New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath ($settingsPath+'.tmp') -Destination $settingsPath -Force
Write-Host "Consulta privada: http://${IpServidor}:5081/ . Redes autorizadas: $($subnets -join ', '). Reiniciar Vision para aplicar. No se habilitó el editor en la red ni se abrió un puerto público."
