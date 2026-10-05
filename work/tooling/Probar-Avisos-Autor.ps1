param([Parameter(Mandatory)][string]$TestRoot)
$ErrorActionPreference = 'Stop'
$testRootPath = [IO.Path]::GetFullPath($TestRoot)
$panel = Join-Path $testRootPath 'outputs\MonitorRed.Panel'
$data = Join-Path $panel 'datos'
$exe = Join-Path $panel 'MonitorRed.Panel.exe'
if (Test-Path -LiteralPath (Join-Path $data 'email-session.dpapi')) { throw 'No probar con una sesión de correo autorizada.' }
if (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object LocalPort -in 5080,5081).Count) { throw 'Puertos ocupados.' }
$emailPath = Join-Path $data 'email-settings.json'
$outboxPath = Join-Path $data 'alertas-pendientes.json'
$ownerPath = Join-Path $data 'owner-notifications.json'
$mobileSessionsPath = Join-Path $data 'mobile-sessions.dpapi'
$originalMobileSessions = if (Test-Path -LiteralPath $mobileSessionsPath) { [IO.File]::ReadAllBytes($mobileSessionsPath) } else { $null }
$originalEmail = [IO.File]::ReadAllText($emailPath)
$settings = $originalEmail | ConvertFrom-Json
if ($settings.automaticAlertsEnabled) { throw 'No probar con alertas de red habilitadas.' }
$originalOutbox = if (Test-Path -LiteralPath $outboxPath) { [IO.File]::ReadAllText($outboxPath) } else { '[]' }
$originalOwner = if (Test-Path -LiteralPath $ownerPath) { [IO.File]::ReadAllText($ownerPath) } else { $null }
$p = $null
function Start-Test {
    $process = Start-Process -FilePath $exe -WorkingDirectory $panel -WindowStyle Hidden -PassThru
    for ($i=0; $i -lt 40; $i++) {
        if ($process.HasExited) { throw 'Falló el arranque.' }
        try { [void](Invoke-RestMethod 'http://127.0.0.1:5080/api/email/status' -TimeoutSec 2); return $process }
        catch { Start-Sleep -Milliseconds 250 }
    }
    Stop-Process -Id $process.Id
    throw 'La aplicación no respondió.'
}
function Get-OwnerQueue {
    $all = Invoke-RestMethod 'http://127.0.0.1:5080/api/notifications'
    return @($all | Where-Object kind -in 'recipient_added','mobile_login')
}
function Update-Recipients($addresses) {
    [void](Invoke-RestMethod 'http://127.0.0.1:5080/api/email/recipients' -Method PUT -Headers @{'X-Topology-Editor'='1'} -ContentType 'application/json' -Body (@{recipients=@($addresses)} | ConvertTo-Json))
}
try {
    if (Test-Path -LiteralPath $mobileSessionsPath) { Remove-Item -LiteralPath $mobileSessionsPath }
    $settings.recipients = @('base@example.invalid')
    $settings | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $emailPath
    @{recipientAddress='autor@example.invalid';enabled=$true} | ConvertTo-Json | Set-Content -LiteralPath $ownerPath
    '[]' | Set-Content -LiteralPath $outboxPath
    $p = Start-Test
    $status = Invoke-RestMethod 'http://127.0.0.1:5080/api/email/status'
    if (!$status.ownerNotificationsEnabled -or $status.ownerNotificationAddress -ne 'autor@example.invalid') { throw 'Configuración del autor incorrecta.' }
    Update-Recipients @('base@example.invalid','nuevo@example.invalid')
    $queue = @(Get-OwnerQueue)
    if ($queue.Count -ne 1 -or $queue[0].kind -ne 'recipient_added' -or !$queue[0].message.Contains('nuevo@example.invalid')) { throw 'Falta aviso de nuevo destinatario.' }
    Update-Recipients @('BASE@example.invalid','NUEVO@example.invalid')
    Update-Recipients @('base@example.invalid')
    $current = Invoke-RestMethod 'http://127.0.0.1:5080/api/email/status'
    if ($current.recipients -notcontains 'autor@example.invalid') { throw 'Se pudo quitar al autor de las alertas de red.' }
    if (@(Get-OwnerQueue).Count -ne 1) { throw 'Avisos repetidos al guardar o quitar.' }
    $access = Invoke-RestMethod 'http://127.0.0.1:5080/api/mobile/access'
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $headers = @{'X-Vision-Mobile'='1';'User-Agent'='Mozilla/5.0 (Android Test) <script>prueba</script>'}
    $body = @{code=$access.codes[0]} | ConvertTo-Json
    [void](Invoke-RestMethod 'http://127.0.0.1:5081/api/mobile/login' -Method POST -Headers $headers -ContentType 'application/json' -Body $body -WebSession $session -SkipHeaderValidation)
    if (@(Get-OwnerQueue).Count -ne 2) { throw 'Falta aviso de acceso.' }
    [void](Invoke-RestMethod 'http://127.0.0.1:5081/api/mobile/login' -Method POST -Headers $headers -ContentType 'application/json' -Body $body -WebSession $session -SkipHeaderValidation)
    [void](Invoke-RestMethod 'http://127.0.0.1:5081/api/status' -WebSession $session -SkipHeaderValidation)
    if (@(Get-OwnerQueue).Count -ne 2) { throw 'La sesión activa generó avisos duplicados.' }
    $rejected = Invoke-WebRequest 'http://127.0.0.1:5081/api/mobile/login' -Method POST -Headers @{'X-Vision-Mobile'='1'} -ContentType 'application/json' -Body '{"code":"incorrecto"}' -SkipHttpErrorCheck
    if ($rejected.StatusCode -ne 401 -or @(Get-OwnerQueue).Count -ne 2) { throw 'Un código incorrecto generó un aviso.' }
    $queue = @(Get-OwnerQueue)
    foreach ($notice in $queue) {
        if ($notice.targetRecipients.Count -ne 1 -or $notice.targetRecipients[0] -ne 'autor@example.invalid') { throw 'Aviso enviado a destinatarios generales.' }
        foreach ($code in $access.codes) { if ($notice.message.Contains($code)) { throw 'Código expuesto en correo.' } }
        if ($notice.message.Contains('<script>') -or $notice.message.Contains('VisionMobile')) { throw 'Contenido inseguro o cookie expuesta.' }
    }
    Stop-Process -Id $p.Id; [void]$p.WaitForExit(10000)
    $p = Start-Test
    if (@(Get-OwnerQueue).Count -ne 2) { throw 'No persistieron los avisos.' }
    # Simulate a crash after saving a recipient change but before moving its audit to the outbox.
    Stop-Process -Id $p.Id; [void]$p.WaitForExit(10000)
    $saved = Get-Content -LiteralPath $emailPath -Raw | ConvertFrom-Json
    $pending = @(@{id='audit-recovery-test';addresses=@('recuperado@example.invalid');occurredAtUtc=[DateTimeOffset]::UtcNow})
    $saved | Add-Member -NotePropertyName ownerNotificationPending -NotePropertyValue $pending -Force
    $saved | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $emailPath
    $p = Start-Test
    if (@(Get-OwnerQueue).Count -ne 3) { throw 'No se recuperó el aviso pendiente tras reinicio.' }
    Stop-Process -Id $p.Id; [void]$p.WaitForExit(10000)
    $p = Start-Test
    if (@(Get-OwnerQueue).Count -ne 3) { throw 'Se duplicó el aviso recuperado.' }
    [ordered]@{recipientAddition=$true;onlyNewRecipients=$true;successfulLogin=$true;sessionDeduplication=$true;failedLoginNotNotified=$true;ownerOnly=$true;htmlEscaped=$true;noCodesOrCookies=$true;persistentQueue=$true;auditRecovered=$true;noRealEmailSent=$true} | ConvertTo-Json
} finally {
    if ($p -and !$p.HasExited) {
        if ($p.Path -ne $exe) { throw 'Ruta de proceso inesperada.' }
        Stop-Process -Id $p.Id; [void]$p.WaitForExit(10000)
    }
    [IO.File]::WriteAllText($emailPath,$originalEmail,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($outboxPath,$originalOutbox,[Text.UTF8Encoding]::new($false))
    if ($originalOwner) { [IO.File]::WriteAllText($ownerPath,$originalOwner,[Text.UTF8Encoding]::new($false)) }
    else { @{recipientAddress='autor@example.invalid';enabled=$false} | ConvertTo-Json | Set-Content -LiteralPath $ownerPath }
    if ($null -ne $originalMobileSessions) { [IO.File]::WriteAllBytes($mobileSessionsPath, $originalMobileSessions) }
    elseif (Test-Path -LiteralPath $mobileSessionsPath) { Remove-Item -LiteralPath $mobileSessionsPath }
}
