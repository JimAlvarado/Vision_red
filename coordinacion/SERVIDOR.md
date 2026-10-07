# Estado de Windows Server - Codex Server

Actualizado por Codex Server: 7 de octubre de 2026, 16:08 (Ciudad de Mexico). Entrega de G15 instalada y verificada.

En curso: nada.

## Notas para Codex (próximo arranque)

1. Hacer `git pull`. Desde aquí aplica el **Protocolo común** de LEEME.md: línea **En curso** en G15.md y SERVIDOR.md, merge sin `--force`, publicación regenerada tras una mezcla, `test-owner` y `test-volumen` obligatorias, SDK 10.0.401.
2. Aplicacion instalada: `591a3acbaf95b240498cb9a3b7a386c7622c1621`; HEAD recibido: `fa42366`. Ver instalacion 10 y G15.md.
3. Ya están resueltos en `e4d447d` los puntos que G15.md lista como "Pendientes anteriores fuera de esta entrega": volumen de correo (resumen, intermitentes, pausa por 429, prioridad y límite de avisos de acceso, visibilidad), diario JSONL, `.ps1` con BOM, texto del autor configurado y apagado, y límite del cuerpo del `PUT`. El detalle (antes "Intervención 5") está en `git show 3b58c95:coordinacion/SERVIDOR.md` y en el mensaje de `e4d447d`. Conviene revisar el diff y actualizar G15.md.
4. Nuevas desde entonces (excepciones autorizadas por el usuario): invitaciones al portal móvil (Intervención 7) y varias redes autorizadas para la consulta móvil (Intervención 8).
5. Agregar `En curso: nada` (o el tema actual) al inicio de G15.md.

## Situacion reportada el 5 de octubre (historica; revision actual abajo)

- Ruta: `C:\Proyectos\Vision_red`; servicio: `Vision` (automático, cuenta de dominio del usuario), `Running`.
- Aplicación ejecutada: **varias redes para la consulta móvil** (Intervención 8), compilada en el servidor por excepción autorizada. El commit que la contiene acompaña este registro.
- Consulta móvil: autoriza la red de la VPN corporativa y la **VLAN 100** de Arzyz (rangos en `datos/mobile-vpn-settings.json`, fuera de Git). 4 códigos sin cambios; el código 1 en uso (iPhone del usuario, entrada desde la VLAN 100), los otros 3 libres. Una invitación enviada (código 1) y recibida por el usuario.
- Correo: buzón institucional autorizado. Alertas de red automáticas **encendidas** (decisión del usuario). Avisos al autor configurados y **apagados**. 2 destinatarios.
- Monitoreo `operating`, 19 equipos (revisión 176).

## Instalacion 10 - 7 de octubre de 2026, 16:07: entrega de G15

- Pedido del usuario: pull de las actualizaciones de G15. Aplicacion instalada: `591a3acbaf95b240498cb9a3b7a386c7622c1621`; HEAD recibido: `fa42366`. Fuente y publicacion autocontenida recibidas por `git pull --ff-only`; Server no compilo.
- Servicio detenido antes del pull y cierre del proceso confirmado. Windows marco detenido antes de terminar el proceso: se espero su cierre, sin forzarlo. Respaldo local completo en `work/tooling/rollback-20261007-160714/MonitorRed.Panel` (fuera de Git). Los 30 archivos de datos se comprobaron por SHA-256 en respaldo y tras el pull, antes de iniciar: identicos.
- Vision Running/Automatic; nuevo proceso desde 16:07:26. Monitoreo operating, 19/19 online; mapa HTTP 200; pagina movil local HTTP 200. Cuatro codigos y cuatro invitaciones historicas disponibles; interfaz movil activa y nueva propiedad invitationReceipts expuesta en editor local. Datos DPAPI preservados por huella, sin registrar valores ni cambiar cuenta.
- Correo connected, alertas automaticas encendidas, autor apagado, 3 destinatarios. No se enviaron mensajes ni se repitio la invitacion al jefe. Entrega externa y acceso desde un celular real no se probaron.
- Cero eventos de error de MonitorRed.Panel desde este inicio durante la observacion inicial. Las fechas antiguas de devices.csv/links.csv no prueban fallo: la exportacion omite reescritura si el contenido coincide. Falta observacion prolongada de bloqueos CSV y perdidas de referencia; no se declaran resueltos por esta comprobacion breve.
- Incluir datos/mobile-invitation-receipts.json en futuros respaldos cuando exista (se crea al primer intento). Las pruebas de G15 constan en G15.md; no se ejecutaron de nuevo en produccion. Esta instalacion no requirio reiniciar Windows.

## Revision 9 - 7 de octubre de 2026: estabilidad y correo (Codex Server)

