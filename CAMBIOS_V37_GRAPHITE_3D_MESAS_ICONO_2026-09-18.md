# V37 — Graphite 140 + celeste flúor + mesas 3D + icono

## Arranque visual
- FerrariPOS Windows arranca siempre en tema `Graphite`.
- El brillo global se fija en `140` al iniciar la aplicación.
- Se mantiene la posibilidad de modificar el tema/brillo durante la sesión; el siguiente arranque vuelve a Graphite 140.

## Color Graphite
- El acento naranja de Graphite se reemplazó por celeste/cian flúor `RGB 57,255,255`.
- Códigos de barras y stock de las grillas neon de Graphite usan celeste flúor.
- La selección de productos en la venta también usa celeste flúor.
- No se cambiaron colores de advertencias de estado (rojo/verde/amarillo) que tienen significado operativo.

## Salón / Mesas Windows
- El fondo del área de mesas es liso y toma el color de superficie/ventana del tema; no usa fotografía en esa zona.
- Las mesas usan recursos PNG transparentes renderizados (no fotografías):
  - `Resources/tables3d/mesa_3d_cuadrada.png`
  - `Resources/tables3d/mesa_3d_ovalada.png`
  - `Resources/tables3d/mesa_3d_rectangular.png`
- Los recursos incorporan sombra, halo difuminado, bisel, reflejo y sillas para dar sensación 3D.
- `ROUND` se representa como mesa cuadrada de esquinas redondeadas; `OVAL` como mesa ovalada; `RECTANGLE` como mesa rectangular.
- Las mesas libres se muestran en celeste flúor; ocupadas/reservadas conservan rojo en texto/borde para diferenciar estado.
- Se conservan clic, doble función operativa, edición, arrastre, renombrado, tamaño, eliminación, reservas y menú contextual.

## Icono
- `FerrariPOS_icono.ico` fue regenerado a partir del `FerrariPOS_logo.png` del proyecto, sin rediseñarlo.
- El ICO contiene 16/24/32/48/64/128/256 px.
- Se actualizó también el icono del License Manager.
- El instalador V73.1.57 utiliza este ICO.

## Compilación
- Se corrigió además el bloque POST de `/api/mobile/compras` de `WebDashboardServer.cs` que había quedado mal formado y provocaba el `CS1002 ; expected` reportado en GitHub Actions.
- El entorno actual no tiene .NET SDK ni puede descargar Gradle, por lo que la compilación final debe verificarse en GitHub Actions.
