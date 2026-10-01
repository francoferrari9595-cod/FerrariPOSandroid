# Ferrari POS Manager V14 — corrección de compilación Android

Se corrigió el error de Kotlin que detenía `compileDebugKotlin`:

`MainActivity.kt:113:1 This annotation is not applicable to target 'top level property with backing field'.`

Causa: quedó un `@Composable` colocado delante de la propiedad `NeonViolet`. Las propiedades de color del sistema neon no son composables, por lo que la anotación fue eliminada.

No se modificó la lógica de mesas, escáner, tickets, ventas ni el diseño neon de V13.

Verificación local: no fue posible completar Gradle porque este entorno no puede resolver `services.gradle.org`; GitHub Actions deberá ejecutar la compilación completa.
