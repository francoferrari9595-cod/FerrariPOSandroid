@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title FerrariPOS Manager - Compilar Android

echo ===============================================================
echo        FERRARI'S POS - MANAGER ANDROID
echo        COMPILACION LOCAL DEL APK
echo ===============================================================
echo.

where java >nul 2>&1
if errorlevel 1 (
  echo ERROR: No se encontro Java/JDK.
  echo Instala JDK 17 y vuelve a ejecutar este BAT.
  pause
  exit /b 1
)

java -version
echo.

if not exist "gradlew.bat" (
  echo ERROR: No se encontro gradlew.bat.
  pause
  exit /b 1
)

echo Limpiando y compilando APK...
call gradlew.bat --no-daemon --stacktrace --warning-mode all clean assembleDebug
set "RC=%ERRORLEVEL%"

echo.
if not "%RC%"=="0" (
  echo ===============================================================
  echo ERROR: La compilacion Android fallo.
  echo ===============================================================
  pause
  exit /b %RC%
)

echo ===============================================================
echo APK GENERADO CORRECTAMENTE
echo ===============================================================
echo.
echo Archivo:
echo %CD%\app\build\outputs\apk\debug\app-debug.apk
echo.
echo Para GitHub, ejecuta SUBIR_A_GITHUB.bat desde la carpeta raiz.
echo.
pause
endlocal
exit /b 0
