# FerrariPOS V42 — TOTAL 3D centrado sin alterar mesas

- Base: V40.
- No se modificó `SalonForm.cs`, `TableService.cs`, `ThemeService.cs` ni los recursos `Resources/tables3d`.
- El único cambio funcional es el renderizado/ajuste del TOTAL principal de venta.
- Hasta 6 dígitos conserva 40 pt siempre que el espacio real lo permita.
- 7, 8, 9 y 10+ dígitos usan escalones progresivos de tamaño.
- Se mide la geometría vectorial real del texto 3D.
- Se centra con cara + extrusión + margen de seguridad.
- Si un importe queda justo, se permite una compresión horizontal máxima del 10% antes de sacrificar altura.
- El efecto 3D continúa siendo completamente generado por código, sin imágenes.
