@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title FERRARIPOS - AUTO SETUP COMPLETO V73.1.45

set "PROJECT=%~dp0FerrariPOS.csproj"
set "OUT=%~dp0EXE"
set "DIST=%~dp0DIST"
set "ISS=%~dp0Setup_FerrariPOS.iss"
set "INSTALLER=%~dp0FerrariPOS_Setup_V73.1.45.exe"
set "CF=%OUT%\cloudflared.exe"
set "CF_URL=https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"

if not exist "%PROJECT%" (
  echo ERROR: No se encontro FerrariPOS.csproj
  pause
  exit /b 1
)

net session >nul 2>&1
if errorlevel 1 (
  echo Solicitando permisos de administrador...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b 0
)

where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
  if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" set "PATH=%ProgramFiles(x86)%\dotnet;%PATH%"
)
where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo .NET 8 SDK no esta instalado.
  echo Intentando instalarlo automaticamente con winget...
  where winget >nul 2>&1
  if errorlevel 1 (
    echo ERROR: winget no esta disponible. Instala .NET 8 SDK y vuelve a ejecutar este archivo.
    pause
    exit /b 1
  )
  winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
  if errorlevel 1 goto :error
  where dotnet >nul 2>&1
  if errorlevel 1 (
    echo ERROR: .NET 8 SDK se instalo pero no aparece en PATH. Cierra y vuelve a ejecutar AUTO_SETUP.bat.
    pause
    exit /b 1
  )
)

echo.
echo ================================================================
echo        FERRARIPOS - AUTO SETUP COMPLETO V73.1.45
echo   Instalador + Cloudflare + red/DNS por indice de interfaz
echo ================================================================
echo.

echo [1/6] Limpiando compilaciones anteriores...
if exist "%~dp0bin" rmdir /s /q "%~dp0bin"
if exist "%~dp0obj" rmdir /s /q "%~dp0obj"
if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%DIST%" rmdir /s /q "%DIST%"
if exist "%INSTALLER%" del /q "%INSTALLER%"
mkdir "%OUT%" >nul 2>&1

 echo [2/6] Restaurando paquetes...
dotnet restore "%PROJECT%" -r win-x64
if errorlevel 1 goto :error

echo [3/6] Publicando FerrariPOS autocontenido para Windows 10/11 x64...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore -o "%OUT%" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false
if errorlevel 1 goto :error
if not exist "%OUT%\FerrarisPOS.exe" goto :error

 echo [4/6] Descargando cloudflared oficial para incluirlo en el instalador...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$u='%CF_URL%'; $o='%CF%'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $ok=$false; 1..3 | %% { try { Invoke-WebRequest -UseBasicParsing -Uri $u -OutFile $o -TimeoutSec 60; if ((Get-Item $o).Length -gt 5000000) { $ok=$true; break } } catch { Start-Sleep -Seconds 2 } }; if (-not $ok) { exit 1 }"
if errorlevel 1 (
  echo ADVERTENCIA: no se pudo descargar cloudflared. El programa lo descargara automaticamente al primer inicio.
) else (
  for %%A in ("%CF%") do if %%~zA LSS 5000000 del /q "%CF%"
)

mkdir "%DIST%" >nul 2>&1
copy /y "%OUT%\FerrarisPOS.exe" "%DIST%\FerrarisPOS.exe" >nul
if exist "%OUT%\license_server.json" copy /y "%OUT%\license_server.json" "%DIST%\license_server.json" >nul
if exist "%OUT%\cloudflared.exe" copy /y "%OUT%\cloudflared.exe" "%DIST%\cloudflared.exe" >nul

 echo [5/6] Preparando Inno Setup y configuracion automatica de red...
set "ISCC="
where ISCC.exe >nul 2>&1
if not errorlevel 1 set "ISCC=ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  where winget >nul 2>&1
  if not errorlevel 1 (
    echo Inno Setup no esta instalado. Instalando automaticamente...
    winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
    if not errorlevel 1 (
      if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
      if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
    )
  )
)

if not defined ISCC (
  echo ERROR: Inno Setup no esta disponible.
  echo La compilacion portable si fue generada en: "%OUT%\FerrarisPOS.exe"
  pause
  exit /b 1
)

 echo [6/6] Generando FerrariPOS_Setup_V73.1.45.exe...
"%ISCC%" "%ISS%"
if errorlevel 1 goto :error
if not exist "%INSTALLER%" goto :error

echo.
echo ================================================================
echo INSTALADOR COMPLETO CREADO CORRECTAMENTE
echo ================================================================
echo.
echo %INSTALLER%
echo.
echo El instalador incluye:
echo  - FerrariPOS autocontenido (sin .NET en la PC destino)
echo  - cloudflared incluido como respaldo local
echo  - deteccion de red por InterfaceIndex, no por nombre WiFi / WiFi 2 / Ethernet
echo  - DNS automatico 1.1.1.1 / 1.0.0.1 / 8.8.8.8 / 8.8.4.4
echo  - flush DNS automatico
echo  - regla privada de firewall para el servidor local 8787
echo  - servidor web y Quick Tunnel Cloudflare automaticos al iniciar FerrariPOS
echo.
echo IMPORTANTE: AUTO_SETUP.bat SOLO CREA EL INSTALADOR.
echo No inicia el instalador ni modifica la instalacion actual de FerrariPOS.
echo.
exit /b 0

:error
echo.
echo ================================================================
echo ERROR DE COMPILACION / SETUP
echo ================================================================
echo.
pause
exit /b 1
