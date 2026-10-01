# Corrección V17.0 — compilación Android

Se corrigió únicamente el error de compilación de `MainActivity.kt` reportado por GitHub Actions.

Error:
- Unresolved reference `Dialog` en las líneas 896, 924 y 951.
- Errores derivados: invocaciones `@Composable` fuera de contexto.

Causa:
- Se usaban componentes `Dialog` de Jetpack Compose sin importar `androidx.compose.ui.window.Dialog`.

Corrección:
- Se agregó el import correcto en `MainActivity.kt`.
- No se modificó ninguna lógica de conexión QR, red, Cloudflare, token, fallback ni sincronización.
- No se modificó el código Windows.

Validación:
- El archivo contiene ahora el import correcto y los tres usos de `Dialog` quedan resueltos por ese componente Compose.
- No fue posible ejecutar Gradle localmente porque el entorno no pudo resolver `services.gradle.org`; GitHub Actions debe ejecutar la compilación definitiva.
