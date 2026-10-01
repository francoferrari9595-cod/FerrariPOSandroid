@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET 8 SDK no esta instalado.
  echo Instala el SDK de .NET 8 y vuelve a ejecutar este archivo.
  pause
  exit /b 1
)

if not exist private_key.pem (
  echo ERROR: falta private_key.pem.
  echo La clave privada debe permanecer solamente en el equipo administrador.
  pause
  exit /b 1
)

echo.
echo ==============================================
echo FERRARI'SPOS - LICENSE MANAGER CORREGIDO
echo ==============================================
echo.
dotnet run -c Release
if errorlevel 1 (
  echo.
  echo ERROR: no se pudo compilar/ejecutar.
)
pause
