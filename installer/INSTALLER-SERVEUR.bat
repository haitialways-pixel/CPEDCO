@echo off
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Ask someone who manages this computer to right-click Installer CPCREDO and choose Run as administrator.
  echo.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Setup-Serveur.ps1"
echo.
echo Log: C:\CPCREDO\logs\install.log
if exist "C:\CPCREDO\install-complete.txt" (
  echo.
  type "C:\CPCREDO\install-complete.txt"
)
pause
