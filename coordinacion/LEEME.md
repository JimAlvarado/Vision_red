# Coordinación de Vision: Codex y Claude

Esta carpeta es el punto de intercambio acordado por el usuario. Se comparte mediante GitHub; cada asistente la consulta y actualiza cuando el usuario le encarga trabajo. No hay comunicación automática entre chats.

## Lectura inicial

1. Leer este archivo, G15.md y SERVIDOR.md.
2. Comprobar rama, cambios locales y versión remota antes de sincronizar. No descartar trabajo ajeno. Si hay cambios concurrentes, conservar ambos y resolver el conflicto.
3. Consultar solamente los archivos del proyecto necesarios para la tarea. Estos resúmenes evitan explorar todo el proyecto, pero no sustituyen la revisión ni las pruebas de los archivos modificados.

## Responsabilidades y rutas

- Codex desarrolla y compila en G15: `D:\Proyectos\Vision_red`. Panel en `outputs/MonitorRed.Panel`, motor en `outputs/MonitorRed`, herramientas en `work/tooling`.
- Entorno de prueba autorizado en G15: `D:\Proyectos\Vision_red server`. Es una copia local de prueba, no el servidor real. No enviar su rama ni sus datos.
- Claude aplica y verifica en Windows Server: `C:\Proyectos\Vision_red`. Servicio de Windows: `Vision`. Ejecutable previsto en `outputs/MonitorRed.Panel`; comprobar la ruta registrada antes de operaciones de instalación o limpieza.
- Código fuente y publicación compilada se versionan juntos. El servidor utiliza la publicación autocontenida; no necesita compilar.

## Actualizar el servidor

Leer G15.md para conocer el commit de aplicación previsto. Detener Vision y confirmar el estado detenido; realizar `git pull --ff-only` con la carpeta de trabajo limpia; iniciar Vision y comprobar servicio, mapa, acceso móvil y correo cuando la prueba esté autorizada. Conservar los datos locales. Si el pull o la verificación falla, registrar el fallo y la versión efectiva, sin declarar aplicada la actualización.

Un commit que solo modifica coordinación no exige reiniciar el servicio. Distinguir el HEAD del repositorio del commit de aplicación que ejecuta el servidor.

## Qué escribe cada asistente

- Codex mantiene G15.md: último cambio preparado, commit de aplicación, pruebas, entrega y pendientes.
- Claude mantiene SERVIDOR.md: fecha de aplicación, commit de aplicación ejecutado, HEAD del repositorio, verificaciones y problemas. Su registro debe basarse en comprobaciones del servidor, no en inferencias.
- Conservar la estructura breve y reemplazar el resumen anterior. Incluir como máximo tres entradas recientes por archivo. Si falta un dato, escribir "pendiente de verificar".
- Ambos deben avisar al usuario antes de cada push. Publicar el estado al finalizar el trabajo autorizado; no enviar mensajes a otros chats sin autorización del usuario.

## Información que queda fuera de Git

No incluir contraseñas, códigos de acceso, tokens, cookies, archivos DPAPI, configuración privada de correo, destinatarios reales, inventarios privados, respaldos ni contenido de `datos`. Registrar resultados resumidos sin secretos. Los datos operativos permanecen en cada máquina y no se sustituyen con pull.
