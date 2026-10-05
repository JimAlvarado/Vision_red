# Vision_red

Monitoreo de red Vision (MonitorRed.Panel, .NET 10 autocontenido).

## Flujo de trabajo

- **Desarrollo solo en G15** (local, con Codex). Ahí se modifica, se compila y se hace push.
- **El servidor no se modifica**: solo descarga lo que está en `main`.
- El servidor no tiene .NET SDK, por eso el repo lleva la publicación compilada en `outputs/MonitorRed.Panel`.

## Estructura

```
outputs/MonitorRed.Panel/   Publicación compilada que ejecuta el servicio "Vision"
src/                        (pendiente) Código fuente, se agrega desde G15
```

## Actualizar el servidor

1. `Detener servicio Vision.bat`
2. `git pull` en `C:\Proyectos\Vision_red`
3. `Iniciar servicio Vision.bat`

`outputs/MonitorRed.Panel/datos/` está fuera de git: es el estado en vivo del servidor (topología, eventos, incidentes, sesiones DPAPI) y nunca se sube ni se reemplaza con un pull.
