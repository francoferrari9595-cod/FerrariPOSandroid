@echo off
setlocal EnableExtensions
cd /d "%~dp0"
color 0A

echo =====================================================
echo   FERRARI'SPOS - CREAR INSTALADOR
echo =====================================================
echo.

echo [1/5] Comprobando .NET SDK...
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: No se encontro dotnet.exe.
  echo Necesitas instalar .NET 8 SDK.
  echo.
  pause
  exit /b 1
)
dotnet --version
echo.

echo [2/5] Comprobando archivos del proyecto...
if not exist "%~dp0FerrariPOS_LicenseManager_Corregido.csproj" (
  echo ERROR: Falta FerrariPOS_LicenseManager_Corregido.csproj
  pause
  exit /b 1
)
if not exist "%~dp0private_key.pem" (
  echo ERROR: Falta private_key.pem
  pause
  exit /b 1
)
if not exist "%~dp0FerrariPOS_LicenseManager_Setup.iss" (
  echo ERROR: Falta FerrariPOS_LicenseManager_Setup.iss
  pause
  exit /b 1
)
if not exist "%~dp0FerrariPOS_icono.ico" (
  echo ERROR: Falta FerrariPOS_icono.ico
  pause
  exit /b 1
)
echo OK.
echo.

echo [3/5] Buscando Inno Setup 6...
set "ISCC="
for %%P in (
  "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  "%ProgramFiles%\Inno Setup 6\ISCC.exe"
  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
) do if exist "%%~P" set "ISCC=%%~P"

if not defined ISCC (
  where ISCC.exe >nul 2>nul
  if not errorlevel 1 for /f "delims=" %%P in ('where ISCC.exe') do if not defined ISCC set "ISCC=%%P"
)

if not defined ISCC (
  echo ERROR: No se encontro ISCC.exe de Inno Setup 6.
  echo Instala Inno Setup 6 y vuelve a ejecutar este archivo.
  echo.
  pause
  exit /b 1
)
echo Inno Setup encontrado: %ISCC%
echo.

echo [4/5] Publicando el desarrollador...
if exist "%~dp0bin" rmdir /s /q "%~dp0bin"
if exist "%~dp0obj" rmdir /s /q "%~dp0obj"
if exist "%~dp0publish" rmdir /s /q "%~dp0publish"
if exist "%~dp0installer" rmdir /s /q "%~dp0installer"
mkdir "%~dp0installer"

dotnet publish "%~dp0FerrariPOS_LicenseManager_Corregido.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0publish"
if errorlevel 1 (
  echo.
  echo PUBLICACION FALLIDA.
  echo Revisa el mensaje rojo anterior.
  pause
  exit /b 1
)

copy /y "%~dp0private_key.pem" "%~dp0publish\private_key.pem" >nul
copy /y "%~dp0FerrariPOS_icono.ico" "%~dp0publish\FerrariPOS_icono.ico" >nul
if not exist "%~dp0publish\FerrariPOS_LicenseManager_Corregido.exe" (
  echo ERROR: No se genero el EXE.
  pause
  exit /b 1
)
echo Publicacion correcta.
echo.

echo [5/5] CREANDO INSTALADOR CON INNO SETUP...
"%ISCC%" "%~dp0FerrariPOS_LicenseManager_Setup.iss"
if errorlevel 1 (
  echo.
  echo ERROR: Inno Setup no pudo crear el instalador.
  pause
  exit /b 1
)

if not exist "%~dp0installer\FerrariPOS_LicenseManager_Setup.exe" (
  echo ERROR: No se encontro el instalador final.
  pause
  exit /b 1
)

echo.
echo =====================================================
echo   INSTALADOR CREADO CORRECTAMENTE
echo =====================================================
echo.
echo Archivo:
echo "%~dp0installer\FerrariPOS_LicenseManager_Setup.exe"
echo.
start "" explorer.exe "%~dp0installer"
pause
exit /b 0
