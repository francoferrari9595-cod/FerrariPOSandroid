# V43 — Corrección PRODUCTO EN COMÚN Android → Windows

## Problema detectado
El botón **PRODUCTO EN COMÚN** del Manager Android creaba una línea con `ProductId=0`. El producto especial de Windows está reservado como `barcode='__COMUN__'` y no forma parte del listado normal de productos. Al importar una mesa o una venta pendiente, Windows intentaba buscar el ID 0 en `products`; como no existía, descartaba la línea completa. Esto hacía que pareciera que el producto común nunca se había enviado.

## Corrección aplicada
- Android obtiene el ID real del producto especial mediante `GET /api/mobile/producto-comun` antes de guardar la línea.
- Windows normaliza cualquier línea antigua `IsCommon=true` con `ProductId<=0` al ID real de `__COMUN__` al recibir datos móviles.
- Windows importa `IsCommon` como una línea especial directamente desde `__COMUN__`, sin depender del ID enviado por una versión antigua del Manager.
- Se aplica la protección tanto a **mesas/tickets abiertos** como a **ventas pendientes**.
- La venta directa Android también normaliza el ID antes de llegar a `SaleService`, evitando `product_id=0` en `sale_items`.
- Se mantiene `IsCommon=true`, `UsesInventory=false` y stock 0, por lo que no modifica inventario.

## Integridad visual
No se modificaron `SalonForm.cs`, `TableService.cs`, `ThemeService.cs` ni los recursos `Resources/tables3d`.
