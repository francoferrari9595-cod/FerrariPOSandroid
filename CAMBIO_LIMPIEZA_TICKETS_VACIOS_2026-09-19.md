# FerrariPOS — limpieza automática de pestañas vacías

Cambio puntual sobre Windows MainForm.cs.

- Si existen ventas con productos, las pestañas locales sin productos se eliminan automáticamente.
- Si todas las ventas locales están vacías, se conserva exactamente una pestaña vacía.
- No se eliminan ni modifican tickets vinculados a mesas.
- No se eliminan ni modifican tickets móviles.
- Al compactar índices se remapean las asociaciones existentes para no cambiar su referencia.
- No se modificaron conexiones, Cloudflare, mesas, Android ni servidor.
