@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title FerrariPOS - Publicar proyecto COMPLETO en GitHub

set "DEFAULT_REPO=https://github.com/francoferrari9595-cod/FerrariPOSandroid.git"
set "BRANCH=main"
set "COMMIT=FerrariPOS - proyecto completo actualizado"

echo.
echo Repositorio destino por defecto:
echo %DEFAULT_REPO%
echo.
set "REPO="
set /p "REPO=Pegue el link del repositorio GitHub (ENTER = usar el destino por defecto): "
if not defined REPO set "REPO=%DEFAULT_REPO%"

rem Aceptar tambien links terminados en /
if "%REPO:~-1%"=="/" set "REPO=%REPO:~0,-1%"
rem Normalizar para Git: agregar .git si es un enlace github.com sin sufijo
if /I "%REPO:~-4%" NEQ ".git" set "REPO=%REPO%.git"

echo.
echo Destino seleccionado: %REPO%
echo.

cls
echo ================================================================
echo       FERRARIPOS - PUBLICAR PROYECTO COMPLETO
 echo ================================================================
echo.
echo Este BAT NO usa GitHub CLI (gh).
echo Solo necesita Git for Windows.
echo.

where git >nul 2>&1
if errorlevel 1 (
  echo ERROR: Git no esta instalado o no esta en PATH.
  echo Instala Git for Windows y vuelve a ejecutar este BAT.
  pause
  exit /b 1
)

if not exist ".git\HEAD" (
  echo [1/7] Preparando repositorio local...
  git init || goto :ERROR
) else (
  echo [1/7] Repositorio local detectado.
)

git remote get-url origin >nul 2>&1
if errorlevel 1 (git remote add origin "%REPO%") else (git remote set-url origin "%REPO%")
if errorlevel 1 goto :ERROR

git branch -M %BRANCH% || goto :ERROR
git config user.name "FerrariPOS Build" || goto :ERROR
git config user.email "ferraripos-build@users.noreply.github.com" || goto :ERROR

echo [2/7] Guardando TODO el proyecto local...
git add -A || goto :ERROR

git diff --cached --quiet
if errorlevel 1 (
  git commit -m "%COMMIT%" || goto :ERROR
) else echo No habia cambios locales pendientes.

echo [3/7] Consultando GitHub...
git fetch origin %BRANCH% >nul 2>&1
if errorlevel 0 (
  echo GitHub ya tiene main. Se conservara el historial remoto y se integrara el estado local.
  git merge --no-edit -X theirs origin/%BRANCH%
  if errorlevel 1 (
    echo El merge automatico no pudo resolverse.
    echo Se intentara publicar el estado local completo mediante force-with-lease.
    git merge --abort >nul 2>&1
  )
) else (
  echo No se pudo leer main remoto; se continuara con la publicacion normal.
)

echo [4/7] Revisando estado...
git status --short

echo [5/7] Preparando commit final...
git add -A || goto :ERROR
git diff --cached --quiet || git commit -m "%COMMIT%" || goto :ERROR

echo [6/7] Subiendo proyecto COMPLETO...
git push -u origin %BRANCH% --force-with-lease
if errorlevel 1 (
  echo.
  echo Force-with-lease rechazado. Intentando push normal despues de actualizar remoto...
  git fetch origin %BRANCH% || goto :ERROR
  git rebase origin/%BRANCH%
  if errorlevel 1 (
    git rebase --abort >nul 2>&1
    goto :ERROR
  )
  git push -u origin %BRANCH%
  if errorlevel 1 goto :ERROR
)

echo [7/7] PUBLICACION COMPLETADA.
echo.
echo Render tomara Nexo/FerrariPOS.CentralServer directamente del repositorio.
echo Central Server NO se publica como artefacto de descarga.
echo.
pause
endlocal
exit /b 0

:ERROR
echo.
echo ================================================================
echo             ERROR DE PUBLICACION
 echo ================================================================
echo.
echo El mensaje real de Git aparece arriba.
echo No se borro el proyecto local.
echo.
pause
endlocal
exit /b 1
