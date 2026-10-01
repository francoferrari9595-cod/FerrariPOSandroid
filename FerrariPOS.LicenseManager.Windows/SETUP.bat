@echo off
cd /d "%~dp0"
call "%~dp0CREAR_INSTALADOR.bat"
exit /b %errorlevel%
