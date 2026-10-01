@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title FerrariPOS - Compilar Facil V73.1.52

set "PROJECT=%~dp0FerrariPOS.csproj"
set "OUT=%~dp0EXE"

if not exist "%PROJECT%" (
  echo ERROR: No se encontro FerrariPOS.csproj
  echo Ejecuta este archivo dentro de la carpeta Proyecto.
  pause
  exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
  if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" set "PATH=%ProgramFiles(x86)%\dotnet;%PATH%"
)
where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo No se encontro .NET 8 SDK.
  echo.
  echo Si tenes Windows 10/11 con winget, se intentara instalarlo automaticamente.
  where winget >nul 2>&1
  if errorlevel 1 (
    echo No se encontro winget.
    echo Instala .NET 8 SDK y vuelve a ejecutar COMPILAR_FACIL.bat.
    pause
    exit /b 1
  )
  winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
  if errorlevel 1 (
    echo ERROR: No se pudo instalar .NET 8 SDK.
    pause
    exit /b 1
  )
  set "PATH=%ProgramFiles%\dotnet;%ProgramFiles(x86)%\dotnet;%PATH%"
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET 8 SDK sigue sin estar disponible.
  echo Reinicia Windows y vuelve a ejecutar este archivo.
  pause
  exit /b 1
)

echo ================================================================
echo             FERRARIPOS - COMPILACION FACIL
echo                         V73.1.52
echo ================================================================
echo.
echo Restaurando dependencias...
dotnet restore "%PROJECT%" -r win-x64
if errorlevel 1 goto :error

echo.
echo Publicando FerrariPOS para Windows x64...
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%" >nul 2>&1

dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore -o "%OUT%" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false
if errorlevel 1 goto :error

if not exist "%OUT%\FerrarisPOS.exe" (
  echo ERROR: La compilacion termino sin generar FerrarisPOS.exe
  goto :error
)

echo.
echo ================================================================
echo                 COMPILACION CORRECTA
echo ================================================================
echo.
echo EXE generado en:
echo %OUT%\FerrarisPOS.exe
echo.
echo Para crear tambien el instalador, ejecuta AUTO_SETUP.bat
explorer "%OUT%"
pause
exit /b 0

:error
echo.
echo ================================================================
echo                  COMPILACION FALLIDA
echo ================================================================
echo.
echo El mensaje de error real aparece arriba.
pause
exit /b 1
