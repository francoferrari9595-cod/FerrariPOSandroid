# FerrariPOS Manager V16.4 — QR estable + reportes por departamento + neón

Base: V16.3 Android compilación corregida.

## Android
- Pantalla inicial de instalación/vinculación muestra directamente la pantalla para escanear el QR, sin pantalla intermedia que obligue a entrar a F7.
- Se conserva Configuración/F7 para reconectar posteriormente.
- El cliente Android guarda URL pública + URL LAN de respaldo.
- Las peticiones GET reintentan errores transitorios y, ante 530/fallo de red, prueban automáticamente la URL LAN guardada.
- Las escrituras POST/PUT/DELETE no se reintentan para evitar duplicar ventas, abonos, stock o cierres.
- Timeouts y recuperación de conexión ampliados.
- Botón COBRAR: verde neón, texto grande, halo y pulsación/titileo fuerte.
- Título FERRARIPOS blanco + MANAGER rojo neón.

## Windows / QR
- El QR de vinculación contiene la configuración completa, incluyendo URL pública, LAN y token, reduciendo una petición dependiente de Cloudflare durante la vinculación.
- Renovar el QR ya no mata ni reinicia cloudflared.
- El health-check público ya no reinicia el Quick Tunnel por una caída temporal de 530; se conserva la URL mientras cloudflared siga vivo.
- Un reinicio real de cloudflared sigue siendo detectable y genera un nuevo enlace cuando corresponde.

## Reportes
- Reporte VENTAS POR CATEGORÍA permite seleccionar un departamento/categoría concreta.
- Filtra por DESDE/HASTA y por categoría.
- Muestra Efectivo, Mercado Pago, Tarjeta, Transferencia, Crédito y Total vendido.
- El resumen superior muestra explícitamente el total vendido del departamento seleccionado.
