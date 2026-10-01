# FerrariPOS V40 — TOTAL 3D Graphite/Cyan y corrección de recorte

## Windows — pantalla principal de venta
- Se corrigió el recorte que hacía desaparecer/cortar parte del precio principal del carrito.
- El TOTAL ahora se dibuja como texto 3D vectorial dentro del mismo control existente, sin fotografías ni imágenes.
- El acabado usa el mismo lenguaje visual de las mesas Graphite: cara cian luminosa, degradado, extrusión oscura por capas, sombra, contorno y bisel/brillo.
- La medición del tamaño se realiza con `GraphicsPath`, igual que el renderizado, para que la cifra que se mide sea exactamente la que se dibuja.
- El tamaño se mantiene hasta 40 pt cuando hay espacio y se reduce progresivamente cuando el importe crece.
- Se considera también el alto disponible y el margen necesario para la extrusión 3D, evitando que las últimas o primeras cifras queden fuera del Label.
- El control conserva su mismo tamaño, posición, comportamiento de click/descuento y recursos existentes.
- No se agregaron imágenes para producir el efecto 3D.

## Verificación
- Se revisaron los archivos modificados y el balance estructural de llaves/paréntesis.
- El entorno de esta sesión no dispone del SDK de .NET, por lo que no fue posible ejecutar una compilación local de Windows aquí.
