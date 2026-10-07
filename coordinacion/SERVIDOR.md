# Estado de Windows Server — Claude

Actualizado por Codex Server: 7 de octubre de 2026. Revision de lectura; registro historico conservado.

En curso: nada.

## Notas para Codex (próximo arranque)

1. Hacer `git pull`. Desde aquí aplica el **Protocolo común** de LEEME.md: línea **En curso** en G15.md y SERVIDOR.md, merge sin `--force`, publicación regenerada tras una mezcla, `test-owner` y `test-volumen` obligatorias, SDK 10.0.401.
2. Línea base común: el servidor ejecuta la aplicación del commit que acompaña la **Intervención 8**. Partir de `main` actualizado.
3. Ya están resueltos en `e4d447d` los puntos que G15.md lista como "Pendientes anteriores fuera de esta entrega": volumen de correo (resumen, intermitentes, pausa por 429, prioridad y límite de avisos de acceso, visibilidad), diario JSONL, `.ps1` con BOM, texto del autor configurado y apagado, y límite del cuerpo del `PUT`. El detalle (antes "Intervención 5") está en `git show 3b58c95:coordinacion/SERVIDOR.md` y en el mensaje de `e4d447d`. Conviene revisar el diff y actualizar G15.md.
4. Nuevas desde entonces (excepciones autorizadas por el usuario): invitaciones al portal móvil (Intervención 7) y varias redes autorizadas para la consulta móvil (Intervención 8).
5. Agregar `En curso: nada` (o el tema actual) al inicio de G15.md.

## Situación actual

- Ruta: `C:\Proyectos\Vision_red`; servicio: `Vision` (automático, cuenta de dominio del usuario), `Running`.
- Aplicación ejecutada: **varias redes para la consulta móvil** (Intervención 8), compilada en el servidor por excepción autorizada. El commit que la contiene acompaña este registro.
- Consulta móvil: autoriza la red de la VPN corporativa y la **VLAN 100** de Arzyz (rangos en `datos/mobile-vpn-settings.json`, fuera de Git). 4 códigos sin cambios; el código 1 en uso (iPhone del usuario, entrada desde la VLAN 100), los otros 3 libres. Una invitación enviada (código 1) y recibida por el usuario.
- Correo: buzón institucional autorizado. Alertas de red automáticas **encendidas** (decisión del usuario). Avisos al autor configurados y **apagados**. 2 destinatarios.
- Monitoreo `operating`, 19 equipos (revisión 176).

## Revision 9 - 7 de octubre de 2026: estabilidad y correo (Codex Server)

- Informe y entrega para G15: [REVISION-2026-10-07.md](REVISION-2026-10-07.md). Incluye evidencia, riesgos encontrados en codigo y pendientes; no se modifico la aplicacion ni se enviaron correos.
- HEAD al revisar: `0bf43b0`; aplicacion en disco: `8313427` (DLL/EXE/PDB coinciden con HEAD). Vision Running, proceso activo desde 5/oct 18:29:03; consulta actual operating, 19/19 online. El proceso en memoria no se inspecciono.
- 187 perdidas de referencia de red con recuperacion en siete dias; 100 desde el ultimo inicio. Nueve fallos de inicio el 3/oct por falta del derecho de inicio como servicio. 33 errores actuales de reemplazo CSV desde el ultimo inicio; causa efectiva pendiente. Son problemas distintos.
- Correo conectado, automatico encendido, autor apagado, 3 destinatarios, sin pausa activa. Cuatro invitaciones aceptadas por Microsoft; entrega al jefe pendiente de identificar y rastrear en Exchange. 29 alertas fallidas historicas (28 por limites, una por autorizacion); no se reintentaron.
- G15 debe preparar correcciones y pruebas; Server solo reviso y documento. La revision no exige reinicio. La Intervencion 6 anterior permanece disponible en el historial Git.

## Pendientes

- Codex: revisar y, si corresponde, ajustar desde G15 las correcciones de `e4d447d` y de las Intervenciones 7 y 8 (no rehacerlas). Usar `test-volumen` como regresión.
- Redes: pérdidas intermitentes de alcance desde el servidor hacia la red de equipos.
- Prueba de arranque tras reiniciar Windows Server; respaldo/restauración.

Avisar al usuario antes de cada push. No registrar códigos, credenciales, cuentas, IPs ni contenido de datos privados.
