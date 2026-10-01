FERRARI POS MANAGER V8 · RENOVACIÓN FUNCIONAL
Fecha: 15/09/2026

CAMBIOS PRINCIPALES
1. DEVOLUCIONES
- La devolución repone inventario cuando el producto usa inventario.
- Reintegra el importe de la devolución usando el/los mismos medios de pago de la venta.
- Si fue EFECTIVO, genera un movimiento negativo en la caja para reducir el efectivo esperado.
- Si fue Mercado Pago, tarjeta, transferencia o crédito, registra el reintegro correspondiente en pagos.
- Si la venta fue a crédito, también corrige la cuenta corriente del cliente.
- Registra motivo y detalle como operación de FerrariPOS Manager para el cierre.

2. CANCELACIÓN DE TICKET
- Nuevo botón CANCELAR TICKET debajo del total en el detalle de las últimas ventas.
- Solicita motivo obligatorio.
- Cancela el ticket, repone el inventario pendiente de devolución y revierte los importes cobrados.
- El efectivo se revierte en caja; el crédito corrige la cuenta corriente.
- El motivo queda detallado en el reporte/correo de cierre.

3. INVENTARIO DESDE ANDROID
- Los ajustes SALIDA/ENTRADA realizados desde Manager ahora quedan auditados con cantidad, producto y motivo.
- El correo de cierre incluye el detalle de estas operaciones.

4. ABONOS
- Los abonos hechos desde FerrariPOS Manager continúan impactando caja/cuenta corriente según el medio de pago y quedan identificados como operación Manager en el reporte de cierre.

5. VENTAS
- La pantalla Ventas ahora es la pantalla principal después de vincularse.
- La venta permite desplazamiento vertical completo cuando hay muchos productos.
- COBRAR, GUARDAR EN MESA, ENVIAR VENTA A WINDOWS y el total permanecen accesibles al desplazar.
- El escáner queda más accesible con botón ESCANEAR destacado.
- Escanear agrega directamente 1 unidad al carrito, sin pedir cantidad.
- El número de cantidad es táctil para editarla directamente.
- Sonido corto de registro al completar una venta.
- Sonido de error para producto inexistente o error de operación.

6. MESAS
- El endpoint móvil incorpora tickets de mesa que estén abiertos en Windows, además de los tickets guardados desde Manager.
- Al cobrar una mesa desde Android se libera también la mesa/ticket correspondiente en Windows.
- Se mantiene el cálculo del total a partir de los productos y cantidades cargados.

7. CAJA
- Sonido corto al abrir caja.
- Sonido corto al cerrar caja.

8. TEMAS
- Se agregaron modos Black, Claro y Celeste además de los temas existentes.
- Claro/Celeste usan esquema Material claro real.
- Se conserva el estilo moderno con paneles translúcidos, bordes y reflejos.

VERIFICACIÓN
- Se revisaron balances de llaves/braces de los archivos modificados.
- No se ejecutó compilación completa local de Android/Windows porque el entorno actual no tiene acceso de red para descargar Gradle y no dispone de .NET SDK.
- El workflow de GitHub Actions sigue siendo el entorno previsto para la compilación completa.
