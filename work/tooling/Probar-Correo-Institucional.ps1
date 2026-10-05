param(
    [Parameter(Mandatory = $true)][string]$Remitente,
    [Parameter(Mandatory = $true)][string]$Destinatario,
    [string]$ClientId,
    [string]$TenantId = 'organizations',
    [switch]$PrepararSolo
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path $PSScriptRoot 'prueba-correo-institucional'
$receiptPath = Join-Path $testRoot 'resultado.json'
foreach ($address in @($Remitente, $Destinatario)) {
    try { $parsed = New-Object Net.Mail.MailAddress($address) } catch { throw 'Dirección de correo inválida.' }
    if ($parsed.Address -ne $address -or $address -match '[\r\n]') { throw 'Usa únicamente la dirección de correo, sin nombre.' }
}
if (!$ClientId) {
    $configPath = Join-Path $projectRoot 'outputs\MonitorRed.Panel\datos\email-settings.json'
    if (!(Test-Path -LiteralPath $configPath)) { throw 'TI debe proporcionar el Id. de cliente de una aplicación autorizada para Microsoft 365.' }
    $ClientId = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).clientId
}
$parsedGuid = [guid]::Empty
if (![guid]::TryParse($ClientId, [ref]$parsedGuid)) { throw 'Id. de cliente inválido.' }
if ($TenantId -ne 'organizations' -and ![guid]::TryParse($TenantId, [ref]$parsedGuid)) { throw 'Organización inválida: usa organizations o el GUID de TI.' }
if (Test-Path -LiteralPath $receiptPath) {
    $previous = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($previous.state -in @('sending', 'accepted', 'unknown')) {
        throw 'Ya existe una prueba aceptada o sin confirmación. Revisar Elementos enviados y recepción antes de otra prueba; no borrar el resultado para reintentar a ciegas.'
    }
}
if ($PrepararSolo) {
    Write-Host 'Preparación correcta. No se contactó Microsoft, no se envió correo y no se modificó Vision.'
    return
}

function Save-Receipt([string]$State, [string]$ErrorText) {
    [void][IO.Directory]::CreateDirectory($testRoot)
    @{ requestId = $script:requestId; state = $State; sender = $Remitente; recipient = $Destinatario;
       updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); error = $ErrorText } |
        ConvertTo-Json | Set-Content -LiteralPath ($receiptPath + '.tmp') -Encoding UTF8
    Move-Item -LiteralPath ($receiptPath + '.tmp') -Destination $receiptPath -Force
}
function Read-OAuthError($Record) {
    try { return ($Record.ErrorDetails.Message | ConvertFrom-Json).error } catch { return '' }
}

$authority = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0"
try {
    $device = Invoke-RestMethod -Method Post -Uri "$authority/devicecode" -TimeoutSec 30 -Body @{
        client_id = $ClientId; scope = 'https://graph.microsoft.com/Mail.Send openid profile'
    }
} catch {
    throw 'Microsoft no permitió iniciar la autorización institucional. TI debe revisar el Id. de cliente, los tipos de cuenta y los flujos de cliente público. No se envió correo.'
}
Write-Host ('Abre ' + $device.verification_uri + ' e introduce el código: ' + $device.user_code)
Write-Host ('Inicia sesión con ' + $Remitente + '. La contraseña se introduce solamente en Microsoft.')
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([int]$device.expires_in)
$interval = [Math]::Max(5, [int]$device.interval)
$token = $null
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    Start-Sleep -Seconds $interval
    try {
        $token = Invoke-RestMethod -Method Post -Uri "$authority/token" -TimeoutSec 30 -Body @{
            client_id = $ClientId; grant_type = 'urn:ietf:params:oauth:grant-type:device_code'; device_code = $device.device_code
        }
        break
    } catch {
        $errorCode = Read-OAuthError $_
        if ($errorCode -eq 'authorization_pending') { continue }
        if ($errorCode -eq 'slow_down') { $interval += 5; continue }
        throw 'No se completó la autorización. Revisa la cuenta y el consentimiento de TI. No se envió correo.'
    }
}
if (!$token -or !$token.access_token -or !$token.id_token) { throw 'La autorización venció o no confirmó la identidad. No se envió correo.' }
try {
    # Identity comes directly from the HTTPS token response, never from a caller-provided token.
    $part = $token.id_token.Split('.')[1].Replace('-', '+').Replace('_', '/')
    $part = $part.PadRight($part.Length + ((4 - $part.Length % 4) % 4), '=')
    $identity = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($part)) | ConvertFrom-Json
    if ($identity.preferred_username -ine $Remitente) { throw 'Cuenta distinta.' }
} catch { $token = $null; throw 'La cuenta autorizada no coincide con el remitente. No se envió correo.' }

$script:requestId = [guid]::NewGuid().ToString()
$body = @{
    message = @{ subject = '[VISION-APODACA] Prueba de correo institucional';
        body = @{ contentType = 'Text'; content = "Prueba única de conexión de Vision al correo institucional. No indica una caída de equipos.`r`nReferencia: $script:requestId" };
        toRecipients = @(@{ emailAddress = @{ address = $Destinatario } }) };
    saveToSentItems = $true
} | ConvertTo-Json -Depth 8
Save-Receipt 'sending' $null
try {
    # Exactly one send request. Ambiguous outcomes are never automatically retried.
    $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri 'https://graph.microsoft.com/v1.0/me/sendMail' -TimeoutSec 35 `
        -Headers @{ Authorization = 'Bearer ' + $token.access_token; 'client-request-id' = $script:requestId } `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    if ([int]$response.StatusCode -eq 202) {
        Save-Receipt 'accepted' $null
        Write-Host 'Microsoft aceptó la prueba. Confirma la recepción en el destinatario y revisa Elementos enviados. No se activaron alertas automáticas.'
    } else { Save-Receipt 'unknown' 'Respuesta inesperada; revisar Elementos enviados antes de repetir.'; throw 'Envío sin confirmación.' }
} catch {
    $statusCode = 0
    if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
    if ($statusCode -ge 400 -and $statusCode -lt 500) {
        Save-Receipt 'failed' ("Microsoft rechazó la prueba: HTTP $statusCode. No se reintentó.")
        throw "Microsoft rechazó la prueba: HTTP $statusCode. TI debe revisar el permiso Mail.Send, la cuenta y sus restricciones."
    }
    Save-Receipt 'unknown' 'No se confirmó el envío; revisar Elementos enviados antes de repetir.'
    throw 'No se confirmó el envío. No repetir hasta revisar Elementos enviados y recepción.'
} finally { $token = $null; $device = $null }
