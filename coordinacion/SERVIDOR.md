# Estado de Windows Server — Claude

Actualizado por Claude: 5 de octubre de 2026, 16:15 (Ciudad de México). Todo lo registrado aquí se comprobó en el servidor.

## Situación actual

- Ruta: `C:\Proyectos\Vision_red`; servicio: `Vision` (automático, cuenta de dominio del usuario), `Running`.
- Commit de aplicación ejecutado: `fc9c0c7`. HEAD local: `ee87f64` (solo coordinación sobre `72fc5af`), sin cambios locales.
- Correo: **migrado al buzón institucional** (`organizational-device-code`, tenant `organizations`, mismo ClientId). Autorizado desde la cuenta de Windows del servicio; la identidad corresponde al buzón pedido por el usuario. Prueba aceptada por Microsoft y **recepción confirmada por el usuario**.
- Alertas de red automáticas: **encendidas desde 16:05 por decisión del usuario**, antes de la protección de volumen (ver Intervención 4). Avisos al autor: **configurados pero apagados**. 3 destinatarios; el autor solicitado está entre ellos.
- Monitoreo: `operating`. Hubo pérdida de alcance a la red monitoreada entre 15:51 y 15:56 (ver Intervención 4); no fue un fallo de Vision.

## Intervención 4 — 5 de octubre de 2026, 15:38–16:00: migración al correo institucional

- Datos usados (indicados por el usuario en el chat del servidor, fuera de Git): remitente institucional y correo del autor.
- Antes: cola `accepted=33, expired=146, failed=29`; mapa 19 equipos/18 enlaces/revisión 172.
- Detener (UAC) y confirmar proceso cerrado → respaldo de `datos` (zip local fuera de Git) → `Configurar-Avisos-Autor.ps1` → `Configurar-Correo-Institucional.ps1`, sin iniciar entre ambos → iniciar (UAC) y confirmar `Running`. Ambos scripts idénticos a `fc9c0c7`.
- **Problema en los scripts (para Codex):** están en UTF-8 **sin BOM**. Windows PowerShell 5.1 los lee como ANSI; la flecha `→` del mensaje final se convierte en una comilla tipográfica y el script falla al analizarse (`TerminatorExpectedAtEndOfString`, línea 27 de `Configurar-Avisos-Autor.ps1`). No se modificó nada: el análisis falla antes de ejecutar. Se ejecutaron copias byte a byte idénticas guardadas con BOM, con `-PanelPath` explícito. Corrección: guardar los `.ps1` en UTF-8 con BOM o dejarlos solo en ASCII. Afecta a todos los `.ps1` de `work/tooling` con caracteres no ASCII.
- Resultado de los scripts: modo institucional, ClientId sin cambios, sesión y recibo anteriores retirados y respaldados en `datos`, destinatarios conservados (3, autor incluido), ambos canales apagados. Resto de `datos` (mapa, cola, historial) con el mismo SHA-256.
- Autorización: el usuario inició sesión por código de dispositivo con el buzón institucional; Vision muestra `connected` (solo lo hace si la cuenta coincide con el remitente) y generó la sesión DPAPI con la cuenta del servicio.
- **Una sola prueba** (15:50:14): Microsoft la aceptó (`accepted`, con request-id). Asunto registrado: `[VISION-APODACA] Prueba de correo`. El usuario confirmó la recepción con remitente institucional y la misma referencia. No se repitió.
- Después: cola `accepted=33, expired=146, failed=29, awaiting-configuration=1`. No se reenvió nada. El nuevo aviso en espera es la pérdida de acceso de las 15:51; no sale porque los canales están apagados y caducará a los 30 min.
- **Pérdida de acceso a la red desde 15:51:** los 19 equipos responden `TimedOut` y se abrió el incidente `network_down`. Comprobado fuera de Vision: el servidor tampoco recibe respuesta a ping de los equipos; la traza llega al segundo salto (red interna) y se corta después; el gateway del servidor responde. Es un problema de ruta, enlace o firewall entre el servidor y la red de equipos (ya ocurrió varias veces por la mañana). Lo debe revisar el área de redes.
- Diario JSONL: 2 errores desde 15:22 (15:24:15 y 15:41:16, uno por arranque). Sigue abierto.
- **16:02–16:08, activación de alertas de red (decisión explícita del usuario).** El usuario hizo una prueba de caída y el aviso quedó `awaiting-configuration` por tener los canales apagados. Se le explicó el riesgo (sin agrupación, un correo por equipo y por cambio) y pidió activarlas. No hay interruptor en el panel: detener (UAC) → respaldo de `email-settings.json` en `datos` → `automaticAlertsEnabled=true` (resto de campos sin cambios) → iniciar (UAC). Avisos al autor siguen `enabled=false`.
- Al arrancar se enviaron los 6 avisos en espera (todos con menos de 30 min): 4 del acceso a la red (15:51–15:56) y la caída y recuperación de prueba (16:00 y 16:04). Microsoft aceptó los 6 entre 16:05:43 y 16:06:19, al primer intento. El usuario confirmó la recepción. La sesión institucional siguió válida después del reinicio.
- Para Codex: en `correo.js` el texto "Avisos al autor pendientes de configurar en el servidor" aparece también cuando el autor está configurado pero `enabled=false`; conviene distinguir "configurado y apagado" de "sin configurar". Tampoco existe un interruptor en el panel para las alertas de red.

## Intervención 3 — 5 de octubre de 2026, 15:22–15:30: actualización a `72fc5af`

- Revisión de `fc9c0c7`: modo personal compatible, prefijo `[VISION-APODACA]` correcto, `.gz`/`.br` coinciden. Sin observaciones bloqueantes.
- Detener (UAC) → respaldo → `git pull --ff-only` → iniciar (UAC). `.exe`/`.dll` idénticos a HEAD; `datos` con el mismo SHA-256; mapa, móvil y bloqueo de `PUT` por móvil verificados.

## Intervención 2 — 5 de octubre de 2026, 12:40–13:30: correos que no salen (buzón personal)

- HTTP 429 desde 09:49 CDMX por **límite diario de Outlook.com** de la cuenta personal sin verificar (aviso "Verify your account…" del mismo minuto). Origen: 25 correos en 26 min, 10 en el mismo segundo, más avisos de un equipo intermitente.
- Para Codex, antes de reactivar envíos: agrupar avisos por ronda; amortiguar intermitencias; espera creciente ante 429; mostrar el límite en el panel y el móvil; limitar los avisos de acceso móvil y dar prioridad a la red en la cola. El buzón institucional por sí solo no protege el volumen.
- Revisión de `71153f5`: correcta; riesgo menor si `owner-notifications.json` queda mal formado (el canal de correo no arranca).

## Pendientes para Codex (siguen abiertos)

- **Conflicto de acceso a `eventos-monitor-*.jsonl`:** `EventJournalWorker.cs` lee con `File.ReadLines` (sin compartir escritura) mientras `MonitoringWorker.cs` añade con `File.AppendAllTextAsync`. Leer con `FileShare.ReadWrite | FileShare.Delete` y no abortar la ronda si falla el diario.
- Codificación de los `.ps1` (Intervención 4).
- Protección de volumen de correo (Intervención 2) antes de reactivar canales.
- Menores de `29d20f0`: límite de 16 KB del `PUT` evaluado después de leer el cuerpo; validación acepta dominios sin punto.

Avisar al usuario antes de cada push. No registrar códigos, credenciales, cuentas, IPs ni contenido de datos privados.
