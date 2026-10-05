# Vision_red

Monitoreo de red Vision (MonitorRed.Panel, .NET 10 autocontenido).

Antes de trabajar, consultar [coordinacion/LEEME.md](coordinacion/LEEME.md), [coordinacion/G15.md](coordinacion/G15.md) y [coordinacion/SERVIDOR.md](coordinacion/SERVIDOR.md). Estos archivos contienen el estado compartido, las verificaciones y el siguiente paso.

## Flujo de trabajo

- **Desarrollo solo en G15** (local, con Codex). Ahí se modifica, se compila y se hace push.
- **El servidor no se modifica**: solo descarga lo que está en `main`.
- El servidor no tiene .NET SDK, por eso el repo lleva la publicación compilada en `outputs/MonitorRed.Panel`.

## Estructura

```
outputs/MonitorRed.Panel/   Publicación compilada que ejecuta el servicio "Vision"
outputs/MonitorRed.Panel/*.cs y .csproj   Código fuente del panel y monitor integrado
outputs/MonitorRed/                       Código fuente del motor independiente
work/tooling/Publicar-Vision.ps1          Compilación y publicación desde G15
```

## Actualizar el servidor

1. `Detener servicio Vision.bat`
2. `git pull` en `C:\Proyectos\Vision_red`
3. `Iniciar servicio Vision.bat`

`outputs/MonitorRed.Panel/datos/` está fuera de git: es el estado en vivo del servidor (topología, eventos, incidentes, sesiones DPAPI) y nunca se sube ni se reemplaza con un pull.

## Preparar una actualización en G15

El código fuente conserva las rutas establecidas para Vision; no se crea una segunda carpeta src. Ejecutar `work/tooling/Publicar-Vision.ps1` desde PowerShell con .NET SDK 10 disponible, o con el SDK local de `work/tooling/dotnet`. La herramienta compila primero en una carpeta temporal dentro del proyecto, comprueba que no incluya datos privados y copia la publicación a `outputs/MonitorRed.Panel`, sin reemplazar `datos`. El inventario privado del motor independiente tampoco se versiona; su uso requiere el archivo local `inventario.json`.

Revisar los cambios y avisar al usuario antes de cada push. Versionar código y publicación compilada juntos. El servidor no compila: detener el servicio, comprobar que esté detenido, realizar `git pull --ff-only`, iniciarlo y comprobar estado, mapa, acceso móvil y correo. Si el pull falla, resolverlo antes de considerar aplicada la actualización.

Los archivos comprimidos `.gz` y `.br` se regeneran durante la publicación; no deben conservar contenido de una versión anterior. El script no inicia el monitor de G15, no controla servicios y no hace commit ni push.

## Avisos privados al autor

Tras detener el servicio y actualizar el servidor, ejecutar `work/tooling/Configurar-Avisos-Autor.ps1 -CorreoAutor <correo indicado por el autor>` antes de iniciar Vision. La configuración se guarda en `datos/owner-notifications.json`, fuera de Git, con respaldo previo. También se incluye el autor en los destinatarios de las alertas de red y se conserva su dirección al editar la lista.

Los avisos de nuevos destinatarios e inicios de sesión válidos en la consulta van exclusivamente al autor, con cola persistente. Su canal es independiente del ajuste de alertas de red, pero usa la misma autorización del buzón. Una sesión ya activa y las consultas periódicas no generan avisos nuevos. La IP y el navegador declarados no identifican con certeza a una persona ni el teléfono. No se envían códigos ni cookies. Guardar la misma lista o retirar un destinatario no genera un aviso de incorporación.

En G15, `Probar-Avisos-Autor.ps1 -TestRoot <carpeta de prueba>` requiere PowerShell 7 y una copia sin autorización de correo ni alertas de red; usa correos ficticios y restaura sus ajustes. El proyecto `work/tooling/test-owner/TestOwner.csproj` verifica el canal con un transporte simulado.
