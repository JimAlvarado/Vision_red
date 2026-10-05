# Vision — editor visual de topología

Primera versión funcional del editor, inspirada en la referencia proporcionada. Nombre del proyecto: Vision.

## Abrir
Durante la sesión de desarrollo: http://127.0.0.1:5080

Para iniciarlo de nuevo, haz doble clic en `D:\Proyectos\Vision_red\Iniciar Vision.cmd`. El iniciador compila, reinicia el monitor y abre el navegador. Conservar `work\tooling`, que contiene el SDK instalado para este proyecto. El monitor sigue activo con el navegador cerrado, pero todavía no está instalado como servicio de Windows.

## Uso
- Arrastra la tarjeta de cualquier equipo para moverla. También se puede seleccionar con Tab y mover con las flechas.
- Las conexiones salen por la derecha y entran por la izquierda. Pulsa el punto derecho del origen y después el punto izquierdo del destino.
- También puedes seleccionar un equipo y usar «Conectar desde» en su ficha para escoger el switch anterior.
- Puedes crear varias conexiones y representar enlaces redundantes; no se impone una jerarquía artificial.
- Elimina una conexión haciendo clic en su línea y confirmando, o desde las conexiones entrantes de la ficha.
- Arrastra el fondo para desplazarte. La rueda y los controles +/− cambian el zoom. «Encuadrar» muestra todos los equipos.
- «Agregar equipo» admite switches, cámaras IP, servidores, routers, impresoras y otros dispositivos IP. Nombre, IP y criticidad son editables.
- El buscador resalta coincidencias; Enter centra la primera. En ventanas pequeñas, utiliza el lienzo y «Encuadrar».
- Los cambios se guardan automáticamente en esta PC. Comprueba el indicador «Guardado en esta PC» antes de cerrar. Si falla, conserva la pestaña y pulsa el indicador para reintentar; en conflicto con otra ventana, exporta tus cambios antes de recargar.
- «Exportar» descarga una copia JSON. La restauración por interfaz será una función posterior.

## Datos y alcance
El inventario visual inicial incluye 23 switches críticos, sin los dos registros excluidos de 172.20.30.18. Se conservan las notas originales. No se inventaron conexiones: el usuario define los enlaces físicos reales.
Los datos, posiciones y conexiones se guardan en datos/topologia.json mediante escritura temporal y reemplazo, con revisión para evitar sobrescribir cambios de otra ventana. Respaldar ese archivo.

Vision ahora supervisa las IP del inventario visual cada 10 segundos por ICMP y actualiza el mapa cada 5 segundos. Las altas nuevas entran en la siguiente ronda sin reiniciar. El motor de consola anterior queda como prototipo independiente. Las alertas confirmadas de equipos críticos se guardan en una cola local; todavía no hay envío de correo ni WhatsApp, descubrimiento automático ni acceso administrativo a switches.
Una respuesta ICMP confirma que una IP responde desde esta PC, pero no identifica el equipo ni descubre la ruta física. Las conexiones del mapa son declaradas por el usuario. Si una IP responde mientras todos los enlaces anteriores dibujados no responden, Vision marca la tarjeta «IP responde · revisar ruta» y mantiene en rojo las ramas afectadas. Con enlaces redundantes, basta una ruta anterior que responda para evitar ese aviso. Hasta definir el origen real del monitoreo y verificar los enlaces físicos, la vista no puede afirmar continuidad de extremo a extremo.
Mientras el equipo de desarrollo no tenga acceso a la red de switches, `datos\monitor-settings.json` mantiene `incidentDetectionEnabled` en `false`: se ven las respuestas ICMP, pero no se abren incidentes ni se encolan avisos. No activar incidentes hasta comprobar desde el servidor la conectividad real y las IP de los equipos.
El panel escucha únicamente en esta PC. Autenticación, HTTPS, publicación en la red y servicio de Windows pertenecen a la preparación del servidor.

## Verificación
- Compilación .NET 10 sin errores ni advertencias.
- Alta de cámara y creación de enlace mediante el navegador.
- Persistencia después de recargar y guardado de movimiento por arrastre.
- Rechazo de IP duplicadas (HTTP 400) y revisiones desactualizadas (HTTP 409).
- Revisión de interfaz en ventana estrecha y escritorio de 1440 × 900.
- Se retiró el dispositivo temporal de pruebas y se restablecieron las posiciones iniciales; se entregan 23 switches y cero conexiones inventadas.


