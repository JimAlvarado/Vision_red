# Estado de Windows Server — Claude

Actualizado por Claude: 5 de octubre de 2026, 18:20 (Ciudad de México). Todo lo registrado aquí se comprobó en el servidor.

En curso: nada.

## Notas para Codex (próximo arranque)

1. Hacer `git pull`. Desde aquí aplica el **Protocolo común** de LEEME.md: línea **En curso** en G15.md y SERVIDOR.md, merge sin `--force`, publicación regenerada tras una mezcla, `test-owner` y `test-volumen` obligatorias, SDK 10.0.401.
2. Línea base común: el servidor ejecuta la aplicación del commit que acompaña la **Intervención 7** (invitaciones al portal móvil), construida sobre `abaaa25`. Partir de `main` actualizado.
3. Ya están resueltos en `e4d447d` los puntos que G15.md lista como "Pendientes anteriores fuera de esta entrega": volumen de correo (resumen, intermitentes, pausa por 429, prioridad y límite de avisos de acceso, visibilidad), diario JSONL, `.ps1` con BOM, texto del autor configurado y apagado, y límite del cuerpo del `PUT`. Detalle en la Intervención 5. Conviene revisar el diff y actualizar G15.md.
4. Agregar `En curso: nada` (o el tema actual) al inicio de G15.md.
5. Sigue pendiente para ambos: arranque tras reiniciar Windows Server, respaldo/restauración y persistencia de sesiones móviles tras reiniciar Vision.

## Situación actual

- Ruta: `C:\Proyectos\Vision_red`; servicio: `Vision` (automático, cuenta de dominio del usuario), `Running`.
- Aplicación ejecutada: **invitaciones al portal móvil** (Intervención 7, compilada en el servidor por excepción autorizada), sobre `abaaa25`. El commit que la contiene acompaña este registro.
- Accesos móviles: 4 códigos sin cambios, los 4 libres al cierre; sin invitaciones enviadas todavía. El apartado se llama ahora **"Móvil"**.

## Intervención 7 — 5 de octubre de 2026, 18:10–18:20: invitaciones al portal móvil (excepción autorizada)

Pedido del usuario, limitado a: invitar por correo desde el apartado de celular con un código libre, renombrar el apartado a "Móvil" y mostrar los códigos como "En uso" o "Libre". Se publicó el aviso **En curso** antes de empezar (`8ff0999`).

- **Interfaz** (`acceso-celular.html/.js`, `correo.css`): título y encabezado "Vision móvil"; estado de cada código "Libre" o "En uso · dispositivo", más "Invitación enviada a … el …" si corresponde. Sección **Invitar a una persona**: correo, lista solo con códigos libres (prefiere uno nunca enviado), aviso si ese código ya se envió a otra persona, confirmación y resultado. El menú del mapa (`index.html`) y el menú de configuración (`branding.js`) dicen "Móvil". La URL `acceso-celular.html` no cambió.
- **Servidor**: `MobileInvitations.cs` (nuevo) con `POST /api/mobile/invite {email, slot}`, solo editor local (403 por móvil o sin cabecera). Valida el correo (dominio con punto, igual que destinatarios), que el código exista y esté **libre** (409 si está en uso), que Vision escuche en la VPN y que no haya pausa por límite de Microsoft. Envía con `EmailChannel.SendDirectAsync` (el envío a Graph se extrajo a `PostMailAsync`, compartido con las alertas). Un 429 o una aceptación actualizan la pausa general compartida con las alertas. `GET /api/mobile/access` agrega `invitations`. `MobileAccess` solo suma `SlotCount`, `CodeForSlot` y `SlotInUse`; la lógica de sesiones de Codex no cambió.
- **Privacidad**: la última invitación por código (correo y fecha) se guarda en `datos/mobile-invitations.json`, visible solo en el editor local. El historial, que también se consulta desde el móvil, registra `mobile_invitation_sent/failed` con el número de código, sin correo ni código.
- **Correo de invitación**: asunto `[VISION-APODACA] Invitación al portal móvil de Vision`; código, enlace VPN, pasos (VPN, abrir enlace, escribir código) y reglas (un dispositivo a la vez, cerrar sesión o pedir liberación, cookies, no compartir). Todo el contenido variable se codifica como HTML.
- **Pruebas**: `test-volumen` con 9 comprobaciones nuevas de invitaciones (correo inválido, código inexistente, código en uso, VPN inactiva, pausa por límite, sin buzón autorizado no envía, no registra fallidas, contenido codificado, validación compartida): 40/40. `test-owner` pasa. `node --check` correcto en los JS modificados.
- **Instalación**: publicación con 14 archivos distintos (aplicación y comprimidos de las 5 páginas tocadas); detener (UAC) → respaldo de `datos` y de los 14 archivos anteriores → copia verificada → iniciar (UAC). `datos` intacto, códigos idénticos, correo sin cambios, 0 errores. En el servicio: invitación por móvil o sin cabecera → 403; correo sin dominio y código inexistente → 400; cuerpo de 20 KB → 413. Captura con Edge headless: la página se ve correctamente.
- **No probado**: el envío real de una invitación (lo hará el usuario desde la página).
- Correo: buzón institucional (`organizational-device-code`) autorizado. Alertas de red automáticas **encendidas** (decisión del usuario). Avisos al autor configurados y **apagados**. 2 destinatarios (el usuario editó la lista a las 16:06).
- Monitoreo `operating`, 19 equipos (el usuario agregó y retiró un equipo entre 17:27 y 17:28; revisión 176).

