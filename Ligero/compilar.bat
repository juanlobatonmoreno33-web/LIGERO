@echo off
rem Compila Ligero.exe con el compilador de C# que ya trae Windows. No hay que instalar nada.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu ^
  /win32manifest:app.manifest /win32icon:Ligero.ico /out:Ligero.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 (echo. & echo ERROR al compilar. & exit /b 1)
echo Listo: Ligero.exe
