@echo off
setlocal
cd /d "%~dp0"

echo ================================================================
echo       FERRARIPOS - COMPILAR TODO FACIL
 echo ================================================================
echo.

if not exist "FerrariPOS.Windows\COMPILAR_FACIL.bat" (
  echo ERROR: Falta FerrariPOS.Windows\COMPILAR_FACIL.bat
  pause
  exit /b 1
)

call "FerrariPOS.Windows\COMPILAR_FACIL.bat"
set "RC=%ERRORLEVEL%"
endlocal & exit /b %RC%
