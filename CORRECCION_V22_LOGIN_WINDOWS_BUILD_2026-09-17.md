# FerrariPOS V22 — corrección de compilación + rediseño del ingreso

Fecha: 2026-09-17

## Android — error de GitHub corregido

Se corrigieron los errores de `MainActivity.kt` que impedían `compileReleaseKotlin`:

- conflicto de nombres `NeonWhite` entre la paleta `FerrariPalette` y el color `Color`.
- expresión incorrecta de `paymentNormalized` (`:` donde correspondía `else`) y se reescribió en varias líneas para evitar ambigüedades de inferencia.
- llamada del botón de actualización del Corte Z: `onClick={load()}`.

Los mensajes `Unable to strip...` de bibliotecas nativas no eran la causa del fallo; el fallo real estaba en `compileReleaseKotlin`.

## Windows — pantalla de ingreso

- Eliminado exclusivamente el QR pequeño que aparecía en la pantalla de login.
- El QR de vinculación de Android continúa disponible desde la configuración/vinculación, por lo que no se rompe el mecanismo de conexión.
- Se conserva la imagen `FerrariPOS_login_poster.png`.
- Rediseño de la ventana de login con fondo oscuro degradado, tarjetas tipo glass, bordes suaves, mejor espaciado y aspecto más delicado/profesional.
- No se modificó la lógica de usuarios, contraseñas, licencia ni conexión QR/Cloudflare.

## GitHub Actions

Se conserva el flujo que entrega cuatro artefactos independientes:

1. `FerrariPOS-Manager-Android`
2. `FerrariPOS-Manager-Windows-EXE`
3. `FerrariPOS-Manager-Windows-SETUP`

Nota: la compilación Android no pudo ejecutarse localmente en este entorno porque no hubo resolución DNS hacia `services.gradle.org`; por eso la validación final del APK debe hacerla GitHub Actions.
