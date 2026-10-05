# Estado de Windows Server — Claude

Actualizado por Claude: 5 de octubre de 2026, 15:30 (Ciudad de México). Todo lo registrado aquí se comprobó en el servidor.

## Situación actual

- Ruta: `C:\Proyectos\Vision_red`; servicio: `Vision` (automático, cuenta de dominio del usuario).
- Commit de aplicación ejecutado: `fc9c0c7` (incluye `71153f5`). HEAD local: `72fc5af`, sin cambios locales.
- Servicio, panel, mapa y acceso móvil funcionando.
- Correo: **sin migrar**. Sigue el buzón personal (`personal-device-code`), autorizado, envío automático de red **habilitado**, avisos al autor **desactivados** (no se ejecutó `Configurar-Avisos-Autor.ps1` ni `Configurar-Correo-Institucional.ps1`), 3 destinatarios. El límite diario de Outlook.com puede seguir activo.

## Intervención 3 — 5 de octubre de 2026, 15:22–15:30: actualización a `72fc5af`

- Revisión previa de `fc9c0c7`/`72fc5af`: modo personal sigue usando `consumers` (compatible con la configuración actual); prefijo `[VISION-APODACA]` correcto; `.gz`/`.br` de `correo.*` coinciden; `Configurar-Correo-Institucional.ps1` respalda antes de cambiar y deja ambos canales apagados. Sin observaciones bloqueantes.
- Detener (UAC) y confirmar proceso cerrado → respaldo de `datos` (zip local fuera de Git) → `git pull --ff-only` con árbol limpio → iniciar (UAC) y confirmar `Running`.
- `.exe`/`.dll` idénticos a HEAD; 19 archivos de `datos` con el mismo SHA-256 antes y después del pull.
- Mapa 19 equipos/18 enlaces/revisión 172; detección activa, condición `operating`; `correo.html` sirve la versión con avisos al autor; móvil 200 en localhost; `PUT /api/email/recipients` por móvil o sin cabecera de editor → 403.
- Al arrancar se repitió una vez el error conocido del diario JSONL (ver pendientes).
- Migración institucional (pasos 3–4 de G15.md) **no realizada**: requiere remitente institucional y correo del autor que el usuario debe indicar en este chat. Sin correos enviados.

## Intervención 2 — 5 de octubre de 2026, 12:40–13:30: correos que no salen

- Prueba del usuario a las 12:40: Vision detectó la caída y la recuperación de un equipo y generó ambos avisos. Microsoft respondió **HTTP 429** a cada intento; tras 5 intentos quedaron `failed`.
- **Causa confirmada:** límite diario de mensajes de Outlook.com para una cuenta personal sin verificar. El buzón de alertas recibió de `member_services@outlook.com` el aviso "Verify your account to do more with Outlook.com" ("The daily message limit…") a las 15:49 UTC = **09:49 CDMX**, el mismo minuto del primer 429. No lo causó la actualización ni el cambio de destinatarios.
- Origen del consumo: 25 correos aceptados entre 09:18 y 09:44, cada uno a varios destinatarios; 10 salieron en el mismo segundo (09:21:46), uno por cada equipo caído. Un equipo que cae y se recupera repetidamente generó 14 de los avisos posteriores.
- Desde 09:49 **todos los envíos fallan**: 23 avisos de red perdidos (`failed`). Las alertas de red además caducan a los 30 min, menos que un bloqueo de este tipo.
- Acción del usuario pendiente: verificar la cuenta (sube el límite) o esperar ~24 h. Claude no envió correos de prueba.
- Los avisos "Nuevo inicio de sesión detectado" (servicio "Vision - Correo de pruebas") corresponden a la renovación de sesión de la propia aplicación.

### Para Codex: prioridad antes de agregar más correos

1. **Agrupar avisos:** un correo por ronda con todos los equipos que cambiaron, no uno por equipo.
2. **Amortiguar intermitencias:** no avisar cada caída/recuperación de un equipo que oscila; resumir.
3. **429:** espera creciente entre reintentos en lugar de reintentar cada 60 s cinco veces; reintentar cada minuto puede prolongar el bloqueo y agota los intentos en 5 min.
4. **Visibilidad:** mostrar en el panel y en la consulta móvil que el correo está limitado por Microsoft; hoy solo aparece en registros.
5. **`71153f5` comparte el mismo cupo:** cada inicio de sesión móvil válido envía un correo al autor. El código es compartido; quien lo conozca puede iniciar sesión repetidamente (sin cookie, varios dispositivos) y agotar el cupo diario, dejando sin alertas de red. Sugerencia: límite o resumen de avisos de acceso (p. ej. uno por IP/dispositivo por día, o resumen diario), y que las alertas de red tengan prioridad en la cola sobre los avisos al autor (hoy la cola es FIFO común).
6. A mediano plazo: un buzón corporativo de Microsoft 365 tiene límites mucho mayores que una cuenta personal de Outlook.com.

### Revisión de `71153f5` por Claude (aplicado en Intervención 3, avisos al autor sin configurar)

- Correcto: canal exclusivo del autor, deduplicación de altas con id persistente, HTML codificado, login con sesión activa no duplica aviso, consulta móvil no expone destinatarios.
- `Configurar-Avisos-Autor.ps1` probado en este servidor (Windows PowerShell 5.1) sobre una copia de `datos` con correo ficticio: conserva los campos, agrega al autor y genera `owner-notifications.json`. Escribe UTF-8 con BOM; .NET lo lee sin problema.
- Riesgo menor: si `owner-notifications.json` queda mal formado, `OwnerNotifications` lanza excepción al construirse y el canal de correo no arranca.
- La configuración del autor y su prueba quedan para la migración institucional.

## Intervención 1 — 5 de octubre de 2026, 12:28–12:35: actualización a `29d20f0`

- Detener (UAC) → respaldo de `datos` (zip local fuera de Git) → `git pull --ff-only` → iniciar (UAC). `.exe`/`.dll` idénticos a HEAD; 19 archivos de `datos` con el mismo SHA-256 antes y después.
- Mapa 19 equipos/18 enlaces/revisión 172; móvil 200 en localhost; `PUT /api/email/recipients` por móvil o sin cabecera de editor → 403.

## Pendientes para Codex (siguen abiertos)

- **Conflicto de acceso a `eventos-monitor-*.jsonl`:** `EventJournalWorker.cs` lee con `File.ReadLines` (sin compartir escritura) mientras `MonitoringWorker.cs` añade con `File.AppendAllTextAsync`. 24 errores en 3 días; 5 abortaron una ronda completa de monitoreo. Leer con `FileShare.ReadWrite | FileShare.Delete` y no abortar la ronda si falla el diario.
- Menores de `29d20f0`: límite de 16 KB del `PUT` evaluado después de leer el cuerpo (usar `RequestSizeLimit`); validación acepta dominios sin punto.

Avisar al usuario antes de cada push. No registrar códigos, credenciales, cuentas, IPs ni contenido de datos privados.
