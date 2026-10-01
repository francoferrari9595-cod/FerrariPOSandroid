# Ferrari POS Manager — corrección de compilación Android

Se corrigieron los errores de Kotlin reportados por GitHub Actions:

- Se agregó `TablePicker`, que faltaba en `MainActivity.kt`.
- Se corrigieron las llamadas a `ListRow` que no estaban pasando `onClick` explícitamente.
- Se corrigió `Icon(ChevronRight)` para usar `tint` correctamente.
- Se corrigió el botón de búsqueda para ejecutar `load()` al pulsarlo.
- Se migró `kotlinOptions` a `compilerOptions` con JVM 17.
- El `gradlew` ya no usa el Gradle instalado globalmente en GitHub Runner: fuerza Gradle 8.11.1.
- El workflow verifica que realmente se esté ejecutando Gradle 8.11.1 antes de compilar.
- Se mantiene la compilación conjunta Android + Windows y el ZIP final.

Nota: la advertencia de Gradle/Kotlin sobre funciones obsoletas no era la causa principal del fallo. El fallo real estaba en `compileDebugKotlin`, con referencias y argumentos incorrectos en `MainActivity.kt`.
