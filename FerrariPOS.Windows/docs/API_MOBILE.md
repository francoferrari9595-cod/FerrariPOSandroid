# FerrariPOS Manager · API móvil

La API móvil de FerrariPOS usa `X-FerrariPOS-Token` y se publica desde el mismo servidor que ya utiliza el panel web/Cloudflare.

## Productos
- `GET /api/mobile/productos?q=`
- `GET /api/mobile/productos/{id}`
- `POST /api/mobile/productos`
- `PUT /api/mobile/productos/{id}`
- `PUT /api/mobile/productos/{id}/precio`
- `POST /api/mobile/productos/{id}/stock`

## Clientes
- `GET /api/mobile/clientes`
- `POST /api/mobile/clientes`
- `POST /api/mobile/clientes/{id}/abono`

## Ventas y devoluciones
- `GET /api/mobile/ventas`
- `POST /api/mobile/ventas`
- `GET /api/mobile/ventas/{id}/ticket`
- `POST /api/mobile/devoluciones/{saleItemId}`

## Caja
- `GET /api/mobile/caja`
- `POST /api/mobile/caja/apertura`
- `POST /api/mobile/caja/movimiento`
- `POST /api/mobile/caja/cierre`

## Inventario/Excel
Las operaciones que modifican productos, stock, ventas o devoluciones intentan actualizar `Documentos/Ferrari'sPOS/FerrariPOS_Inventario_Actual.xlsx`. Si Excel está abierto, la operación de base de datos no se revierte: el Excel se puede regenerar después.


## Administración de licencias desde Android
- `POST /api/mobile/license/activate`
- Requiere `X-FerrariPOS-Token` como las demás operaciones móviles.
- Body JSON: `{ "token": "FPOS-LIC-3...." }`.
- El servidor verifica la firma RSA y el Machine ID mediante `LicenseService` antes de aplicar ACTIVATE/RENEW/DEACTIVATE.
