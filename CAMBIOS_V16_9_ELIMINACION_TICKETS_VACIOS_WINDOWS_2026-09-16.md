# FerrariPOS V16.9 — Eliminación de tickets vacíos en Windows

## Cambio solicitado

Esta versión modifica únicamente el programa de Windows. Android se mantiene sin cambios.

### Comportamiento
- Los tickets pendientes de Venta Móvil recibidos desde Android siguen pudiendo eliminarse desde Windows.
- Los tickets de mesa móviles sin productos válidos se eliminan automáticamente del almacenamiento persistente y no vuelven a aparecer con el refresco.
- Las ventas móviles pendientes sin productos válidos también se eliminan automáticamente de `mobile_pending_tickets`.
- Los tickets con productos continúan solicitando confirmación antes de eliminarse.
- Los tickets vacíos locales continúan pudiendo eliminarse sin confirmación mediante `Eliminar ticket`.
- Se conserva la lógica de conexión QR, sincronización, mesas, cobro y Android de V16.8.

## Publicación GitHub

Reemplazar el contenido del repositorio con el contenido de este ZIP y conservar la rama `main`.

El workflow existente `.github/workflows/build-ferrari-pos-completo.yml` continúa generando Android y Windows como artefactos separados.
