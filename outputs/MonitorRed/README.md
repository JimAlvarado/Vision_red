# Monitor de red — primer incremento

Motor de consola en C#/.NET 10 con inventario de 23 switches Cisco críticos. Se excluyeron los dos registros asociados a 172.20.30.18 por indicación del usuario.

## Funciones disponibles
- Ping cada 10 segundos con espera de 1.5 segundos y hasta 8 comprobaciones simultáneas.
- Caída tras 3 fallos consecutivos y recuperación tras 2 respuestas consecutivas.
- Estado desconocido al iniciar; errores locales diferenciados de falta de respuesta.
- Registro diario de cambios en JSONL y estado más reciente en JSON con fecha UTC.
- Validación del inventario y pruebas internas de la lógica de estados.

## Ejecutar desde la carpeta de origen
```powershell
. ./work/tooling/activar-desarrollo.ps1
dotnet run --project outputs/MonitorRed --configuration Release
```
Detener con Ctrl+C. Para una sola ronda, agregar `-- --once`. Para validar configuración, `-- --validate`. Para comprobar la lógica, `-- --self-test`.

La configuración original está en inventario.json. La compilación copia el archivo a la carpeta de ejecución; ejecutar sin --no-build después de editarlo.
Los resultados se guardan en bin/Release/net10.0/datos. La fecha checkedAtUtc permite determinar si el archivo está desactualizado: un archivo existente no implica que el monitor continúe ejecutándose.

## Verificación realizada
- Compilación Release sin errores ni advertencias.
- Ocho comprobaciones de estados satisfactorias, incluyendo recuperación, fallos alternados, errores de comprobación y control de incidentes duplicados.
- Inventario válido: 23 IP únicas y todos los equipos críticos.
- Dos rondas independientes de conectividad desde esta PC, incluyendo una fuera del entorno restringido: 23 tiempos de espera agotados en cada una. Esto no demuestra que los switches estén apagados; falta confirmar acceso a la red/VPN y permisos de ICMP. No se declaró una caída confirmada con estas rondas independientes.

## Límites de este incremento
Es un prototipo de consola, todavía no un servicio de Windows ni una aplicación lista para producción. No está ejecutándose en segundo plano al entregar este incremento. No envía correo ni WhatsApp, no incluye panel y no intenta acceso administrativo a los switches.
Los eventos se conservan en disco, pero los contadores y el estado de incidentes se reinician al abrir el programa. La persistencia de incidentes entre reinicios, retención automática, credenciales, notificaciones y servicio se implementarán en las fases siguientes.
