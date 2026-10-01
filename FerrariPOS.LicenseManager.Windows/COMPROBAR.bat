@echo off
setlocal EnableExtensions
cd /d "%~dp0"
color 0B

echo =====================================================
echo   FERRARI'SPOS - COMPROBACION DEL PROYECTO
 echo =====================================================
echo.

set FAIL=0
for %%F in (
  "FerrariPOS_LicenseManager_Corregido.csproj"
  "LicenseManagerForm.cs"
  "LoginForm.cs"
  "Program.cs"
  "private_key.pem"
  "FerrariPOS_icono.ico"
  "FerrariPOS_LicenseManager_Setup.iss"
  "CREAR_INSTALADOR.bat"
  "PUBLICAR.bat"
) do (
  if exist "%%~F" (echo [OK] %%~F) else (echo [FALTA] %%~F & set FAIL=1)
)

echo.
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [FALTA] .NET SDK no esta instalado o no esta en PATH.
  set FAIL=1
) else (
  echo [OK] .NET SDK detectado:
  dotnet --version
)

echo.
where ISCC.exe >nul 2>nul
if errorlevel 1 (
  if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    echo [OK] Inno Setup 6 detectado.
  ) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
    echo [OK] Inno Setup 6 detectado.
  ) else (
    echo [AVISO] Inno Setup 6 no detectado. Solo impide crear el instalador, no publicar el EXE.
  )
) else echo [OK] ISCC.exe detectado en PATH.

echo.
if "%FAIL%"=="0" (
  echo COMPROBACION BASICA CORRECTA.
) else (
  echo HAY ELEMENTOS FALTANTES.
)
echo.
pause
exit /b %FAIL%
