# Vision_red

Monitoreo de red Vision (MonitorRed.Panel, .NET 10 autocontenido).

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
