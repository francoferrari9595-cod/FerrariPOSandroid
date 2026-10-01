# Cambios — SOLO Android — 2026-09-25

Base: `FERRARI_POS_WINDOWS_ANDROID_CORREGIDO_BORRADO_MOVIL_2026-09-25.zip`.

Se modificó exclusivamente `FerrariPOS.Manager.Android` y solo para los comportamientos solicitados:

1. Ajuste de stock: el motivo es obligatorio únicamente para `SALIDA`. Si falta, se muestra aviso con icono de exclamación y no se envía el movimiento.
2. Carrito de ventas Android: eliminar una línea de promoción (`PROMO · ...`) exige motivo obligatorio antes de quitarla.
3. Carrito de ventas Android: aplicar un descuento a un artículo que no sea promoción exige motivo obligatorio. El motivo se conserva como `discountReason` al registrar/enviar la venta.
4. Eliminación de cliente: ya tenía motivo obligatorio; se conserva sin cambios.
5. Gestión de promociones: ya tenía motivo obligatorio al eliminar una promoción; se conserva sin cambios.

No se modificó Windows.
No se modificó la visual ni otras estructuras Android fuera de estos comportamientos.
