@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "GRADLE_VERSION=8.11.1"
set "CACHE=%USERPROFILE%\.gradle\wrapper\dists\gradle-%GRADLE_VERSION%-bin"
set "GRADLE_HOME="
for /d %%D in ("%CACHE%\*") do if exist "%%D\gradle-%GRADLE_VERSION%\bin\gradle.bat" set "GRADLE_HOME=%%D\gradle-%GRADLE_VERSION%"
if defined GRADLE_HOME goto RUN
set "TMP=%TEMP%\FerrariPOS_Gradle"
if not exist "%TMP%\gradle-%GRADLE_VERSION%\bin\gradle.bat" (
  echo Descargando Gradle %GRADLE_VERSION%...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $d='%TMP%'; New-Item -ItemType Directory -Force $d | Out-Null; Invoke-WebRequest 'https://services.gradle.org/distributions/gradle-8.11.1-bin.zip' -OutFile ($d+'\gradle.zip'); Expand-Archive ($d+'\gradle.zip') -DestinationPath $d -Force"
)
set "GRADLE_HOME=%TMP%\gradle-%GRADLE_VERSION%"
:RUN
call "%GRADLE_HOME%\bin\gradle.bat" %*
set "RC=%ERRORLEVEL%"
endlocal & exit /b %RC%
