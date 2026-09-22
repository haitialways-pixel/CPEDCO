@echo off
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Demandez a quelqu'un qui gere cet ordinateur de faire un clic droit sur Installer CPCREDO et choisir Executer en tant qu'administrateur.
  echo.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Setup-Serveur.ps1"
if errorlevel 1 (
  echo.
  echo Installation interrompue. Journal : C:\CPCREDO\logs\install.log
  pause
)
