# FerrariPOS — corrección puntual 2026-09-25

Base utilizada: `FERRARI_POS_WINDOWS_ANDROID_CORREGIDO_SOLO_WINDOWS_2026-09-24.zip`.

## Android — una sola corrección funcional
Se modificó únicamente el diálogo de **Recibir mercadería / Recepción con diferencia**.

Antes, el campo convertía el texto a número y lo volvía a formatear en cada tecla (`num(...)` + `fmt(...)`). Eso hacía que el cursor saltara y que el valor escrito se reseteara.

Ahora el campo conserva el texto exactamente mientras se escribe y recién al confirmar convierte cada cantidad a número. La nota/motivo mantiene su comportamiento original.

No se modificaron colores, pantallas, navegación, estructura, órdenes de compra ni ninguna otra parte visual/funcional de Android.

## Windows — persistencia de ventas recibidas desde Android
Se agregó persistencia de los cambios locales realizados en Windows sobre un ticket que llegó desde Android. Al agregar productos desde Windows, la copia persistente del ticket se actualiza antes del refresco automático, evitando que el temporizador vuelva a cargar la versión anterior y quite el producto agregado.

También se cubren tickets de mesa móviles.

No se revirtió ninguna corrección anterior.

## Verificación
- Android modificado: `FerrariPOS.Manager.Android/app/src/main/java/com/ferrarispos/manager/MainActivity.kt` únicamente.
- Windows modificado: `FerrariPOS.Windows/Forms/MainForm.cs` y `FerrariPOS.Windows/Services/WebDashboardServer.cs`.
- La compilación Android no pudo ejecutarse en este entorno porque Gradle requiere descargar `gradle-8.11.1` desde `services.gradle.org` y el entorno no tiene resolución de red.
