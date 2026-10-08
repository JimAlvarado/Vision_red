# Diagnóstico solicitado: PowerShell repetido y pérdidas de alcance en Server

Fecha: 8 de octubre de 2026, Ciudad de México. Preparado en G15; ejecución y evidencia deben obtenerse en Server. Push de este documento autorizado explícitamente por el usuario el 8 de octubre de 2026.

## Pregunta del usuario

Solo en Server aparecen y desaparecen ventanas PowerShell continuamente. Investigar quién las inicia y si están relacionadas con las pérdidas de comunicación hacia los equipos supervisados. No atribuir causalidad por coincidencia visual.

## Evidencia existente y límites

- El monitoreo usa Ping.SendPingAsync en .NET, no ejecuta PowerShell/CMD por ping. Último reporte remoto disponible: SERVIDOR.md del 7/oct, instalación 10; no hay evidencia actual del 8/oct ni reporte de instalación de la entrega de rangos.
- La revisión del 7/oct registró 100 pérdidas de referencias desde el arranque del 5/oct con el mismo proceso de Vision activo. Es evidencia histórica de pérdida de alcance mientras el servicio seguía funcionando, no un diagnóstico de la red actual.
- MonitoringWorker publica no-reachability y todos los estados unreachable si ninguna referencia responde; algún otro equipo puede haber respondido. Una sola ronda basta para esta condición visual; el incidente de red requiere tres rondas negativas. Distinguir mapa, incidente confirmado y respuesta directa independiente.
- Lo observado en G15 (consolas de Codex/Visual Studio Code) no demuestra quién crea ventanas en Server.

## Comprobación en Server, de lectura

1. Registrar versión efectiva, hora UTC y local, PID/hora de inicio del servicio y estado de Vision. No detenerlo, reiniciarlo, actualizarlo ni cambiar configuración de monitoreo, VPN, firewall, alertas o permisos para este diagnóstico.
2. Capturar eventos de creación de powershell.exe, pwsh.exe y cmd.exe con Win32_ProcessStartTrace durante una observación acotada, inicialmente 30 minutos. Guardar hora, PID, PID padre, nombre/ruta del iniciador y sesión. Resolver padre y ancestros mientras existan, verificando hora de creación para evitar confundir PID reutilizado. Si la línea de ejecución se alcanza a consultar, extraer solamente nombre/ruta del script o ejecutable; no guardar argumentos completos, tokens ni credenciales. Distinguir el proceso del propio diagnóstico.
3. Revisar tareas programadas y registros operativos de Task Scheduler en las horas capturadas; identificar nombre de tarea, acción y frecuencia cuando el origen sea una tarea. No habilitar auditorías, crear tareas persistentes ni cambiar políticas sin un encargo adicional. Si no hay permisos para la captura, registrar el límite; no inferir el iniciador por ausencia de resultados.
4. Usar una sola sesión de diagnóstico, oculta si se lanza en segundo plano, para evitar crear nuevas ventanas por muestra. Limitarla a 30 minutos y liberar sus suscripciones al terminar. Guardar evidencia privada dentro de work/tooling/diagnostico-red-<fecha-hora>, excluida por las reglas Git; nunca publicar registros crudos con direcciones o rutas privadas.
5. En paralelo, comprobar las dos referencias configuradas, un equipo de control distinto de ellas y el siguiente salto relevante si responde normalmente a ICMP. Obtener direcciones de los datos reales de Server, no de ejemplos ni de la copia de G15. Comprobar referencia/equipo cada 5 segundos con una solicitud por destino y espera de 1,5 segundos, independientemente de Vision; no abrir una consola por ping. Registrar éxito, latencia/error y hora real. Si no ocurre una pérdida durante la ventana, informar que no se reprodujo; no declarar reparado el problema.
6. Registrar condición de /api/status, PID del servicio, estado de adaptadores y ruta elegida hacia los destinos. Guardar rutas/adaptadores al inicio y al cambiar o producirse una pérdida; no atribuir una ruta ausente a un fallo si la consulta fue rechazada. Correlacionar eventos de adaptador/VPN/Windows y horarios del diario de Vision. Un gateway que no contesta habitualmente a ICMP no es un control válido de caída.
7. Si se conoce un puerto de servicio válido en el equipo de control, verificarlo ocasionalmente durante la pérdida; no escanear puertos. ICMP y disponibilidad del servicio son comprobaciones distintas. Cuando exista otra máquina en la misma red, comparar el mismo destino/hora con ella para localizar si la pérdida es específica de Server.

## Cómo interpretar resultados

- Consolas con padre Codex y sin cambios de ruta/adaptador ni pérdidas independientes: origen de ventanas identificado; no evidencia de que causen el fallo de red.
- Ambas referencias fallan pero el equipo de control responde: investigar referencias/filtrado ICMP y la condición global de Vision; no reportar 19 equipos caídos.
- Referencias y equipo de control fallan; cambia ruta/VPN/adaptador: evidencia para investigar la conectividad de Server. La coincidencia orienta el diagnóstico, no demuestra por sí sola causa.
- ICMP falla pero el servicio conocido responde: revisar política/limitación de ICMP, latencia y adecuación de las referencias antes de concluir pérdida total.
- Pruebas independientes responden mientras Vision falla en horas comparables: investigar diferencias de timeout, destino, momento de muestreo o errores internos de sonda. Un ping aislado posterior no refuta una pérdida anterior.
- Un script inicia inmediatamente antes de las pérdidas y su contenido cambia VPN/rutas/adaptador: revisar su función y correlación repetida antes de proponer desactivarlo. No detenerlo automáticamente.

## Entrega del diagnóstico

Actualizar SERVIDOR.md con hora/ventana, origen comprobado de consolas, número de pérdidas independientes, comportamiento de referencias/control, cambios de ruta/adaptador, estado/PID de Vision y conclusión con evidencia o pendiente de verificar. Usar etiquetas referencia A/B y equipo de control, sin direcciones, usuarios ni argumentos privados.

Preguntar al usuario y esperar aprobación explícita antes de cada push del reporte. No modificar la aplicación ni reducir la sensibilidad para ocultar el síntoma durante esta investigación.

Documentación de captura de procesos: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/krnlprov/win32-processstarttrace.
