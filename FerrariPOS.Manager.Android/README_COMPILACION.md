# FerrariPOS Manager - compilación

Corrección incluida: `MainActivity.kt` tenía una declaración de función composable con la anotación `@Composable` mal colocada. Eso provocaba:

`Unresolved reference: row`

La declaración ahora usa la forma válida:

`row: @Composable (T) -> Unit`

Además se mantiene Java/Kotlin en JVM 17 y GitHub Actions usa JDK 17 + Gradle 8.9.