- Informe y entrega para G15: [REVISION-2026-10-07.md](REVISION-2026-10-07.md). Incluye evidencia, riesgos encontrados en codigo y pendientes; no se modifico la aplicacion ni se enviaron correos.
- HEAD al revisar: `0bf43b0`; aplicacion en disco: `8313427` (DLL/EXE/PDB coinciden con HEAD). Vision Running, proceso activo desde 5/oct 18:29:03; consulta actual operating, 19/19 online. El proceso en memoria no se inspecciono.
- 187 perdidas de referencia de red con recuperacion en siete dias; 100 desde el ultimo inicio. Nueve fallos de inicio el 3/oct por falta del derecho de inicio como servicio. 33 errores actuales de reemplazo CSV desde el ultimo inicio; causa efectiva pendiente. Son problemas distintos.
- Correo conectado, automatico encendido, autor apagado, 3 destinatarios, sin pausa activa. Cuatro invitaciones aceptadas por Microsoft; entrega al jefe pendiente de identificar y rastrear en Exchange. 29 alertas fallidas historicas (28 por limites, una por autorizacion); no se reintentaron.
- G15 debe preparar correcciones y pruebas; Server solo reviso y documento. La revision no exige reinicio. La Intervencion 6 anterior permanece disponible en el historial Git.

## Intervención 8 — 5 de octubre de 2026, 18:30–18:40: varias redes autorizadas para la consulta móvil (excepción autorizada)

Pedido del usuario: que la consulta móvil funcione también desde la VLAN 100 de Arzyz, además de la VPN, y poder agregar más redes en el futuro. Aviso **En curso** publicado antes de empezar.

- **Causa del 403 reportado**: la consulta móvil solo aceptaba una subred (la de la VPN) y el usuario entraba desde la VLAN 100. No era un fallo de la invitación: esta salió (`mobile_invitation_sent`) y el usuario confirmó la recepción con el código.
- **`VpnMobileNetwork.cs`**: acepta `allowedSubnets` (lista, máximo 20, privadas IPv4 con prefijo /16 a /32); el formato anterior `allowedSubnet` sigue funcionando. `Allows` comprueba todas las redes. `GET /api/mobile/access` agrega `allowedSubnets` y la página Móvil las muestra.
- **`Configurar-Acceso-Privado.ps1`** (con BOM): acepta varias subredes (argumentos o separadas por comas), valida todas antes de tocar nada, **actualiza** la regla `VisionServidorVPN` si existe (comprobando puerto 5081 y ejecutable de Vision) o la crea, y después escribe la configuración. Uso futuro: ejecutarlo como administrador con la lista completa y reiniciar Vision. Validado sin administrador: subred pública, prefijo /8, texto inválido e IP ajena se rechazan sin escribir nada.
- **Textos**: la página Móvil y el correo de invitación dicen "red de Arzyz o VPN corporativa" en lugar de solo VPN.
- **`MonitorRed.Panel.csproj`**: `datos\**` con `CopyToOutputDirectory="Never"`. Al compilar en el servidor, los `.json` de `datos` se copiaban a las carpetas `bin` (locales, fuera de Git); se borraron las copias y ya no se generan.
- **`test-volumen`**: 5 comprobaciones nuevas de redes (varias redes, rechazo fuera de ellas, IPv4 dentro de IPv6, formato anterior, configuraciones inválidas), con rangos de ejemplo: 45/45. Las pruebas ahora crean sus datos bajo su propia carpeta de compilación (antes, ejecutadas desde la carpeta del panel, dejaban `fixtures` ficticios que la publicación incluía; se detectó por el conteo de archivos y se borraron antes de instalar). `test-owner` pasa.
- **Instalación**: publicación de 424 archivos, ninguno privado ni de prueba, 8 distintos (aplicación y comprimidos de `acceso-celular.*`). Detener (UAC) → respaldo de `datos`, de los 8 archivos y de la configuración de red → copia verificada → (UAC) script con las dos redes e inicio. Firewall y configuración con ambas redes; la cuenta del servicio conserva permisos sobre el archivo; códigos idénticos; correo y mapa sin cambios; 0 errores.
- **Confirmado por el usuario**: acceso correcto desde la VLAN 100; el código 1 quedó "En uso (iPhone)" con `login_success` a las 18:32.
- Corrección de coordinación: el aviso `d7b71d2` publicó por error las dos subredes; `d4ccc87` las retiró del archivo (siguen en el historial de ese commit).

## Pendientes

- Codex: revisar y, si corresponde, ajustar desde G15 las correcciones de `e4d447d` y de las Intervenciones 7 y 8 (no rehacerlas). Usar `test-volumen` como regresión.
- Redes: pérdidas intermitentes de alcance desde el servidor hacia la red de equipos.
- Prueba de arranque tras reiniciar Windows Server; respaldo/restauración.

Avisar al usuario antes de cada push. No registrar códigos, credenciales, cuentas, IPs ni contenido de datos privados.