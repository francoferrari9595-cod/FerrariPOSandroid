# FerrariPOS Manager V16.7 — Mesas y ventas Android → Windows

## Correcciones
- Android ahora interpreta correctamente los tickets de mesas enviados por Windows aunque el JSON original use propiedades PascalCase. Esto evita mostrar `0.00 × null` y permite calcular el total real de la mesa.
- Windows API ahora devuelve JSON móvil con nombres camelCase de forma consistente.
- Se restauró `POST /api/mobile/ventas-pendientes`, que faltaba en V16.6. Las ventas enviadas desde Android se guardan en `mobile_pending_tickets`.
- Windows MainForm ya tenía el importador periódico de esos tickets cada 2,5 segundos: aparecen como `VENTA MÓVIL #...`, quedan en una pestaña/ticket de fondo y pueden cobrarse posteriormente con el botón normal de cobro de Windows. Al cobrar, el ticket pendiente se elimina de la cola.
- Se conserva íntegramente la conexión QR/Cloudflare/LAN estable de V16.6.

## No se cambia
- No se toca la lógica de reconexión QR de V16.6.
- No se reemplaza la venta por una venta cobrada: `ENVIAR VENTA A WINDOWS` sigue significando dejarla pendiente hasta que Windows la cobre.
