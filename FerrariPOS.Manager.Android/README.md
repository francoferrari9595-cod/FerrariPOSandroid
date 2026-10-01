# FerrariPOS · Sistema completo

Este repositorio contiene dos proyectos separados pero preparados para integrarse:

- `FerrariPOS.Windows`: FerrariPOS de escritorio para Windows 10/11.
- `FerrariPOS.Manager.Android`: FerrariPOS Manager para Android.

## Vinculación

1. Abrí FerrariPOS en Windows.
2. Entrá a Configuración como ADMIN.
3. Pulsá **VINCULAR MANAGER (QR)**.
4. En Android abrí FerrariPOS Manager y elegí **ESCANEAR QR DE FERRARIPOS**.
5. La app valida el token y guarda la conexión.

La app no abre ni modifica SQLite directamente: usa la API móvil integrada en FerrariPOS.

## GitHub Actions

El workflow `.github/workflows/build.yml` genera automáticamente:
- APK Android `app-debug.apk`.
- Setup de Windows `FerrariPOS_Setup_V73.1.56.exe`.

## Windows local

Ejecutá `FerrariPOS.Windows\COMPILAR_WINDOWS10.bat`.

Requisitos: .NET 8 SDK. Para generar el instalador, también Inno Setup 6.

## Android local

Abrí `FerrariPOS.Manager.Android` en Android Studio o ejecutá el Gradle indicado en `BUILD_ANDROID.bat`.

## Importante

La primera versión móvil trabaja en red local. Para acceso remoto debe utilizarse HTTPS/VPN o un mecanismo de publicación seguro; no se recomienda exponer directamente el puerto del POS a Internet.
