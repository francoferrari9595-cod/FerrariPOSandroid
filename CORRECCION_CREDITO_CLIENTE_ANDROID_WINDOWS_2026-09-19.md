# Corrección crédito automático por cliente — 2026-09-19

## Android Manager
- Al seleccionar un cliente real (ID > 1), al abrir **COBRAR** la venta inicia automáticamente en **CRÉDITO**.
- Se muestra claramente que el total completo se cargará a la cuenta corriente del cliente.
- Se mantiene la opción **MIXTO** para repartir el total entre crédito y otros medios.
- En MIXTO, el campo **CRÉDITO** forma parte de la distribución y el cobro debe completar exactamente el total de la venta.
- Una venta 100% a crédito no exige que la caja esté abierta porque no mueve efectivo.
- Si la combinación MIXTA contiene efectivo, sí exige caja abierta.
- Después de una venta, Android vuelve a consultar clientes para actualizar inmediatamente la deuda mostrada.

## Windows / API
- La API móvil ya no bloquea una venta 100% a crédito por tener la caja cerrada.
- `SaleService` conserva la lógica de cuenta corriente: cualquier importe con medio **CRÉDITO** se registra en `customer_accounts` asociado al cliente y al ticket.
- En una venta MIXTA, solamente la parte asignada a **CRÉDITO** aumenta la deuda; el resto queda registrado en sus medios correspondientes.
- El mismo registro SQLite utilizado por Windows alimenta el historial de cuenta corriente visible desde Windows y Android.

## Validaciones
- No se modificó la lógica de productos, inventario, mesas ni sincronización QR.
- La compilación Android fue intentada en este entorno, pero Gradle no pudo resolver `services.gradle.org` por falta de DNS/red; por eso no se declara un APK compilado aquí.
