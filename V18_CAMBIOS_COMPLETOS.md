# FerrariPOS V18 — cambios integrados

## Android Manager
- Tema inicial y predeterminado: **Premium Gris**.
- Fondo de vinculación QR: degradado gris/plateado con iluminación de color.
- `COBRAR` continúa verde neón pulsante.
- Botón de envío del estado de cuenta: diseño responsive a ancho completo para evitar recortes en teléfonos angostos.
- Historial de movimientos de stock desde la caja/base Windows.
- Historial de movimientos de caja principal Windows.
- La caja Android trabaja sobre la **misma caja principal de Windows**. No se crea una caja secundaria ni una base paralela.

## Windows
- F8 Usuarios incorpora permiso `CONTEO_FISICO`.
- Sin ese permiso, `CONTEO FÍSICO` no aparece en Inventario.
- La ventana `InventoryCountForm` también valida el permiso para impedir acceso directo.
- ADMIN mantiene acceso.

## Integridad de conexión
No se modificaron `ApiFactory`, `ConnectionRoute`, pairing QR, Cloudflare, fallback LAN/público ni las rutas de conexión que quedaron estables en V16.6.

## Compilación
Se incluye `.github/workflows/build.yml` para generar automáticamente el APK Release y la compilación Windows en GitHub Actions.
