# FerrariPOS Manager Android — correcciones UI/funciones 2026-09-24

Se modificó exclusivamente `FerrariPOS.Manager.Android`.

## Clientes
Al mantener presionado un cliente aparecen cinco acciones visibles:
1. Editar cliente
2. Estado y detalle de cuenta corriente
3. Abonar deuda
4. Enviar comprobante de deuda
5. Eliminar cliente

`Público General` continúa protegido.

## Órdenes de compra
Cada orden se muestra con estado visual persistente:
- Violeta: recibido con diferencia
- Verde: recibido completo
- Amarillo: en proceso
- Rojo: rechazado

El gesto de mantener presionada la orden conserva las acciones de modificar, recibir, recibir con diferencia y eliminar.

## Inicio / tema
Se conserva el arranque negro/neón y el tema visual existente de FerrariPOS Manager.

## Windows
`FerrariPOS.Windows` no fue modificado. Se verificó comparando todos sus archivos contra la versión recibida antes de los cambios Android.
