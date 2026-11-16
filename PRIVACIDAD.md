# Política de privacidad de LIGERO

*Última actualización: 16 de noviembre de 2026*

LIGERO es un optimizador para Windows que funciona por completo en tu equipo. Esta política explica qué información usa y qué hace con ella.

## Resumen

- **LIGERO no recopila, no envía y no vende ningún dato personal.**
- No tiene cuentas de usuario, publicidad, análisis de uso ni telemetría.
- Todo lo que guarda se queda en tu equipo y puedes borrarlo cuando quieras.

## Qué información lee en tu equipo

Para hacer su trabajo, LIGERO consulta información de tu propio equipo, **sin enviarla a ningún sitio**:

- Uso de memoria, espacio en disco y programas en ejecución.
- Programas instalados y programas que arrancan con Windows.
- Archivos temporales, cachés y, solo cuando tú lo pides, archivos de tus carpetas personales (para buscar archivos grandes, duplicados o descargas).
- Estado del antivirus, del cortafuegos y de las actualizaciones de Windows.
- Características del equipo (procesador, memoria, disco y tarjeta gráfica), para ajustar la aplicación a su gama.

## Qué guarda y dónde

LIGERO guarda en la carpeta `%LOCALAPPDATA%\Ligero` de tu usuario:

- Tus preferencias de aspecto y las características del equipo detectadas (`aspecto.txt`).
- El historial de acciones realizadas, para poder deshacerlas (`historial.txt`).
- Los archivos retirados durante las limpiezas, que se guardan temporalmente para poder recuperarlos (carpeta `Cuarentena`). Los archivos temporales se eliminan definitivamente a los 7 días; los archivos personales retirados, solo cuando tú lo decides.
- Copias de las claves del registro que se borran al limpiar restos de programas, para poder restaurarlas (carpeta `Registro`).

Ninguno de estos datos sale de tu equipo. Puedes borrarlos eliminando esa carpeta. Al desinstalar LIGERO esa carpeta puede quedar en el equipo; puedes borrarla a mano.

## Conexiones a internet

LIGERO solo se conecta a la red cuando pulsas **«Diagnosticar la conexión»** en la sección Red. En ese momento:

- Envía una prueba de conexión (ping) a tu router, a `1.1.1.1` (Cloudflare) y, si no responde, a `8.8.8.8` (Google).
- Busca la dirección de `www.microsoft.com` para comprobar que los nombres de las webs funcionan.

Estas pruebas no envían ninguna información sobre ti ni sobre tu equipo. Como cualquier conexión a internet, esos servidores reciben tu dirección IP pública.

Además, algunos botones abren herramientas o páginas de configuración de Windows. Lo que hagas en ellas se rige por la política de privacidad de Microsoft.

## Niños

LIGERO no recopila datos de nadie, incluidos menores de edad.

## Cambios en esta política

Si esta política cambia, la versión nueva se publicará en este mismo archivo con su fecha de actualización.

## Contacto

Para cualquier pregunta sobre privacidad, abre una incidencia en el repositorio del proyecto: https://github.com/juanlobatonmoreno33-web/LIGERO/issues

---

## Privacy policy (English summary)

LIGERO runs entirely on your device. It does **not** collect, transmit or sell any personal data, and it has no accounts, ads, analytics or telemetry. It reads local system information (memory, disk, installed programs, startup items, security status, hardware specs) only to perform the optimizations you choose, and stores its settings, action history and recoverable cleaned files locally in `%LOCALAPPDATA%\Ligero`. The only network activity happens when you click "Diagnose connection": a ping to your router, to `1.1.1.1` (Cloudflare) or `8.8.8.8` (Google), and a DNS lookup of `www.microsoft.com`. No information about you or your device is sent. Contact: https://github.com/juanlobatonmoreno33-web/LIGERO/issues
