# Coordinación de Vision: Codex G15 y Codex Server

Esta carpeta es el punto de intercambio acordado por el usuario. Se comparte mediante GitHub; cada asistente la consulta y actualiza cuando el usuario le encarga trabajo. No hay comunicación automática entre chats.

## Regla vigente — 7 de octubre de 2026

El proyecto se modifica y se prepara en G15; la ejecución operativa se realiza en Server. Toda entrega y todo reporte entre ambos pasan por Git, en la rama main del repositorio https://github.com/JimAlvarado/Vision_red.git. Codex G15 mantiene G15.md y Codex Server mantiene SERVIDOR.md. Esta regla sustituye las referencias anteriores a Claude y a desarrollo en el servidor; las intervenciones históricas se conservan como registro.

## Lectura inicial

1. Leer este archivo, G15.md y SERVIDOR.md.
2. Comprobar rama, cambios locales y versión remota antes de sincronizar. No descartar trabajo ajeno. Si hay cambios concurrentes, conservar ambos y resolver el conflicto.
3. Consultar solamente los archivos del proyecto necesarios para la tarea. Estos resúmenes evitan explorar todo el proyecto, pero no sustituyen la revisión ni las pruebas de los archivos modificados.

## Responsabilidades y rutas

- Codex desarrolla y compila en G15: `D:\Proyectos\Vision_red`. Panel en `outputs/MonitorRed.Panel`, motor en `outputs/MonitorRed`, herramientas en `work/tooling`.
- Codex Server aplica y verifica en Windows Server: `C:\Proyectos\Vision_red`. Servicio de Windows: `Vision`. Ejecutable previsto en `outputs/MonitorRed.Panel`; comprobar la ruta registrada antes de operaciones de instalación o limpieza.
- Código fuente y publicación compilada se versionan juntos. El servidor utiliza la publicación autocontenida; no necesita compilar.

## Actualizar el servidor

Leer G15.md para conocer el commit de aplicación previsto. Detener Vision y confirmar el estado detenido; realizar `git pull --ff-only` con la carpeta de trabajo limpia; iniciar Vision y comprobar servicio, mapa, acceso móvil y correo cuando la prueba esté autorizada. Conservar los datos locales. Si el pull o la verificación falla, registrar el fallo y la versión efectiva, sin declarar aplicada la actualización.

Un commit que solo modifica coordinación no exige reiniciar el servicio. Distinguir el HEAD del repositorio del commit de aplicación que ejecuta el servidor.

## Qué escribe cada asistente

- Codex mantiene G15.md: último cambio preparado, commit de aplicación, pruebas, entrega y pendientes.
- Codex Server mantiene SERVIDOR.md: fecha de aplicación, commit de aplicación ejecutado, HEAD del repositorio, verificaciones y problemas. Su registro debe basarse en comprobaciones del servidor, no en inferencias.
- Conservar la estructura breve y reemplazar el resumen anterior. Incluir como máximo tres entradas recientes por archivo. Si falta un dato, escribir "pendiente de verificar".
- Ambos deben avisar al usuario antes de cada push. Publicar el estado al finalizar el trabajo autorizado; no enviar mensajes a otros chats sin autorización del usuario.

## Protocolo común desde el 5 de octubre de 2026

Acordado por el usuario después de que Codex y Claude modificaran el mismo código al mismo tiempo. Aplica a ambos por igual.

### Al empezar cualquier tarea

1. `git fetch` y `git status`. Si hay commits remotos, `git pull --ff-only` (en el servidor, con Vision detenido si el pull trae binarios).
2. Leer este archivo, G15.md y SERVIDOR.md, empezando por la línea **En curso** de cada uno.

### Aviso de trabajo en curso (evita dos versiones del mismo código)

- Antes de modificar código fuente, publicación compilada o scripts, escribir en el archivo propio (G15.md o SERVIDOR.md), al inicio: `En curso: <tema> · archivos previstos · desde <fecha y hora>`. Hacer commit y push de ese aviso **antes** de empezar, avisando al usuario.
- Si el otro tiene un **En curso** abierto, no tocar esos archivos: preguntar al usuario. Coordinación sí puede editarse.
- Al entregar, sustituir la línea por `En curso: nada`.

### Si el push es rechazado

1. `git fetch` y `git merge origin/main`. No reescribir commits publicados (`rebase`, `--force`).
2. Resolver el código fuente conservando ambos cambios.
3. Nunca elegir el `.dll`, `.exe`, `.pdb`, `staticwebassets.endpoints.json`, `.gz` ni `.br` de un lado: regenerar la publicación desde el código mezclado.
4. Ejecutar las pruebas obligatorias, hacer commit de la mezcla, registrar en el archivo propio y hacer push.

### Compilación idéntica en G15 y en el servidor

- SDK .NET **10.0.401** (runtime 10.0.12). En el servidor está en `work/tooling/dotnet` (fuera de Git). Si se cambia de SDK o de runtime, anotarlo en el archivo propio.
- Compilar desde rutas cortas: más de 260 caracteres hace fallar la compresión de `wwwroot`.
- Después de publicar, comprobar que solo cambian los archivos de la aplicación (`MonitorRed.Panel.dll/.exe/.pdb`, `staticwebassets.endpoints.json`) y los comprimidos de los archivos web modificados. Si cambian DLL del runtime, revisar la versión del SDK antes de subir.

### Pruebas obligatorias antes de subir código

- `work/tooling/test-owner` y `work/tooling/test-volumen` (`dotnet run -c Release`): ambas sin fallos. Usan transporte ficticio, sin correo real.
- Los `.ps1` con caracteres no ASCII se guardan en UTF-8 **con BOM** y se comprueban con Windows PowerShell 5.1.

### Servidor

- Codex Server recibe las entregas por Git, las ejecuta y verifica, y registra los resultados en SERVIDOR.md. Los cambios de código y scripts se realizan en G15 y se entregan por Git.
- Los archivos que crea la aplicación en `datos` (por ejemplo `correo-limite.json` y `mobile-sessions.dpapi`) forman parte de los respaldos y nunca se suben a Git.

## Información que queda fuera de Git

No incluir contraseñas, códigos de acceso, tokens, cookies, archivos DPAPI, configuración privada de correo, destinatarios reales, inventarios privados, respaldos ni contenido de `datos`. Registrar resultados resumidos sin secretos. Los datos operativos permanecen en cada máquina y no se sustituyen con pull.
