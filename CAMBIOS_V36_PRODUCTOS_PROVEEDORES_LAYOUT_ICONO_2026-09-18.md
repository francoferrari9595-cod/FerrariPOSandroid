# V36 — Productos + proveedores + layout + icono

- Windows Productos: F10, IVA y redondeo quedan en la misma fila; proveedor con selector y botón + en la pantalla de producto.
- Windows: al guardar un producto se guarda/asigna el proveedor en supplier_products.
- Android: alta/edición de producto incorpora selector de proveedores, creación rápida con + y asignación al guardar.
- API: endpoint PUT /api/mobile/productos/{id}/proveedor; las respuestas de producto incluyen supplierId/supplierName.
- Android: Producto en común queda visible inmediatamente debajo de Promociones dentro de la tarjeta de venta, evitando espacio negro intermedio.
- Iconos Windows y License Manager convertidos a ICO multi-resolución usando el logo FerrariPOS existente.
- No se modifican QR, Cloudflare, pairing ni rutas de conexión.
