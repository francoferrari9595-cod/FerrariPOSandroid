# Cambios 2026-09-30 · Stock / QR Android / Protección de precios

## Base y regla de versionado
Esta entrega parte exclusivamente de la versión base recibida en `FERRARI_POS_CORREGIDO_LIVE_STOCK_FINAL.zip`. Las modificaciones se aplican encima de esa base y no se recuperan archivos de versiones antiguas ni se revierten correcciones existentes.

## Productos sin inventario
- En Manager Windows, al activar `ESTE PRODUCTO NO USA INVENTARIO`, los campos `EXISTENCIA / STOCK` y `STOCK MÍNIMO` quedan automáticamente deshabilitados y muestran `NO APLICA`.
- Al guardar un producto sin inventario, el stock y stock mínimo se almacenan como 0 para evitar un stock ficticio que nunca baja.
- El servidor móvil aplica la misma regla para altas/ediciones provenientes del Manager Android.

## Protección contra reducción extraordinaria de precio
- Si el precio de venta baja a menos del 50% del precio anterior, se crea un bloqueo de venta de 3 horas.
- El bloqueo se aplica a ventas Windows y a los cambios de precio provenientes del Manager Android y del panel web.
- Subir nuevamente el precio no elimina el bloqueo: las ventas siguen bloqueadas hasta que finalicen las 3 horas.
- La base conserva el registro `price_sale_blocks` y el historial de cambios de precio existente.

## QR del Manager Android en el panel web
- Windows sigue generando el QR de vinculación con el Quick Tunnel actual.
- El snapshot central sincroniza el payload, código y una imagen PNG del mismo QR.
- El panel web central muestra una sección `CONEXIÓN ANDROID · QR DEL MANAGER` con el mismo QR, código alternativo, URL Quick Tunnel y hora de generación.
- El QR se actualiza automáticamente cuando cambia el enlace público.
- Quick Tunnel es estable durante la vida del proceso; al reiniciar un Quick Tunnel de Cloudflare puede cambiar el hostname, por lo que el QR se vuelve a sincronizar automáticamente.

## GitHub Actions
La workflow entrega exactamente cuatro artefactos independientes:
1. `Windows-Manager-Setup`
2. `Windows-Manager-EXE`
3. `Windows-Licencias-EXE`
4. `Android-Manager`
5. `Android-Licencia`

Nota: son cinco nombres de artefactos porque Windows requiere Setup y EXE separados y Android tiene Manager y Licencia separados; junto con Windows Licencias forman los cinco entregables solicitados.
