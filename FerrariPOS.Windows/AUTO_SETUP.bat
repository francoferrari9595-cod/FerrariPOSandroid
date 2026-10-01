@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title FerrariPOS - Generador de Setup V73.1.52

set "PROJECT=%~dp0FerrariPOS.csproj"
set "ISS=%~dp0Setup_FerrariPOS.iss"
set "INSTALLER=%~dp0FerrariPOS_Setup_V73.1.52.exe"
set "BUILDROOT=%TEMP%\FerrariPOS_Build"
set "OUT=%BUILDROOT%\Publish"
set "INNO_INSTALLER=%TEMP%\FerrariPOS_InnoSetup_6.7.3.exe"
set "INNO_URL=https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe"
set "CF=%OUT%\cloudflared.exe"
set "CF_URL=https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"

if not exist "%PROJECT%" (
  echo ERROR: No se encontro FerrariPOS.csproj
  pause
  exit /b 1
)
if not exist "%ISS%" (
  echo ERROR: No se encontro Setup_FerrariPOS.iss
  pause
  exit /b 1
)

:: Elevar a administrador sin PowerShell.
net session >nul 2>&1
if errorlevel 1 (
  echo Solicitando permisos de administrador...
  >"%temp%\FerrariPOS_elevate.vbs" echo Set UAC = CreateObject^("Shell.Application"^)
  >>"%temp%\FerrariPOS_elevate.vbs" echo UAC.ShellExecute "%~f0", "", "", "runas", 1
  cscript //nologo "%temp%\FerrariPOS_elevate.vbs" >nul 2>&1
  del /q "%temp%\FerrariPOS_elevate.vbs" >nul 2>&1
  exit /b 0
)

cls
echo ================================================================
echo              FERRARIPOS - SETUP V73.1.52
echo ================================================================
echo.
echo CORRECCION IMPORTANTE:
echo La compilacion se realiza en una carpeta temporal de Windows.
echo No se sobrescribe EXE\FerrarisPOS.exe, por lo que el programa
echo puede estar abierto y no provoca "Access is denied" durante el build.
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
  if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" set "PATH=%ProgramFiles(x86)%\dotnet;%PATH%"
)
where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET 8 SDK no esta instalado.
  where winget >nul 2>&1
  if errorlevel 1 (
    echo ERROR: Windows no tiene winget disponible.
    echo Instala .NET 8 SDK y vuelve a ejecutar este Setup.
    pause
    exit /b 1
  )
  echo Instalando .NET 8 SDK automaticamente...
  winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
  if errorlevel 1 goto :error
  set "PATH=%ProgramFiles%\dotnet;%ProgramFiles(x86)%\dotnet;%PATH%"
)
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET 8 SDK no quedo disponible.
  echo Reinicia Windows y vuelve a ejecutar AUTO_SETUP.bat.
  pause
  exit /b 1
)

:: Buscar Inno Setup instalado antes de compilar.
call :find_inno
if not defined ISCC (
  echo Inno Setup no esta instalado. Descargando la version oficial...
  set "CURL=%SystemRoot%\System32\curl.exe"
  if not exist "%CURL%" set "CURL=curl.exe"
  where curl.exe >nul 2>&1
  if errorlevel 1 (
    echo ERROR: No se encontro curl.exe para descargar Inno Setup.
    echo Instala Inno Setup 6 manualmente y vuelve a ejecutar este archivo.
    pause
    exit /b 1
  )
  if exist "%INNO_INSTALLER%" del /q "%INNO_INSTALLER%" >nul 2>&1
  "%CURL%" -L --fail --retry 3 --connect-timeout 20 --max-time 300 -o "%INNO_INSTALLER%" "%INNO_URL%"
  if errorlevel 1 goto :inno_download_error
  for %%A in ("%INNO_INSTALLER%") do set "INNO_SIZE=%%~zA"
  if not defined INNO_SIZE goto :inno_download_error
  if !INNO_SIZE! LSS 3000000 goto :inno_download_error
  echo Instalando Inno Setup...
  start "" /wait "%INNO_INSTALLER%" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
  if errorlevel 1 goto :error
  del /q "%INNO_INSTALLER%" >nul 2>&1
  call :find_inno
)
if not defined ISCC (
  echo ERROR: No se encontro ISCC.exe despues de instalar Inno Setup.
  pause
  exit /b 1
)

