# Estado de Windows Server — Claude

Actualizado por Claude: 5 de octubre de 2026, 12:35 (Ciudad de México). Todo lo registrado aquí se comprobó en el servidor.

## Situación actual

- Ruta: `C:\Proyectos\Vision_red`; servicio: `Vision` (automático, cuenta de dominio del usuario).
- Commit de aplicación ejecutado: `29d20f0` (incluye `ff75dfd`). Antes de esta intervención corría `637d44e`.
- HEAD del repositorio del servidor: `4482520`, sin cambios locales.
- Servicio, panel, mapa, acceso móvil y correo: funcionando (detalle abajo).

## Intervención 1 — 5 de octubre de 2026, 12:28–12:35

- Tarea realizada: actualización `637d44e` → `4482520` encargada por el usuario.
- Procedimiento: línea base del panel → detener Vision con elevación (UAC) y confirmar `Stopped` y proceso cerrado → respaldo de `datos` en `outputs/MonitorRed.Panel/Respaldo-Antes-Actualizacion-4482520.zip` (local, fuera de Git) → `git pull --ff-only` con árbol limpio → iniciar Vision con elevación y confirmar `Running`.
- Servicio y ruta del ejecutable registrados: `"C:\Proyectos\Vision_red\outputs\MonitorRed.Panel\MonitorRed.Panel.exe"`. `.exe` y `.dll` en disco idénticos a los de HEAD.
- Configuración y datos locales conservados: sí. Los 19 archivos de `datos` tienen el mismo SHA-256 antes y después del pull.
- Panel/mapa: 5080 responde 200; mapa con 19 equipos, 18 enlaces, revisión 172, igual que antes de actualizar. Detección de incidentes activa, condición `operating`.
- Acceso móvil: 5081 responde 200 en localhost y escucha en la IP de VPN. Desde el propio servidor la IP de VPN responde 403 (esperado: fuera de la subred autorizada). `PUT /api/email/recipients` por 5081 → 403; por 5080 sin cabecera de editor → 403.
- Correo: buzón autorizado, envío automático habilitado, 2 destinatarios (sin cambios). `correo.html` sirve la versión con el formulario de destinatarios. No se envió ningún correo ni se modificó la lista.
- No probado: guardar destinatarios desde el formulario (el usuario no ha indicado la lista), correo de caída y recuperación, arranque tras reiniciar Windows.

## Problemas para Codex

1. **Conflicto de acceso a `eventos-monitor-*.jsonl` (existía antes de esta actualización).** El registro de Aplicación muestra 24 errores en 3 días. `EventJournalWorker.cs:18` lee con `File.ReadLines`, que no comparte escritura, mientras `MonitoringWorker.cs:134` añade con `File.AppendAllTextAsync`. Cuando chocan:
   - falla la escritura → "Falló la ronda de comprobaciones." (5 veces): **se pierde una ronda completa de monitoreo**;
   - falla la lectura → "No se pudo actualizar el registro CSV de eventos." (19 veces, una al arrancar esta versión).
   Sugerencia: leer con `FileStream` y `FileShare.ReadWrite | FileShare.Delete`, y que un fallo al escribir el diario no aborte la ronda (reintento corto o registrar y continuar).
2. Menores de `29d20f0`: el límite de 16 KB del `PUT` se revisa después de leer el cuerpo (conviene `RequestSizeLimit` en el endpoint); la validación acepta dominios sin punto (`usuario@dominio`).
3. Los BAT de servicio piden UAC y esperan Enter; el procedimiento se hizo con comandos equivalentes (`Stop-Service`/`Start-Service` + espera de estado) aprobando UAC.

## Siguiente paso

- Codex: corregir el punto 1 y publicar; Claude lo aplica cuando el usuario lo encargue.
- Probar el formulario de destinatarios cuando el usuario indique la lista definitiva.
- Pendientes compartidos: prueba de caída/recuperación autorizada, reinicio de Windows Server, respaldo/restauración.

Avisar al usuario antes de cada push. No registrar códigos, credenciales, cuentas, IPs ni contenido de datos privados.
