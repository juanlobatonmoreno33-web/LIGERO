# Ligero

Optimizador para Windows pensado para equipos con pocos recursos. Explica qué hace cada función antes de hacerla y casi todo se puede deshacer.

## Qué hace

- **Inicio y chequeo de salud**: estado de la memoria, el disco y la protección, con una nota y adónde ir para mejorar cada punto.
- **Memoria**: libera la memoria que los programas no usan y muestra los que más gastan.
- **Acelerar**: ajustes de Windows (animaciones, transparencias, servicios pesados, hibernación…), todos reversibles.
- **Arranque**: activa o desactiva los programas que se abren solos al encender.
- **Disco y archivos**: limpia temporales y cachés, y encuentra archivos grandes sin usar, duplicados y descargas.
- **Programas**: desinstala y limpia los restos, avisando antes si quitar algo afecta a la seguridad o a otros programas.
- **Eliminar**: elimina cualquier archivo o carpeta tras explicar a qué pertenece y qué pasaría.
- **Red y protección**: diagnóstico de la conexión y estado del antivirus, cortafuegos y actualizaciones.
- **Reconocimiento del equipo**: en el primer arranque mide el equipo y se ajusta a su gama.
- **Deshacer**: historial de todo lo hecho. Los archivos no se borran de golpe: pasan antes por una zona de seguridad.

## Seguridad y privacidad

- No descarga nada ni envía datos a ningún sitio. Las únicas conexiones son las pruebas de la sección Red (un ping y una consulta de nombre), solo cuando las pides.
- Nada cambia hasta que pulsas un botón.
- Algunos antivirus pueden marcar un `Ligero.exe` sin firmar como sospechoso, porque hace cosas propias de un optimizador (liberar memoria de otros procesos, cambiar ajustes de Windows, arrancar con el sistema). Las versiones publicadas van firmadas.

## Requisitos

Windows 10 u 11 con .NET Framework 4 (incluido en Windows). No hace falta instalar nada más.

## Compilar

```bat
compilar.bat
```

Usa el compilador de C# que trae Windows y genera `Ligero.exe`. En equipos donde Windows bloquea programas sin firma (Smart App Control), `Abrir Ligero.cmd` ejecuta el mismo código a través de PowerShell.

## Licencia

MIT. Consulta el archivo `LICENSE`.
