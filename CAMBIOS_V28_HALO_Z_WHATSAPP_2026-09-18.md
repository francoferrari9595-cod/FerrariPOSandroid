# FerrariPOS V28 — Correcciones General Z + Halo configurable

## Cambios
- Se restauró y reforzó el botón de envío del Corte Z General en Android.
- El botón ahora aparece como acción de ancho completo: **ENVIAR POR WHATSAPP / MENSAJE**.
- Usa el selector nativo de Android (`ACTION_SEND`) para permitir WhatsApp, Mensajes u otras aplicaciones instaladas en el dispositivo.
- El texto enviado incluye el título del reporte y el contenido completo del Corte Z.
- Se corrigió el halo del título **FERRARI POS MANAGER**: el color seleccionado en Configuración ahora se propaga inmediatamente al encabezado de HomeScreen y también queda guardado en Prefs.
- Se conservan los colores configurables: Verde Flúor, Naranja Flúor, Azul Neón, Rosa Neón, Morado Neón, Cian Neón y Blanco Neón.
- No se modificó la lógica de conexión QR/Cloudflare ni la lógica de licencias offline.

## Verificación
- Se verificó estáticamente que la selección de halo se guarda con `Prefs.saveHalo()` y ahora actualiza el estado del `HomeScreen` mediante callback.
- Se intentó compilar Android localmente, pero el entorno no pudo acceder a `services.gradle.org`; por eso este paquete no se presenta como APK compilado/verificado localmente.
