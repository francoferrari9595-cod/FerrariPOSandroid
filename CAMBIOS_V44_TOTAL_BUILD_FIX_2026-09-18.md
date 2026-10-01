# V44 — TOTAL principal robusto + corrección de compilación Windows

## TOTAL principal
- Mantiene el estilo 3D cian/vectorial existente.
- Centra la geometría completa, incluyendo extrusión y sombra.
- Hasta 6 dígitos prioriza 40 pt y permite compresión horizontal vectorial moderada antes de reducir altura.
- 7/8+ dígitos reducen progresivamente, conservando la mayor escala posible.
- Se tiene en cuenta el espacio real disponible y la profundidad 3D para evitar clipping.

## Compilación
- `CartItem` ahora declara `CostPrice`, propiedad que ya era utilizada por `MainForm` al reconstruir productos/ventas móviles.
- Esto corrige los errores CS0117 de `MainForm.cs` en las líneas reportadas por GitHub Actions.

## Alcance
- No se modificó `SalonForm.cs`.
- No se modificó `TableService.cs`.
- No se modificó `ThemeService.cs`.
- No se modificaron recursos 3D de mesas.
