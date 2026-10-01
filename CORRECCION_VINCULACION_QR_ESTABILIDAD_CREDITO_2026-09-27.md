# FerrariPOS — QR, estabilidad y crédito — 27/09/2026

- El QR de vinculación y la URL pública quedan disponibles para cualquier usuario/cajero desde la barra principal. No requiere ADMIN.
- La pantalla de vinculación muestra QR, URL pública Cloudflare, payload y código alternativo.
- Los endpoints públicos de vinculación siguen disponibles sin autenticación previa; la autenticación normal se mantiene para las operaciones de Manager.
- SQLite usa WAL + busy_timeout + synchronous NORMAL para reducir bloqueos cuando Android y Windows trabajan sobre mesas/ventas simultáneamente.
- Los fallos del sincronizador de mesas se registran sin cerrar ni bloquear el POS.
- Los medios de pago Android se normalizan antes de guardar abonos y ventas, incluyendo variantes dañadas como `cre??dito`, `cr?dito` o `cr�dito`.
- La venta conserva la normalización previa antes de calcular crédito y crear el movimiento de cuenta corriente.
