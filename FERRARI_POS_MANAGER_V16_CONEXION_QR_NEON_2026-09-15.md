# Ferrari POS Manager V16 · Conexión robusta + QR + Neon

Base: V15 COMPRAS / QR / RECONEXIÓN.

- Quick Tunnel con monitor de salud cada 15 s contra `/api/mobile/ping`.
- Si el enlace público deja de responder dos comprobaciones seguidas, se reinicia cloudflared y se obtiene una URL nueva.
- Reconexión inteligente con espera 10 → 20 → 40 → 60 s, sin polling agresivo cada 2/3/5 s.
- El botón `GENERAR OTRO QR` renueva el código corto y fuerza la renovación del Quick Tunnel.
- El QR usa la URL compacta `/api/mobile/vincular/FPM3.XXXXXXXX`, no un payload largo.
- El login de Windows muestra un QR pequeño antes de ingresar usuario/contraseña y permite regenerarlo.
- Se conserva compatibilidad del código FPM2 anterior.
- Android acepta el QR corto y conserva el flujo de vinculación existente.
- `COBRAR` en Android tiene un pulso lento de neón: verde en temas oscuros; amarillo en temas claros.
- Identidad superior Android: `FERRARI POS` naranja + `MANAGER` verde fosforescente.
- Se conserva la funcionalidad de compras/proveedores, órdenes, mesas y sincronización de V15.

No se afirma compilación local completa: este entorno no dispone de las dependencias externas de Gradle/.NET necesarias para una verificación final.


## Corrección V16.1 · conexión Android / QR / Cloudflare
- Se eliminó el reinicio agresivo del Quick Tunnel después de solo dos fallos de salud. Ahora se conserva la misma URL durante cortes transitorios y recién se considera reiniciar tras 90 segundos continuos sin respuesta.
- La vinculación móvil conserva el token y agrega una URL LAN de respaldo en el payload.
- Android reintenta automáticamente solicitudes GET ante 502/503/504/530 o fallos de red, sin repetir POST/PUT/DELETE para evitar ventas o movimientos duplicados.
- Un error 530 durante la vinculación ya no borra la configuración existente.
- El QR de vinculación queda concentrado en F7 · Configuración; la pantalla inicial sin conexión no muestra el escáner QR.
- Se actualizó la estética a negro grafito, blanco y rojo Ferrari neón, con botón COBRAR pulsante de mayor brillo y contraste.