## Intervención 6 — 5 de octubre de 2026, 17:40–17:50: instalación de `abaaa25` (accesos móviles exclusivos)

- Integración: durante la Intervención 5 Codex publicó `03a9f28`/`20299f0`. Claude los mezcló con `e4d447d` en `abaaa25` (merge): `MobileAccess.cs` conserva la reserva exclusiva de Codex y registra si el aviso al autor se omitió por el límite; `correo.css` y `.gitignore` conservan ambas partes; publicación regenerada; `test-volumen` (31) y `test-owner` (incluido `MobileAccessTests`) pasan.
- Instalación: detener (UAC) → respaldo de `datos` → `git pull --ff-only` → iniciar (UAC). Archivos publicados idénticos a HEAD; `datos` con el mismo SHA-256; `mobile-access.dpapi` y `mobile-access-codes.dpapi` sin cambios; los 4 códigos idénticos a los previos (comparados por huella, sin registrarlos). Panel, mapa (revisión 176), móvil y `acceso-celular.html` 200; correo sin cambios (automático encendido, autor apagado, 2 destinatarios); 0 errores al arrancar.
- Prueba HTTP desde el servidor con tres sesiones de navegador independientes por el puerto móvil local: A entra con el código 1 (200); B con el mismo código → **409**; B con el código 2 → 200 sin afectar a A; reingreso de A con su cookie → 200 sin duplicar; liberar desde el puerto móvil o sin cabecera de editor → 403; liberar el acceso 1 desde el editor → A recibe 401 y B sigue con 200; C entra con el código 1 liberado → 200; cierre de sesión de C → 200 y después 401. Al final se liberó el acceso 2; los 4 quedaron disponibles. Se creó `mobile-sessions.dpapi` en `datos`. Avisos al autor apagados: sin correos.
- No probado: persistencia de sesiones tras otro reinicio de Vision (se verá en el próximo reinicio) y prueba con dos celulares reales (la hará el usuario).

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

## Pendientes

- Codex: revisar y, si corresponde, ajustar desde G15 las correcciones de la Intervención 5 (no rehacerlas). Usar `test-volumen` como regresión.
- Redes: pérdidas intermitentes de alcance desde el servidor hacia la red de equipos.
- Prueba de arranque tras reiniciar Windows Server; respaldo/restauración.

Avisar al usuario antes de cada push. No registrar códigos, credenciales, cuentas, IPs ni contenido de datos privados.
