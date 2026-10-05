param([Parameter(Mandatory)][pscredential]$CuentaServicio,[string]$PanelPath='C:\Proyectos\Vision_red\outputs\MonitorRed.Panel')
$ErrorActionPreference='Stop'
$panel=[IO.Path]::GetFullPath($PanelPath)
$exe=Join-Path $panel 'MonitorRed.Panel.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Copiar primero el contenido de la carpeta Vision del paquete a $panel en el servidor."}
if(Get-Service -Name Vision -ErrorAction SilentlyContinue){throw 'El servicio Vision ya existe. Revisar la instalación antes de reemplazarla.'}
foreach($name in 'topologia.json','email-settings.json','mobile-vpn-settings.json','monitor-settings.json'){
 if(!(Test-Path -LiteralPath (Join-Path $panel "datos\$name"))){throw "Falta datos\$name. Copiar Transferencia\datos y configurar la IP privada del servidor."}
}
$settings=Get-Content -LiteralPath (Join-Path $panel 'datos\email-settings.json') -Raw|ConvertFrom-Json
if($settings.automaticAlertsEnabled){throw 'La primera instalación debe validarse con automaticAlertsEnabled=false para evitar dos monitores enviando avisos simultáneamente.'}
$user=$CuentaServicio.UserName
# Validar las credenciales antes de modificar permisos. No iniciar hasta completar la configuración.
New-Service -Name Vision -DisplayName 'Vision - Monitoreo de red' -BinaryPathName ('"'+$exe+'"') -StartupType Manual -Credential $CuentaServicio -Description 'Monitoreo privado de equipos, correo y registro de eventos CSV.'|Out-Null
$installerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls.exe $panel /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' ($user+':RX') /T /Q
if($LASTEXITCODE -ne 0){throw 'No se pudieron establecer permisos de lectura del programa.'}
& icacls.exe (Join-Path $panel 'datos') /grant:r ($user+':M') /T /Q
if($LASTEXITCODE -ne 0){throw 'No se pudieron establecer permisos de escritura de los datos.'}
# Conservar el acceso explícito de quien instala para mantenimiento y reintentos.
& icacls.exe $panel /grant:r ('*'+$installerSid+':F') /T /Q
if($LASTEXITCODE -ne 0){throw 'No se pudo conservar el acceso de administración del instalador.'}
# Las entradas heredables se aplican a directorios; los archivos existentes ya tienen permisos directos.
$directories=@(Get-Item -LiteralPath $panel)+@(Get-ChildItem -LiteralPath $panel -Directory -Recurse -Force)
foreach($directory in $directories){
 $servicePermission=if($directory.FullName -eq (Join-Path $panel 'datos') -or $directory.FullName.StartsWith((Join-Path $panel 'datos')+'\',[StringComparison]::OrdinalIgnoreCase)){'M'}else{'RX'}
 & icacls.exe $directory.FullName /grant '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' ($user+':(OI)(CI)'+$servicePermission) ('*'+$installerSid+':(OI)(CI)F') /Q
 if($LASTEXITCODE -ne 0){throw "No se pudieron configurar permisos heredables en $($directory.FullName)."}
}
& sc.exe config Vision start= delayed-auto
if($LASTEXITCODE -ne 0){throw 'No se pudo configurar inicio diferido.'}
& sc.exe failure Vision reset= 86400 actions= restart/60000/restart/60000/restart/60000
if($LASTEXITCODE -ne 0){throw 'No se pudo configurar recuperación del servicio.'}
& sc.exe failureflag Vision 1
if($LASTEXITCODE -ne 0){throw 'No se pudo configurar recuperación por fallos.'}
Start-Service Vision
Get-Service Vision
Write-Host 'Abrir http://127.0.0.1:5080/ en el servidor para autorizar el correo y verificar. No activar avisos hasta detener la instancia de la PC.'
