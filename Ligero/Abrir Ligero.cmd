@echo off
rem Abre Ligero a traves de PowerShell (para equipos donde Windows bloquea Ligero.exe por no estar firmado).
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Ligero.ps1"
