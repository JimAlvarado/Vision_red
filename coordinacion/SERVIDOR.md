# Estado de Windows Server — Claude

Actualizado por Claude: 5 de octubre de 2026, 17:45 (Ciudad de México). Todo lo registrado aquí se comprobó en el servidor.

## Situación actual

- Ruta: `C:\Proyectos\Vision_red`; servicio: `Vision` (automático, cuenta de dominio del usuario), `Running`.
- Aplicación ejecutada: **compilada en el servidor por Claude** (Intervención 5), sobre `ee87f64`. El commit que la contiene es el que acompaña a este registro.
- Correo: buzón institucional (`organizational-device-code`) autorizado. Alertas de red automáticas **encendidas** (decisión del usuario). Avisos al autor configurados y **apagados**. 2 destinatarios (el usuario editó la lista a las 16:06).
- Monitoreo `operating`, 19 equipos (el usuario agregó y retiró un equipo entre 17:27 y 17:28; revisión 176).

## Intervención 5 — 5 de octubre de 2026, 16:45–17:45: correcciones hechas en el servidor (excepción autorizada)

**Excepción a la regla "el servidor no modifica código", autorizada por el usuario solo para este caso:** Claude corrigió en el servidor los puntos que había reportado a Codex. **Codex: hacer `git pull` antes de continuar** y no repetir estas correcciones; revisar el diff y ajustar lo que convenga desde G15.

### Entorno de compilación

- SDK .NET **10.0.401** (runtime 10.0.12, el mismo de la publicación de Codex) instalado sin administrador en `work/tooling/dotnet` (fuera de Git). Compilación sin cambios previa: 420 de 424 archivos idénticos byte a byte a la publicación de Codex; solo difieren dll/exe/pdb/endpoints.
- Publicación con los cambios: 14 archivos distintos (dll, exe, pdb, endpoints y `.gz`/`.br` de `correo.*` y `mobile.*`); 410 idénticos; sin archivos privados. Respaldo de los 14 anteriores en `work/tooling/rollback-*` (local, fuera de Git).

### Cambios

1. **Diario JSONL** (`JournalFile.cs` nuevo, `EventJournalWorker.cs`, `MonitoringWorker.cs`): lectura y escritura con `FileShare.ReadWrite | FileShare.Delete`; la escritura reintenta hasta 5 veces y, si falla, registra advertencia **sin abortar la ronda** (el cambio se escribe en la siguiente ronda).
2. **Agrupación** (`NotificationOutbox.ClaimBatch`, `EmailDeliveryWorker`, `AlertMessage.ComposeDigest`): las alertas de red esperan 20 s (5 s sin cambios nuevos, máximo 60 s) y salen en **un solo correo "Resumen de red"** con tabla por equipo, duración de recuperaciones y equipos que siguen sin respuesta. Un único cambio de un equipo estable conserva el mensaje individual anterior.
3. **Intermitencias**: un equipo con 2 o más pérdidas en 30 min es intermitente; sus cambios se retienen hasta 10 min sin cambios (o 30 min desde el primero retenido) y salen resumidos ("Intermitente: N pérdidas y M recuperaciones, último estado"). La primera caída y su recuperación salen normalmente.
4. **429 de Microsoft**: pausa **general** de todos los envíos (`correo-limite.json` en `datos`, sobrevive reinicios), 60 s → 2 → 4 … hasta 60 min, respetando `Retry-After`. Los intentos limitados **no cuentan** para los 5 intentos; esos avisos caducan a las 3 h (los normales siguen a 30 min). Al aceptarse un envío la pausa termina. Evento `email_limited` / `email_limit_cleared` en el historial.
5. **Prioridad y avisos de acceso**: la red sale antes que los avisos al autor. Acceso móvil: un aviso por IP y tipo de dispositivo cada 24 h, máximo 10 por día; el evento `login_success` indica si se omitió.
6. **Visibilidad**: `correo.html` muestra la pausa por límite y tiene **interruptor** de envío automático (`PUT /api/email/automatic {enabled}`, solo editor local, registra evento). La consulta móvil muestra "Microsoft limitó el envío · se reintenta a las …" (`limitedUntilUtc` en `/api/mobile/overview` y `/api/email/status`).
7. **Menores**: límite de 16 KB aplicado en el middleware antes de leer el cuerpo de `/api/email/*` (también chunked); destinatarios exigen dominio con punto (servidor y navegador); `owner-notifications.json` dañado ya no detiene el correo (avisos al autor apagados + `ownerConfigurationError`); texto "Avisos al autor configurados y desactivados" separado de "sin configurar".
8. **Scripts `.ps1`**: `Configurar-Avisos-Autor`, `Configurar-Correo-Institucional`, `Probar-Avisos-Autor` y `Probar-Correo-Institucional` guardados en UTF-8 **con BOM** (contenido igual). Comprobado: los dos primeros fallaban al analizarse en Windows PowerShell 5.1 y ahora no.

### Pruebas

- `work/tooling/test-volumen` (nuevo, versionado): 31 comprobaciones con transporte ficticio, sin correo real: concurrencia del diario (3000 escrituras con lector simultáneo, y reproduce el conflicto anterior), agrupación, pausa por 429 (persistencia, crecimiento, no consume intentos, no caduca a 30 min), caducidades, prioridad, intermitentes, HTML codificado, worker con un solo envío, límites de acceso móvil, autor dañado, dominio sin punto e interruptor. **Todas pasan.**
- `work/tooling/test-owner` de Codex: **pasa** sin cambios.
- En el servicio: panel, mapa y móvil 200; `PUT /api/email/automatic` sin valor → 400, sin cabecera de editor → 403, por móvil → 403; `PUT` de destinatarios de 20 KB → 413; dominio sin punto → 400. Ninguna prueba modificó la configuración. **Sin errores del diario JSONL al arrancar** (antes aparecía en cada arranque).
- Instalación: detener (UAC) → respaldo de `datos` → copia de los 14 archivos verificados por hash → iniciar (UAC). `datos` intacto (mismo SHA-256).

### No probado

- Un 429 real de Microsoft y un resumen real con varios equipos (requieren una caída real o forzarla). El próximo cambio múltiple de red generará el primer resumen real.

## Intervención 4 — 5 de octubre de 2026, 15:38–16:08: migración al correo institucional

- Scripts de Codex ejecutados como copias con BOM (corregido en Intervención 5); buzón institucional autorizado desde la cuenta del servicio; una sola prueba aceptada y recibida. Canales apagados por el script.
- 16:05: el usuario pidió encender las alertas de red; se enviaron los 6 avisos en espera (aceptados al primer intento, recibidos).
- 15:51–15:56: pérdida de alcance a la red de equipos desde el servidor (ping y traza fallan fuera del servidor); problema de red, no de Vision.

## Intervención 3 — 5 de octubre de 2026, 15:22–15:30: actualización a `72fc5af`

- Detener → respaldo → `git pull --ff-only` → iniciar. Binarios idénticos a HEAD, `datos` intacto.

## Pendientes

- Codex: revisar y, si corresponde, ajustar desde G15 las correcciones de la Intervención 5 (no rehacerlas). Usar `test-volumen` como regresión.
- Redes: pérdidas intermitentes de alcance desde el servidor hacia la red de equipos.
- Prueba de arranque tras reiniciar Windows Server; respaldo/restauración.

Avisar al usuario antes de cada push. No registrar códigos, credenciales, cuentas, IPs ni contenido de datos privados.
