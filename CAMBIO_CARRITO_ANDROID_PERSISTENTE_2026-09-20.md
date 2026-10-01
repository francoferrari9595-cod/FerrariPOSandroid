# Corrección — carrito de Ventas Android persistente al cambiar de sección

Fecha: 2026-09-20

## Cambio
El carrito de la pantalla **Ventas** dejó de vivir dentro de `SalesTerminal` y ahora vive en `HomeScreen`, que permanece montado mientras el usuario cambia entre las secciones de la aplicación.

Por lo tanto, al ir de **Ventas** a Productos, Clientes, Caja, Análisis, Inicio, etc. y volver a Ventas, los artículos cargados permanecen en el carrito.

## Cuándo se vacía
El carrito se vacía únicamente cuando corresponde a una acción explícita:
- botón **LIMPIAR**;
- cobro/venta completada correctamente;
- guardado en mesa;
- envío de la venta a Windows correctamente.

No se vacía por navegar entre secciones.

## Alcance
No se modificaron conexiones, QR, Cloudflare, servidor, mesas 3D, tickets Windows ni el resto de funciones.

## Compilación
Se intentó ejecutar `./gradlew :app:compileReleaseKotlin`, pero el entorno no pudo resolver `services.gradle.org` (DNS/red). Por ese motivo no se declara una compilación local exitosa.
