# Corrección build Android — 2026-09-20

Se corrigió el error de compilación de `MainActivity.kt` que provocaba:

`Unresolved reference: onCartChange` en la función `Purchases`.

La pantalla de Compras ahora limpia su propio `cart` con `cart=emptyList()` después de guardar una orden, en lugar de intentar utilizar el callback `onCartChange`, que pertenece a `SalesTerminal`.

También se conserva el comportamiento solicitado en Ventas: al seleccionar un producto desde el buscador, el campo de búsqueda se vacía automáticamente y se limpian los resultados visibles para poder buscar inmediatamente otro producto.

No se modificaron las conexiones, QR, mesas, Windows ni otros componentes fuera de estos cambios de Android.