## Diseño Vision
Se aplicó la paleta proporcionada: fondo #08111f, lateral #0b1626, paneles #0e1b2c, superficies #132337, borde #1d3047 y textos #e8eef8, #8fa0b7 y #7f91a9. Acentos: menta #58dcb8, verde #4ade80, azul #4eb7f5, ámbar #f4b942, rojo #fb7185 y turquesa #22c7b5.

El mapa tiene prioridad: cabecera y contadores compactos, detalle flotante solo al seleccionar un equipo y botón «Ampliar mapa» para ocultar el resto de los controles superiores.

«Ver animaciones» abre una demostración aislada con equipos ficticios: pulso y flujo menta, pendiente ámbar, alerta roja suave sin flujo y gris estático sin datos. Incluye un ciclo reproducible y respeta la preferencia de movimiento reducido del sistema. Esta demostración no escribe en el inventario.

Los puntos animados y colores ya responden a estados reales cuando el monitor confirma disponibilidad o caída. Cuando un extremo no responde, la rama se vuelve roja y un punto rojo la recorre. Si esta PC no tiene acceso a ninguna IP, la rama también se muestra roja, pero el panel lo identifica como falta de acceso desde esta PC, sin abrir incidentes individuales. La vista previa usa cuatro tarjetas y dos enlaces temporales, con aviso visible, y no modifica los datos guardados.
Las lecturas periódicas actualizan el estado sin reconstruir las ramas: los puntos conservan su movimiento entre una comprobación y la siguiente. En una rama sin comunicación, el punto rojo llega solo hasta la mitad, se dispersa en cuatro destellos y vuelve a salir del origen. Al mover un equipo, la ruta se ajusta a su nueva posición.


## Vista previa animada en el mapa
Al abrir Vision, la demostración aparece solo hasta la primera ronda real. Después se apaga automáticamente. Puedes volver a mostrarla con «Mostrar vista previa». La banda ámbar siempre indica que los estados y enlaces son simulados.




## Pantalla completa y puntos animados
El botón «Pantalla completa» maximiza el mapa en el navegador; presiona Esc para salir. Si el navegador no permite ese modo, se amplía dentro de la ventana. Los enlaces son líneas fijas; los puntos verdes recorren conexiones disponibles y los puntos rojos señalan cortes. Los puntos aparecen en los enlaces reales que el usuario haya dibujado cuando hay estados confirmados; también se pueden revisar en la vista previa.



## Monitoreo actual
El panel hace ping ICMP con límite de ocho comprobaciones simultáneas y espera máxima de 1.5 segundos. Confirma caída tras tres fallos consecutivos y recuperación tras dos respuestas. Si ninguna IP responde desde esta PC, muestra «Sin acceso a la red» y no genera caídas individuales. El archivo datos/eventos-monitor-AAAA-MM-DD.jsonl guarda cambios de estado; la API GET /api/status entrega la última ronda. La ejecución continúa mientras el proceso de Vision esté activo, incluso con el navegador cerrado. Aún no está instalada como servicio de Windows.



## Incidentes
Vision guarda incidentes confirmados en datos/incidentes.json y los muestra en el botón «Incidentes». Una caída repetida no crea duplicados. Una falta de acceso general desde esta PC no abre incidentes individuales ni cierra uno previamente abierto. La recuperación confirmada cierra el incidente. Se comprobó apertura, deduplicación, persistencia tras reinicio y recuperación con un equipo de prueba aislado del inventario real.

## Alertas pendientes
Cada caída o recuperación confirmada de un equipo crítico crea una alerta de correo en `datos\alertas-pendientes.json`. La cola evita duplicados y recupera alertas pendientes si Vision se interrumpe entre guardar el incidente y guardarlas. Se puede consultar localmente en `http://127.0.0.1:5080/api/notifications`. Todas permanecen en estado `awaiting-configuration`: Vision todavía no intenta enviarlas.

`notificaciones.example.json` propone `vision-alertas@arzyz.com` como dirección exclusiva; es una plantilla, no un buzón creado. Para activar el correo hace falta conocer el proveedor que administra el dominio, crear el buzón y definir destinatarios. Las credenciales no deben ponerse en la plantilla ni enviarse por chat.

