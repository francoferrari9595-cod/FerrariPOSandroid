# FerrariPOS — Corrección Android cobro Windows + crédito + UI + aviso Windows

Fecha: 2026-09-20

## Android
- Se reforzó el flujo de cobro de tickets Windows para evitar carreras de estado al cerrar el diálogo después de un cobro exitoso.
- Se agregó bloqueo de doble toque durante el procesamiento del cobro.
- El diálogo ya no ejecuta `onClose()` dos veces después de `onPaid()`.
- Los errores de servidor se muestran sin cerrar la aplicación.
- Si el servidor rechaza una operación por límite de crédito, se muestra una alerta visual rojo neón y no se registra la venta a crédito.
- Se consulta el saldo/límite actual del cliente antes de permitir una venta a crédito.
- Se evita que dos toques rápidos puedan intentar cargar dos veces el mismo crédito desde Android.
- Se agregó el tema `Negro & Blanco Neón`.
- Se remodelaron los accesos rápidos con tarjetas, iconografía, profundidad y navegación visual más moderna, sin eliminar accesos existentes.

## Windows
- `SaleService` serializa las operaciones de venta para que solicitudes simultáneas no puedan superar el límite de crédito leyendo el mismo saldo antes de registrar.
- Al cobrar un ticket Windows desde Android, Windows muestra una notificación inferior temporal con ticket, total y texto `COBRO DESDE ANDROID MANAGER`.
- La notificación parpadea rápidamente y desaparece sola.
- Se reproduce el sonido de caja existente; la notificación está protegida para que nunca pueda afectar el cobro.

## Preservado
- Mesas 3D y sus recursos `Resources/tables3d`.
- Conexiones, QR, Cloudflare y servidor público.
- Tickets Windows y Android.
- Escáner de productos.
- Funciones existentes de ventas, clientes, caja, productos y reportes.
