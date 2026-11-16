@echo off
rem Compila Ligero.exe con el compilador de C# que ya trae Windows. No hay que instalar nada.
rem Opcional: ruta de salida como primer argumento (por defecto, Ligero.exe en esta carpeta).
setlocal
cd /d "%~dp0"
set OUT=%~1
if "%OUT%"=="" set OUT=Ligero.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
rem Descripciones de Windows para las funciones de la versión de la Store (arranque con Windows).
set WM=%WINDIR%\System32\WinMetadata
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu ^
  /win32manifest:app.manifest /win32icon:Ligero.ico /out:"%OUT%" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%FW%\System.Runtime.dll" /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.ApplicationModel.winmd" ^
  src\*.cs
if errorlevel 1 (echo. & echo ERROR al compilar. & exit /b 1)
echo Listo: %OUT%
