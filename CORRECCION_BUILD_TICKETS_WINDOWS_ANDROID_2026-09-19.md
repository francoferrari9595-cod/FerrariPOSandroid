# Corrección de compilación Android — Tickets Windows

Se corrigió el error de compilación Kotlin en `FerrariPOS.Manager.Android/app/src/main/java/com/ferrarispos/manager/MainActivity.kt`.

## Causa
El estado `mixed` del `PaymentDialog` se infería con un tipo ambiguo al combinar `mapOf(...)` y `emptyMap()`. Eso provocaba el error del delegado `setValue` y una cascada de errores posteriores (`any`, `isEmpty`, `values`, `filterValues`, etc.).

## Corrección
Se declaró explícitamente el estado como `Map<String, Double>` y el `MutableState` con el mismo tipo.

No se modificó la lógica de cobro mixto ni la función de tickets Windows; solamente se corrigió la inferencia de tipos que impedía compilar.

## Verificación
El entorno de esta sesión no pudo ejecutar Gradle porque no tiene resolución DNS hacia `services.gradle.org`. La corrección está aplicada sobre el código que produjo exactamente los errores reportados en el build de GitHub Actions.
