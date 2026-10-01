# FerrariPOS Manager V17.0 — Compras Android

## Objetivo
Corrección exclusiva del módulo de Compras del Manager Android, manteniendo intacta la conexión QR/red/Cloudflare de V16.9.

## Cambios
- Se reemplazó el selector de proveedores por un diálogo táctil robusto con botones grandes.
- Se reemplazó el selector de productos por un diálogo táctil robusto con búsqueda por descripción/código.
- Se mantiene el flujo Proveedor → Producto → Cantidad/Costo → Carrito → Crear orden.
- Se agregaron acciones para las órdenes: modificar, recibir mercadería, recibir con diferencia y eliminar.
- La recepción muestra pedido, recibido previamente y pendiente.
- La recepción normal permite recibir las cantidades pendientes.
- La recepción con diferencia permite excedentes o cierre con pendientes y exige motivo.
- La recepción actualiza stock y costo mediante el mismo almacenamiento del POS, y registra movimiento de compra.
- Se agregaron los endpoints mínimos de API de compras al servidor Windows únicamente para soportar recepción desde Android. No se modificó ninguna lógica de conexión ni la interfaz de Windows.
- Se agregaron cuatro temas visuales modernos: Cyber Neon, Aurora, Graphite y Quantum.
- Android no modifica la lógica de conexión QR, fallback LAN, Cloudflare, token ni rutas de conexión.

## Validación
- Conteo de llaves/paréntesis equilibrado en los archivos Kotlin modificados.
- Se intentó `assembleDebug --offline`; el entorno no tiene la distribución Gradle disponible y el wrapper intentó resolver `services.gradle.org`, por lo que la compilación completa queda para GitHub Actions.
