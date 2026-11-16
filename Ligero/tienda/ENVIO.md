# Textos para enviar LIGERO a la Microsoft Store

Copia cada bloque en el campo de Partner Center que se indica. Los textos para revisores de Microsoft van en inglés (es el idioma que usan en la certificación); la ficha de la Store va en español.

Los límites de caracteres que se mencionan son orientativos: Partner Center te avisará si alguno se pasa.

---

## 1. Paquete

**Dónde:** *Envío → Paquetes*

Sube `Ligero/tienda/salida/Ligero-1.0.0.0.msix` (se genera con `empaquetar.ps1`). En cada envío nuevo hay que subir la versión en `identidad.txt` (por ejemplo `1.0.1.0`) y volver a generar el paquete.

---

## 2. Justificación de los permisos especiales

**Dónde:** *Envío → Opciones de envío → Capacidades restringidas* (aparece una caja de texto por cada capacidad que lo necesita).

### `runFullTrust`

> LIGERO is a classic Win32 desktop application (Windows Forms, .NET Framework 4) packaged as MSIX. It requires full trust to read system information (memory, disk usage, running processes, installed programs, startup items) and to apply the optimizations that the user explicitly selects.

### `allowElevation`

> Some optional optimizations change machine-wide Windows settings that require administrator rights: disabling the SysMain, Windows Search indexer and DiagTrack services, turning hibernation off, cleaning Windows Update download caches and creating a system restore point before an uninstall. LIGERO always runs as a standard user. It only asks for elevation when the user presses the "Open as administrator" button or chooses one of these settings, and the standard Windows UAC prompt is shown every time. Every change is recorded in the app's history and can be undone from the "Undo" page.

### `unvirtualizedResources`

> LIGERO's purpose is to change real Windows settings and clean real user files, and to be able to undo those changes later. With registry and file-system write virtualization enabled, its changes to HKCU (for example visual effects, Game Bar recording, Store suggestions and startup-app approval in HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved) would be written to a private hive that Windows never reads, so they would have no effect. In the same way, files restored by the Undo feature into %LOCALAPPDATA% (for example the user's Temp folder and browser caches) would be redirected to a private location and would not be visible to the apps that own them. For this reason the package declares desktop6:RegistryWriteVirtualization and desktop6:FileSystemWriteVirtualization as "disabled". The app only writes to these locations when the user explicitly triggers an action, every change is logged, and every change can be reverted from the app.

---

## 3. Notas para los revisores

**Dónde:** *Envío → Opciones de envío → Notas para la certificación*

