@echo off
setlocal EnableExtensions
cd /d "%~dp0"
color 0A

echo =====================================================
echo   FERRARI'SPOS - PUBLICAR DESARROLLADOR PRO 3.2
echo =====================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: No se encontro dotnet.exe.
  echo Instala .NET 8 SDK y vuelve a ejecutar PUBLICAR.bat.
  pause
  exit /b 1
)

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
if not exist "%~dp0FerrariPOS_icono.ico" (
  echo ERROR: Falta FerrariPOS_icono.ico
  pause
  exit /b 1
)

if exist "%~dp0publish" rmdir /s /q "%~dp0publish"
mkdir "%~dp0publish"

echo.
echo Publicando en modo Release win-x64...
echo.
dotnet publish "%~dp0FerrariPOS_LicenseManager_Corregido.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0publish"
if errorlevel 1 (
  echo.
  echo =====================================================
  echo PUBLICACION FALLIDA
  echo =====================================================
  echo El error real aparece arriba. No se oculto ninguna salida.
  pause
  exit /b 1
)

copy /y "%~dp0private_key.pem" "%~dp0publish\private_key.pem" >nul
copy /y "%~dp0FerrariPOS_icono.ico" "%~dp0publish\FerrariPOS_icono.ico" >nul

if not exist "%~dp0publish\FerrariPOS_LicenseManager_Corregido.exe" (
  echo ERROR: dotnet termino sin generar el EXE esperado.
  pause
  exit /b 1
)

echo.
echo PUBLICACION CORRECTA.
echo EXE: "%~dp0publish\FerrariPOS_LicenseManager_Corregido.exe"
echo.
pause
exit /b 0
