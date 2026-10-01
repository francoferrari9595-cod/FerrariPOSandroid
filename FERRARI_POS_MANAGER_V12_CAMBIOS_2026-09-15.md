# FerrariPOS Manager V12 — corrección integral

## Mesas / tickets
- Agregar productos a una mesa ocupada ahora usa un endpoint de append atómico del servidor.
- Nunca reemplaza el ticket existente por el último producto.
- Si la mesa fue abierta desde Windows, el servidor recupera el ticket externo y agrega las nuevas líneas.
- El cobro de mesa usa un endpoint específico y toma el ticket real del servidor.
- El cobro de mesa permite cobrar tickets cuyos productos quedaron inactivos después de abrir el ticket, siempre que el producto siga existiendo.

## Escáner
- Se agregó bloqueo de entrega de resultado con `AtomicBoolean`.
- Una lectura de cámara entrega un solo código aunque ML Kit detecte el mismo código en varios frames.
- La venta agrega automáticamente cantidad 1.

## Devoluciones / cancelaciones Windows
- Se conserva el registro histórico de ventas canceladas.
- Daily Tickets e IMP-TICKET incluyen ventas canceladas.
- Ticket cancelado: estado CANCELADO y texto tachado.
- Ticket con devolución: resaltado amarillo.
- Se corrigió `customerId` faltante en SaleReturnService para devoluciones de ventas a crédito.
- La devolución mantiene el reintegro por los medios originales y reduce efectivo cuando corresponde.
- La cancelación revierte inventario y medios de pago y conserva el ticket.

## Promociones Android
- Mantener presionada una promoción abre opciones MODIFICAR / ELIMINAR.
- Se agregó DELETE de promociones al API Windows.
- Se agregó edición de nombre, descripción y precio manteniendo sus productos asociados.

## Centro de Análisis
- Nueva opción inferior junto a Caja: ANÁLISIS.
- Productos más vendidos.
- Productos que hay que pedir.
- Créditos / deudas de clientes.
- Proveedores relacionados con faltantes.
- Distribución de medios de pago.
- Gráficos de barras y gráfico circular.

## Visual / sonido
- Título dividido: FERRARI POS en rojo Ferrari + MANAGER en amarillo.
- Botones con efecto de elevación/glow visual.
- Sonidos de apertura/cierre de caja más largos.

## Compatibilidad
- Se conserva el flujo Android + Windows + instalador.
- No se eliminan las funciones anteriores de V11 intencionalmente.

## Verificación
- Se revisaron balances de llaves `{}` en los archivos modificados.
- No se ejecutó compilación Android/Windows local porque el entorno de trabajo no dispone de las dependencias de build de GitHub (.NET/Gradle remoto).
- La compilación final debe ejecutarse en GitHub Actions.
