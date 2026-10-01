# FerrariPOS — Windows V44 + Android Manager actual

## Base preservada
- Windows Manager: base V44 `TOTAL 3D + BUILD FIX`, sin reemplazar ni rediseñar sus mesas 3D ni su total/precio 3D.
- Se conserva `FerrariPOS.Windows/Resources/tables3d` íntegramente.

## Android Manager incorporado
Se reemplazó únicamente `FerrariPOS.Manager.Android` por la versión Android actual del proyecto, conservando sus funciones recientes:
- carrito persistente
- cobro de tickets Windows
- crédito de clientes
- escaneo normal y masivo
- botón ESCANEAR rojo flúor con titileo
- venta por granel precio ↔ cantidad
- título 3D
- mesas 3D Android
- conexión QR/Cloudflare y funciones existentes

## Tema Android
El tema inicial/fallback es `Negro & Blanco Neón`.
La preferencia guardada por el usuario sigue respetándose; si no existe preferencia, la app inicia en `Negro & Blanco Neón`.

## Protección 3D
`BACKUP_3D_PROTEGIDO/` contiene copias de los assets 3D de Windows y Android y `SHA256SUMS.txt`.
