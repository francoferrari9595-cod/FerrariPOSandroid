# FerrariPOS V32 — Splash Android moderno

Se modificó únicamente la pantalla de inicio del FerrariPOS Manager Android.

## Cambios
- Fondo negro profundo (#02040A).
- Halos rojos, violetas y cian con movimiento suave.
- Destellos/neones pequeños que titilan de forma animada.
- Líneas de luz diagonales tipo light-trail.
- Halo dorado/rojo alrededor del logo existente.
- Se conserva el recurso `ferrari_splash_logo` sin reemplazarlo ni rediseñarlo.
- Título FERRARIPOS / MANAGER con acabado más limpio.
- Barra de progreso neón animada y texto `INICIANDO SISTEMA`.
- Se conserva el sonido MP3 de inicio y el tiempo de arranque existente.
- No se modificaron pairing, QR, Cloudflare, API, botones, configuraciones ni lógica del Manager.

## Verificación
- Conteo estructural de llaves/paréntesis de `MainActivity.kt`: balanceado.
- No se pudo ejecutar el build Android local porque el entorno no puede resolver `services.gradle.org` para descargar Gradle 8.11.1.
