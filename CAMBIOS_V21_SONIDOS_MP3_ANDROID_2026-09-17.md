# V21 — Sonidos MP3 reales en Android

Cambio separado sobre V20, sin tocar QR, Cloudflare, conexión, API ni sincronización.

- Se reemplazó el uso de tonos del sistema para los eventos solicitados por archivos MP3 reales incluidos dentro de `app/src/main/res/raw`.
- Al iniciar FerrariPOS Manager se reproduce un breve jingle de entrada durante la pantalla de inicio.
- Al guardar correctamente un INGRESO se reproduce `ferrari_income.mp3`.
- Al guardar correctamente un EGRESO se reproduce `ferrari_expense.mp3`.
- Los MP3 son estéreo, 44.1 kHz y 192 kbps.
- Se conserva el ajuste existente de sonidos: si el usuario desactiva sonidos, los MP3 no se reproducen.
- Los sonidos anteriores de escáner/error y las vibraciones existentes se conservan.
- Los recursos se reproducen con `MediaPlayer`, no con `ToneGenerator`, para los tres eventos nuevos.

La compilación local no pudo verificarse porque el entorno no puede resolver `services.gradle.org`; esto no modifica el código ni los recursos del proyecto.
