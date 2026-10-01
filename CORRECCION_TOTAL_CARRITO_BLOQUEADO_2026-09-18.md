# Corrección TOTAL principal del carrito — 2026-09-18

- Se corrigió el renderizado 3D del TOTAL principal en `FlickerFreeLabel`.
- El cálculo usa las dimensiones reales del control, sin mínimos artificiales que podían hacer que el texto quedara fuera durante reacomodos.
- El texto se ajusta automáticamente por tamaño y compresión horizontal para que entre completo.
- Se agregó un clip de seguridad al área útil del Label para evitar que la geometría 3D invada otros controles.
- Se mantienen el TOTAL 3D cian, el carrito, las mesas 3D, Producto Común, la corrección de compilación y el resto de cambios anteriores sin modificar su lógica.
- No se generó un EXE/Setup en este entorno porque no dispone del SDK .NET/Windows necesario para compilar.