:: Crear carpeta temporal NUEVA. Nunca usamos Proyecto\EXE como destino de publish.
if exist "%BUILDROOT%" rmdir /s /q "%BUILDROOT%" >nul 2>&1
mkdir "%OUT%" >nul 2>&1
if not exist "%OUT%" (
  echo ERROR: No se pudo crear la carpeta temporal de compilacion.
  goto :error
)

:: Limpiar solo artefactos MSBuild si no estan bloqueados; no se toca EXE.
if exist "%~dp0bin" rmdir /s /q "%~dp0bin" >nul 2>&1
if exist "%~dp0obj" rmdir /s /q "%~dp0obj" >nul 2>&1

echo [1/3] Restaurando paquetes...
dotnet restore "%PROJECT%" -r win-x64
if errorlevel 1 goto :error

echo [2/3] Publicando FerrariPOS en carpeta temporal...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore -o "%OUT%" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false
if errorlevel 1 goto :error
if not exist "%OUT%\FerrarisPOS.exe" (
  echo ERROR: No se genero FerrarisPOS.exe en la carpeta temporal.
  goto :error
)

:: Incluir cloudflared en el instalador. Asi una PC nueva puede levantar
:: el Quick Tunnel inmediatamente, sin depender de una descarga en el primer arranque.
echo Descargando cloudflared oficial para incluirlo en el instalador...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$u='%CF_URL%'; $o='%CF%'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $ok=$false; 1..3 | %% { try { Invoke-WebRequest -UseBasicParsing -Uri $u -OutFile $o -TimeoutSec 60; if ((Get-Item $o).Length -gt 5000000) { $ok=$true; break } } catch { Start-Sleep -Seconds 2 } }; if (-not $ok) { exit 1 }"
if errorlevel 1 (
  echo ADVERTENCIA: no se pudo descargar cloudflared. El programa intentara descargarlo al primer inicio.
) else (
  for %%A in ("%CF%") do if %%~zA LSS 5000000 del /q "%CF%"
)

:: Inno recibe la carpeta temporal mediante /dBuildDir, asi nunca lee el EXE bloqueado de Proyecto\EXE.
echo [3/3] Creando FerrariPOS_Setup_V73.1.52.exe...
"%ISCC%" /dBuildDir="%OUT%" /dProjectDir="%~dp0" "%ISS%"
if errorlevel 1 goto :error
if not exist "%INSTALLER%" (
  echo ERROR: Inno Setup termino pero no creo el instalador.
  goto :error
)

rmdir /s /q "%BUILDROOT%" >nul 2>&1

echo.
echo ================================================================
echo              SETUP CREADO CORRECTAMENTE
echo ================================================================
echo.
echo %INSTALLER%
echo.
echo La compilacion ya no usa Proyecto\EXE como carpeta de salida.
echo Por eso no se bloquea aunque FerrariPOS este abierto.
echo.
pause
exit /b 0

:inno_download_error
echo ERROR: No se pudo descargar Inno Setup correctamente.
if exist "%INNO_INSTALLER%" del /q "%INNO_INSTALLER%" >nul 2>&1
pause
exit /b 1

:find_inno
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not defined ISCC for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%I"
exit /b 0

:error
echo.
echo ================================================================
echo                  EL SETUP NO PUDO TERMINAR
echo ================================================================
echo.
echo Revisa el mensaje de error que aparece arriba.
echo.
pause
exit /b 1
