# FerrariPOS Manager V15 — Compras, vinculación QR y reconexión

## Android — Compras a proveedores
- Selector de productos reemplazado por una ventana de selección real y táctil.
- Búsqueda directa por descripción o código de barras.
- Al elegir proveedor, los productos se consultan para ese proveedor mediante `supplier_products`.
- Si el proveedor todavía no tiene productos asociados, se muestran los productos activos como respaldo para no bloquear la creación de órdenes.
- Se reorganizó el formulario en pasos: proveedor → producto → cantidad/costo → agregar al carrito.
- El costo se completa automáticamente con el costo del proveedor cuando existe.
- Se puede crear una orden de compra cuando el carrito tiene productos.
- Pulsación prolongada sobre una orden ahora ofrece **MODIFICAR** o **ELIMINAR**.
- Modificación de órdenes en estado DRAFT: carga proveedor y líneas existentes, permite editar y guardar.
- Eliminación: se corrigió el 405 del servidor para aceptar DELETE y se eliminan primero las líneas de la orden.
- Las órdenes ya recibidas/completadas no se borran accidentalmente.

## Windows — Vinculación QR
- El QR ahora contiene una URL de vinculación mucho más compacta.
- Se genera un código corto `FPM3.XXXXXXXX`.
- Se agregó **GENERAR OTRO QR** a la derecha del QR.
- Cada regeneración cambia el código corto.
- El Android reconoce el QR de URL y consulta el código al Windows antes de guardar la vinculación.
- Se conserva compatibilidad con el formato FPM2 anterior para códigos manuales.

## Cloudflare / red
- Se redujo la reconexión automática permanente.
- Ahora hay hasta 3 intentos automáticos, con esperas de 10 y 20 segundos.
- Después se detiene y queda indicado que la reconexión automática fue detenida.
- Esto evita que FerrariPOS esté intentando reconectar cada pocos segundos indefinidamente ante cortes o respuestas 530.

## Nota de compilación
Este paquete contiene los cambios sobre V14. No se ejecutó una compilación Android/Windows completa en este entorno; la validación definitiva debe hacerse en GitHub Actions.
