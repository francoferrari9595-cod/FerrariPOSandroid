@echo off
setlocal
cd /d "%~dp0"
echo ==========================================
echo FerrariPOS V73.1.57 - COMPILACION WINDOWS
echo ==========================================
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: No se encontro .NET 8 SDK.
  echo Instala .NET 8 SDK y vuelve a ejecutar este archivo.
  pause
  exit /b 1
)
rmdir /s /q EXE 2>nul
mkdir EXE
dotnet restore FerrariPOS.csproj
if errorlevel 1 goto :fail
dotnet publish FerrariPOS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o EXE
if errorlevel 1 goto :fail
echo.
echo LISTO: EXE\FerrarisPOS.exe
where ISCC.exe >nul 2>nul
if not errorlevel 1 (
  ISCC.exe Setup_FerrariPOS_V73_1_57.iss
  if not errorlevel 1 echo SETUP GENERADO EN Installer\FerrariPOS_Setup_V73.1.57.exe
) else (
  echo AVISO: Inno Setup no esta instalado. El EXE ya esta listo.
)
pause
exit /b 0
:fail
echo.
echo LA COMPILACION FALLO. Revisa el mensaje anterior.
pause
exit /b 1