> LIGERO is a free, open-source Windows optimizer for low-end PCs (source code: https://github.com/juanlobatonmoreno33-web/LIGERO). It has no accounts, ads, in-app purchases or telemetry, and it does not need any test credentials.
>
> Nothing is changed until the user presses a button, and every action explains what it does before running. File cleanups do not delete files immediately: files are moved to a local "safety zone" and can be restored from the "Undo" page (temporary files are permanently removed after 7 days; personal files only when the user decides). Registry keys removed as uninstall leftovers are exported first and can be restored.
>
> Suggested test path: open the app → "Chequeo" → "Analizar ahora" (read-only health check) → "Acelerar" → apply a recommended setting → "Deshacer" → undo it. Settings marked "Administrador" require the "Abrir como administrador" button (UAC prompt). The "Red" page performs a ping to the router, 1.1.1.1 / 8.8.8.8 and a DNS lookup of www.microsoft.com only when the user presses "Diagnosticar la conexión".
>
> On first launch the app detects the hardware tier and adjusts its own appearance (animations and transparency) only; it never changes Windows settings without the user's confirmation.

---

## 4. Ficha de la Store (español)

**Dónde:** *Envío → Descripciones de la Store → Español*

### Descripción

> LIGERO hace que tu equipo vaya más fluido sin trucos ni sustos. Está pensado para portátiles y ordenadores con pocos recursos, y te explica cada cosa antes de hacerla.
>
> **Todo se puede deshacer.** Los archivos que limpia no se borran de golpe: pasan antes por una zona de seguridad de la que puedes recuperarlos. Cada ajuste de Windows que cambia se puede volver a dejar como estaba con un clic.
>
> **Sabe qué es seguro y qué no.** Antes de desinstalar un programa o eliminar un archivo, LIGERO te dice a qué pertenece y qué pasaría: si es tuyo, si otro programa lo necesita, si afecta a la seguridad del equipo o si es parte de Windows.
>
> **Se adapta a tu equipo.** La primera vez que lo abres reconoce el procesador, la memoria, el disco y la gráfica, y se ajusta a la gama de tu equipo.
>
> **Qué incluye:**
> • Chequeo de salud con nota y consejos concretos
> • Liberar memoria y ver qué programas gastan más
> • Ajustes de Windows que alivian equipos modestos, todos reversibles
> • Control de los programas que arrancan al encender
> • Limpieza de temporales, cachés y restos de Windows
> • Búsqueda de archivos grandes sin usar, duplicados y descargas olvidadas
> • Desinstalador con limpieza de restos y aviso de riesgo
> • Diagnóstico y reparación de la conexión a internet
> • Estado del antivirus, cortafuegos y actualizaciones
> • Historial de todo lo hecho, con opción de deshacer
>
> **Honesto y privado.** LIGERO es gratuito y de código abierto. No tiene anuncios, ni versión de pago, ni recopila datos: todo funciona en tu equipo.
>
> Nota: liberar memoria tiene un efecto temporal en cualquier optimizador. Lo que más se nota en un equipo lento es desactivar programas de inicio que no usas y mantener espacio libre en el disco, y LIGERO te ayuda a hacerlo con seguridad.

### Descripción breve

> Optimizador honesto para equipos con pocos recursos: explica cada función y casi todo se puede deshacer.

### Características del producto (una por línea)

> Chequeo de salud del equipo con nota y consejos
> Limpieza de disco con zona de seguridad para recuperar archivos
> Ajustes de Windows reversibles para equipos modestos
> Control de los programas que arrancan con Windows
> Desinstalador con limpieza de restos y aviso de riesgo
> Archivos grandes sin usar, duplicados y descargas
> Diagnóstico y reparación de la conexión a internet
> Historial de acciones con opción de deshacer
> Se adapta automáticamente a la gama del equipo
> Gratuito, de código abierto y sin recopilar datos

### Términos de búsqueda (hasta 7)

> optimizador, limpiar disco, liberar memoria, acelerar pc, programas de inicio, desinstalador, mantenimiento

### Otros campos

- **Copyright:** `© 2026 Los autores de Ligero`
- **Sitio web:** `https://github.com/juanlobatonmoreno33-web/LIGERO`
- **Contacto de soporte:** `https://github.com/juanlobatonmoreno33-web/LIGERO/issues`
- **Política de privacidad:** `https://github.com/juanlobatonmoreno33-web/LIGERO/blob/main/PRIVACIDAD.md` (en *Propiedades*)
- **Capturas de pantalla:** al menos una, de 1366×768 o más. Recomiendo Inicio, Chequeo, Acelerar, Disco y Deshacer.

---

## 5. Propiedades y clasificación

**Dónde:** *Envío → Propiedades* y *Clasificación por edades*

- **Categoría:** Utilidades y herramientas.
- **¿Accede, recopila o transmite información personal?** No.
- **Requisitos de sistema:** puedes dejarlos vacíos. Si los rellenas: memoria mínima 2 GB.
- **Clasificación por edades:** en el cuestionario, la app no tiene violencia, contenido sexual, lenguaje adulto, apuestas, compras ni intercambio de datos entre usuarios, así que debería salir para todas las edades.
- **Precio:** Gratis.

---

## 6. Riesgos conocidos antes de enviar

1. **Capacidad `unvirtualizedResources`.** Es restringida y Microsoft puede rechazarla. Si la rechazan, la app se podría publicar sin ella, pero los ajustes de Windows y deshacer limpiezas dentro de AppData dejarían de funcionar: habría que quitar esas funciones de la versión de la Store.
2. **Ajuste de los navegadores (resuelto).** El ajuste «Impedir que Edge y Chrome sigan activos al cerrarlos» cambia directivas de los navegadores en el registro, algo que Microsoft considera sospechoso. En la versión de la Store ya no se ofrece: en su lugar, el botón «Navegadores en segundo plano…» de Acelerar explica cómo desactivarlo desde los ajustes de cada navegador.
3. **Promesas de rendimiento.** Las normas de Microsoft prohíben prometer mejoras que no se cumplen. Los textos de arriba están escritos con ese cuidado; no añadas frases como «tu PC irá el doble de rápido».
4. **Sin probar instalado.** El paquete aún no se ha probado instalado en un equipo. Si el arranque con Windows o los permisos de administrador fallan dentro del paquete, los revisores lo verán.
