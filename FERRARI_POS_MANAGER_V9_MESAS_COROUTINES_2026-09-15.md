FERRARI POS MANAGER V9 — CORRECCIÓN MESAS + COROUTINES

Base: V8

CAMBIOS
1. Una mesa ocupada ya NO queda bloqueada para agregar productos.
2. Al seleccionar una mesa ocupada, Manager obtiene el ticket abierto sincronizado y agrega los nuevos productos al ticket existente; no lo reemplaza.
3. Si la mesa está ocupada por Windows, se fuerza una actualización del ticket abierto antes de informar error.
4. El ticket actualizado se guarda nuevamente en Windows mediante /api/mobile/tickets-abiertos y conserva cliente/notas del ticket existente.
5. La mesa continúa apareciendo ocupada y sus productos quedan acumulados para el posterior cobro.
6. Se mantiene la liberación/cobro existente de V8.
7. Se evita crear una segunda venta/ticket independiente para cada nuevo pedido de una misma mesa.

COROUTINES
- Se mantiene el uso de rememberCoroutineScope solamente para operaciones iniciadas por acciones de UI y se evita depender de un scope externo o persistente.
- El guardado de mesa completa la operación de red antes de cerrar el diálogo.
- El mensaje “coroutine scope left the composition” no debe convertirse en un error de operación: Compose cancela automáticamente un rememberCoroutineScope al salir de composición. La corrección evita usarlo como scope persistente para el ticket.

NOTA DE COMPILACIÓN
No se ejecutó una compilación Gradle completa local porque este entorno no tiene acceso DNS a services.gradle.org. La validación final se debe realizar en GitHub Actions.
