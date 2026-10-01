# FerrariPOS Manager · Terminal móvil completo

Esta versión amplía el Manager para trabajar sobre la misma base de datos del FerrariPOS de Windows mediante la API móvil autenticada por QR/token.

## Operaciones móviles
- Vinculación por QR o URL/token.
- Panel de ventas, tickets, stock bajo y deuda.
- Alta y edición de productos.
- Escaneo de código de barras con cámara.
- Precio de venta, costo, mayorista, stock, mínimo, categoría, unidad e inventario.
- Ajustes de stock de entrada/salida.
- Ventas desde el teléfono con carrito.
- Consulta de tickets y devolución de líneas de venta.
- Compartir ticket por el menú de Android (WhatsApp, correo u otra app compatible).
- Alta de clientes y registro de abonos.
- Apertura, ingresos/egresos y cierre de caja.
- Sincronización automática de una copia Excel del inventario en Documentos/Ferrari'sPOS cuando se modifica inventario desde Manager.

## Fuente de datos
Windows sigue siendo la fuente de verdad. Android nunca abre ni modifica directamente el archivo SQLite ni un XLSX remoto. Las operaciones pasan por la API de FerrariPOS, que actualiza SQLite y genera la copia Excel de inventario.

## Seguridad
El QR entrega la URL LAN y un token aleatorio. Todas las rutas `/api/mobile/*` exigen `X-FerrariPOS-Token`.

## Compilación
El workflow de GitHub Actions existente se conserva. Usa JDK 17 y Gradle 8.9, por lo que no depende del `gradlew` Linux del paquete.
