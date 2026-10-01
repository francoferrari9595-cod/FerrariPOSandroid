# FerrariPOS Android — Tickets Windows: cobro + escáner

## Alcance
Corrección específica del flujo de Tickets Windows desde Android. No se modificaron mesas, QR/conexión, Cloudflare, servidor público, ni configuración general de Windows.

## Cambios
- Corregido el HTTP 500 al cobrar un ticket Windows causado por la deserialización interna del ID estable del ticket. El DTO interno del endpoint acepta el ID como texto, igual que el identificador que Android recibe.
- Android mantiene el ID del ticket como `String` estable.
- En `AGREGAR PRODUCTOS` se agregó botón de escaneo de código de barras.
- El escáner reutiliza el scanner de cámara/ML Kit ya existente en la app.
- Al detectar un código, Android busca el producto por código, lo selecciona y permite indicar cantidad antes de agregarlo al ticket Windows.
- Se mantiene también la búsqueda manual por descripción/código y selección de producto.
- El botón `COBRAR` continúa usando el diálogo de medios de pago existente y, una vez aceptado, recarga la lista de tickets para retirar el ticket ya cobrado.

## Verificación
El entorno de esta sesión no pudo ejecutar Gradle porque el wrapper intentó resolver `services.gradle.org` y el DNS de ejecución no tiene acceso externo. No se afirma que el APK haya sido compilado aquí.
