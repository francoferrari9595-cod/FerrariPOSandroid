# FerrariPOS V19.1 — corrección de compilación Android

## Errores corregidos

El build de GitHub fallaba en `MainActivity.kt` durante `compileDebugKotlin`.

### 1. Promotions / combinedClickable
Se habilitó explícitamente `ExperimentalFoundationApi` a nivel de archivo para `combinedClickable`.

### 2. Inferencia de tipo de `selected`
La expresión con `associate ?: emptyMap()` provocaba que Kotlin no pudiera inferir correctamente el tipo del mapa. Ahora queda declarado explícitamente como `Map<Int, Double>`.

Esto elimina los errores derivados en:
- `entries`
- `values`
- acceso por ID
- `toMutableMap()`
- `remove()`
- `map { ... }`
- `isNotEmpty()`

### 3. Gráfico de medios de pago
Se reemplazó el render anterior por un gráfico circular con efecto 3D visual:
- cada medio de pago conserva un color independiente;
- profundidad inferior;
- separadores entre sectores;
- realce de cada sector;
- centro con total de cobros;
- leyenda sincronizada con los mismos colores.

## Conexión
No se modificó la lógica QR, Cloudflare, rutas, fallback ni autenticación de conexión.

## Caja
La operación continúa utilizando la caja principal de Windows; no se creó una caja secundaria.

## Compilación
Se verificó estructuralmente el Kotlin modificado (llaves y paréntesis balanceados). La compilación real debe ejecutarse en GitHub Actions/Android SDK porque el entorno de trabajo actual no puede descargar la distribución de Gradle desde `services.gradle.org`.
