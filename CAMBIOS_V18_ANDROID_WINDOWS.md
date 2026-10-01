# FerrariPOS V18 — Android Manager + Windows

- Android inicia por defecto con tema **Premium Gris** y mantiene COBRAR verde neón pulsante.
- Pantalla inicial de vinculación QR con fondo premium con iluminación radial; no se modificó la lógica de conexión, QR, Cloudflare, rutas ni fallback.
- Corregido el botón ENVIAR del estado de cuenta de clientes para adaptarse a pantallas angostas; ocupa ancho completo y no queda cortado.
- Android expone historial de movimientos de stock y caja mediante los endpoints ya existentes.
- Caja Android sigue siendo la **caja principal de Windows**: apertura, movimientos, cobros y cierre trabajan sobre la misma caja; no se crea una segunda caja ni una base paralela.
- Windows: nuevo permiso `CONTEO_FISICO` en F8 Usuarios. Si el usuario no lo tiene, el botón CONTEO FÍSICO no aparece y la ventana también bloquea acceso directo. ADMIN conserva acceso.
