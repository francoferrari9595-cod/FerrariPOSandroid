CORRECCION FERRARI POS MANAGER - detailSale

Se corrigió MainActivity.kt en SalesTerminal: se estaba usando detailSale para abrir el detalle de las últimas ventas, pero faltaba declarar el estado Compose.

Agregado:
var detailSale by remember { mutableStateOf<Sale?>(null) }

Esto corrige los errores de Kotlin:
- line 306 Unresolved reference detailSale
- line 325 Unresolved reference detailSale

El resto del V6 se mantiene intacto.

Nota de verificación: el entorno local no pudo ejecutar Gradle porque no tiene resolución DNS hacia services.gradle.org. La corrección se hizo directamente sobre el mismo V6 que produjo el error de GitHub.
