@echo off
setlocal
cd /d "%~dp0"
echo ==============================================
echo DIAGNOSTICO DEL DESARROLLADOR DE LICENCIAS
echo ==============================================
echo.
if exist private_key.pem (
  echo [OK] private_key.pem encontrado.
) else (
  echo [ERROR] falta private_key.pem
)
if exist FerrariPOS_LicenseManager_Corregido.csproj (
  echo [OK] proyecto encontrado.
) else (
  echo [ERROR] falta csproj.
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet no encontrado.
) else (
  echo [OK] dotnet encontrado.
  dotnet --version
)
echo.
echo No se modifica ningun archivo del programa cliente.
echo.
pause
