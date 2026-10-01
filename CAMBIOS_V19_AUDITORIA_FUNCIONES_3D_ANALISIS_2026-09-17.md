# FerrariPOS V19 — auditoría funcional y mejoras Android/Windows

## Auditoría realizada
Se revisó el conjunto Windows + Android y las rutas `/api/mobile/*` existentes antes de preparar la compilación. Se mantuvo la arquitectura de Windows como fuente de datos y Android como cliente sincronizado.

## Android agregado/mejorado
- Reportes detallados del día: ventas, tickets, promedio, ventas por hora, categoría y medio de pago.
- Auditoría de operaciones sincronizadas desde Windows/Android.
- Historial de caja con posibilidad de anular movimientos manuales y registrar motivo.
- Edición/eliminación de proveedores con motivo de baja.
- Edición y eliminación de promociones.
- Gráfico de medios de pago con colores explícitos por segmento, leyenda y efecto de profundidad 3D simulado.
- Tarjetas, métricas, listas y panel general con elevación, sombras, degradados y profundidad visual 3D.
- Fondo general Premium Gris con iluminación.
- Botón ENVIAR del estado de cuenta de clientes adaptado a pantallas angostas y textos de dos líneas.

## Windows
- Se conserva `CONTEO_FISICO` como permiso individual.
- El botón de CONTEO FÍSICO solo aparece para usuarios autorizados; ADMIN conserva acceso.
- El servidor móvil agrega las operaciones necesarias para las nuevas funciones Android.

## Caja
Android utiliza exclusivamente la caja principal de Windows. No se creó una caja secundaria ni una contabilidad paralela.

## Conexión — NO MODIFICADA
No se modificaron las clases/rutas de conexión QR, Cloudflare, fallback, `ConnectionRoute` ni el mecanismo de autenticación existente.

## Verificación previa
- Balance de llaves `{}` y paréntesis `()` revisado en Kotlin y archivos C# modificados.
- Rutas Retrofit nuevas comprobadas contra rutas existentes del servidor.
- Se verificó la presencia de `CONTEO_FISICO` en permisos y control de acceso Windows.
- Se revisó el workflow de GitHub para Android y Windows.
- Este entorno no dispone de SDK Android/Gradle descargado ni .NET SDK, por lo que la compilación final debe ejecutarse en GitHub Actions. No se presenta un binario no probado como si fuera compilado.
