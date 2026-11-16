@echo off
rem Firma Ligero.exe con el certificado Open Source de Certum y comprueba la firma.
rem Antes: compilar.bat, tener conectado el lector con la tarjeta de Certum (o SimplySign abierto)
rem y el Windows SDK instalado (trae signtool.exe). Windows pedirá el PIN de la tarjeta.
setlocal
cd /d "%~dp0"

set SIGNTOOL=
for /f "delims=" %%F in ('dir /b /s /o-n "%ProgramFiles(x86)%\Windows Kits\10\bin\*signtool.exe" 2^>nul ^| findstr /i "\\x64\\"') do if not defined SIGNTOOL set "SIGNTOOL=%%F"
if not defined SIGNTOOL (
  echo No se encuentra signtool.exe. Instala el Windows SDK de Microsoft y vuelve a intentarlo.
  exit /b 1
)
if not exist Ligero.exe (
  echo No existe Ligero.exe. Ejecuta antes compilar.bat.
  exit /b 1
)

rem Busca el certificado por su nombre ("Open Source Developer, <tu nombre>").
rem Si tienes varios, cambia el texto entre comillas por el nombre exacto que aparece en el tuyo.
"%SIGNTOOL%" sign /n "Open Source Developer" /fd sha256 /tr http://time.certum.pl /td sha256 /d "Ligero" Ligero.exe
if errorlevel 1 (echo. & echo ERROR al firmar. Comprueba que la tarjeta esta conectada y el PIN es correcto. & exit /b 1)

"%SIGNTOOL%" verify /pa /v Ligero.exe >nul
if errorlevel 1 (echo. & echo La firma no se pudo verificar. & exit /b 1)
echo Listo: Ligero.exe firmado y verificado.
