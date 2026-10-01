# FerrariPOS V39 — interfaz 3D + proveedor compacto + License Admin responsive

## Windows POS
- El importe TOTAL de la pantalla principal de venta usa ahora un renderizado de texto 3D profesional directamente sobre el control existente.
- Se conserva el mismo espacio/tamaño exterior del TOTAL.
- La tipografía mantiene 40 pt mientras el importe entra; cuando el número crece, se reduce progresivamente hasta un mínimo seguro para evitar cortes, desplazamientos o barras horizontales.
- Se conserva el comportamiento de alineación: centrado normalmente y alineado a la derecha cuando el importe necesita más ancho.
- Se mantienen sombra, extrusión, borde y brillo mediante dibujo vectorial, sin agregar imágenes/fotos.

## Productos Windows
- El selector PROVEEDOR fue movido inmediatamente debajo de "SE VENDE POR GRANEL" y "ESTE PRODUCTO NO USA INVENTARIO".
- El selector ahora ocupa el mismo bloque de campos del producto, con botón + para crear proveedor sin el hueco vertical anterior.
- Los botones de IVA, redondeo, búsqueda y gestión fueron reacomodados para mantener una secuencia compacta y sin superposiciones.
- La lógica existente de carga/guardado/asignación del proveedor no fue reemplazada.

## Android — Administrador de Licencias
- Se eliminó el layout que permitía que los botones ACTIVATE / RENEW / DEACTIVATE y CUSTOM / ANNUAL / PERMANENT se comprimieran y partieran en varias líneas.
- Las opciones ahora son tres botones de ancho proporcional, con altura fija y texto de una sola línea.
- "DÍAS EXACTOS" queda en su propia fila completa.
- Se conserva la generación offline, la firma RSA-PSS, el formato FPOS-LIC-3, copiar y enviar.
- Se redujo ligeramente el bloque superior para aprovechar mejor la pantalla sin cambiar la funcionalidad.

## Verificación
- Se revisaron balance de llaves/paréntesis de los archivos modificados.
- El parser de Kotlin no reportó errores sintácticos; la compilación completa local de Gradle no pudo ejecutarse porque el entorno de esta sesión no tiene acceso a `services.gradle.org` para descargar Gradle 8.11.1.
- No se modificó la lógica de licencias ni la clave privada existente.
