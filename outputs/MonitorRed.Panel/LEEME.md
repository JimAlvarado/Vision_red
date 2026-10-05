# Vision en Windows Server 2022

Paquete preparado en `work\tooling\servidor`. Incluye aplicación x64 con su runtime, soporte de servicio de Windows, scripts revisables e instantánea de mapa/historial. **Todavía no está instalado ni verificado en el servidor.** Los scripts no se han ejecutado en esta PC.

## Datos necesarios para instalar hoy

- IP privada fija del servidor y subred de origen de las PC/tablets que llegan por VPN. El servidor debe responder a los switches y recibir conexiones de esos clientes. La subred de clientes puede ser distinta de la interfaz privada del servidor si el enrutamiento VPN lo permite.
- Acceso administrativo al servidor y unidad D con `D:\Proyectos\Vision_red\outputs\MonitorRed.Panel`.
- Cuenta de Windows dedicada para ejecutar el servicio, con derecho Iniciar sesión como servicio. Su contraseña se introduce en Windows, no en el chat ni en archivos.
- Salida HTTPS del servidor hacia Microsoft para autorización y envío por Graph. No publicar Vision en Internet.
- Respaldo automático de la carpeta `datos` con la herramienta de respaldo del servidor. Conservar CSV, incidentes, cola y topología. Comprobar una restauración y definir retención con Sistemas.

## Instalación

1. Copiar el contenido de `Vision` del paquete a `D:\Proyectos\Vision_red\outputs\MonitorRed.Panel` en el servidor. No sobrescribir una instalación existente sin respaldarla.
2. Copiar `Transferencia\datos` a la carpeta `datos` dentro del panel. Esta instantánea contiene mapa, historial y configuración; los avisos y la detección vienen desactivados para validar sin duplicar correos de la PC. No copiar `email-session.dpapi`, `mobile-access.dpapi`, PID ni registros de proceso de la PC. Esos secretos no están en el paquete.
3. En PowerShell administrador, ejecutar `Configurar-Acceso-Privado.ps1 -IpServidor <IP privada real> -SubredClientes <subred VPN autorizada/CIDR>`. El script verifica que la IP exista en el servidor y limita el firewall a esa IP, puerto 5081, ejecutable y subred de origen. El editor 5080 sigue solo en localhost.
4. Ejecutar `Instalar-Servicio.ps1 -CuentaServicio (Get-Credential)`. Indicar la cuenta dedicada del servidor. El script da lectura/ejecución del programa y modificación de `datos`, registra Vision como servicio con inicio automático diferido y tres acciones de reinicio tras fallo. No cambia la VPN ni crea cuentas.
5. En el propio servidor, abrir `http://127.0.0.1:5080/` y autorizar el buzón desde Correo. La aplicación que corre bajo la cuenta del servicio guarda su propia autorización cifrada. Cambiar de máquina/cuenta exige autorización nueva; conservar identidad del servicio en reinicios y actualizaciones.
6. Desde Configuración → Celular, obtener la nueva dirección y código. El código se genera cifrado en el servidor; las sesiones de la PC no se trasladan. Probar PC/tablet conectadas a VPN y verificar 19 equipos actuales, estado e historial. La consulta por la red usa `http://<IP privada del servidor>:5081/`; la edición se realiza en el servidor, por ejemplo mediante acceso remoto corporativo.
7. Validar correo mediante comprobación de autorización y prueba controlada. Mantener avisos desactivados durante esta validación. No ejecutar dos instancias con avisos automáticos habilitados.

## Cambio definitivo

1. Acordar el momento de cambio. Detener Vision en la PC y conservar una copia final completa de sus datos.
2. Detener el servicio del servidor, actualizar allí topología, CSV, incidentes y cola desde esa copia final, sin reemplazar los archivos DPAPI creados en el servidor.
3. En el servidor, poner `incidentDetectionEnabled=true` en `datos\monitor-settings.json` y `automaticAlertsEnabled=true` en `datos\email-settings.json`, conservando los destinatarios autorizados.
4. Iniciar el servicio; verificar que Microsoft pueda autorizar/envíar, las IP respondan y el acceso VPN funcione. Probar una pérdida y recuperación controlada, confirmar correo, audio y ambos eventos.
5. Reiniciar Windows Server y comprobar que Vision, conectividad de red/VPN, acceso desde tablet y correo vuelvan sin abrir una sesión de escritorio. Esa prueba de arranque es necesaria antes de dar por terminado el traslado.

No borrar registros ni reenviar avisos aceptados para probar. La cola conserva deduplicación por incidente y tipo. Si aparece un bloqueo real de Control de aplicaciones, Sistemas debe revisar/aprobar el ejecutable según su política; no desactivar esa protección.

## Funcionamiento continuo y sonido

El servicio monitorea, registra CSV y envía correo sin navegador ni sesión abierta. El servidor no reproduce sonido en la sesión de servicio: el pitido se escucha en cada PC/tablet que mantiene Vision abierto, con audio permitido y volumen audible. El navegador móvil puede ser suspendido al bloquear la pantalla; el monitoreo y correo del servidor continúan.

En la PC actual, el aviso se dibuja antes de pedir el pitido a Windows. La solicitud está deduplicada contra el respaldo del motor; si no hay una página visible, el motor emite el pitido de respaldo después de siete segundos. En tablet, el audio se programa después de dibujar el aviso. No se garantiza coincidencia física exacta de pantalla y altavoz; se elimina la reproducción anticipada causada por ciclos distintos.

En pantalla completa aparecen las alertas roja y verde sobre el mapa. La roja requiere reconocimiento manual; la verde dura 15 segundos. Esc vuelve a la vista normal conservando zoom y posiciones.

## Verificación realizada aquí

- Aplicación y paquete Windows x64 compilados sin errores ni advertencias.
- Pruebas locales de aviso antes del sonido, una reproducción por evento, persistencia del rojo, cierre manual, verde temporal y ausencia de repetición al actualizar.
- Scripts de instalación/configuración analizados sintácticamente; aún no ejecutados ni probados en el servidor.
- Paquete revisado sin archivos DPAPI de esta PC. La autorización y el código se generan nuevamente en destino.

Referencia de implementación: [Servicio de Windows para ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0) y [cifrado ligado a CurrentUser](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope).
