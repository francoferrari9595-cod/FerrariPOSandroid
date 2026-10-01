# FerrariPOS V17.2

## Android
- Reemplazada la selección de proveedor por un `AlertDialog` estándar con botones Material 3 directamente táctiles.
- Reemplazada la selección de producto por el mismo patrón robusto.
- Se conserva proveedor -> producto -> carrito -> orden -> recepción.
- No se modificó ninguna lógica de conexión QR, red, Cloudflare, token, fallback ni reconexión.

## Windows / horarios
- Los reportes que muestran fecha/hora convierten timestamps SQLite UTC a hora local del equipo Windows mediante `localtime`.
- Calendario de ventas y movimientos de stock muestran hora local.
- Las respuestas de ventas, compras y caja para Android también reciben la hora local del equipo Windows.
- La fecha almacenada en SQLite no se reescribe: solo se corrige la presentación, evitando alterar datos históricos.
