# FerrariPOS V33 — Productos, categorías, IVA, producto común, prueba y temas

- Android Manager: selector de categorías existentes + botón + para crear; categoría obligatoria al guardar; IVA 21% persistente en el producto; guardar fijo y visible al pie.
- Windows Productos: categoría/departamento de selección con botón desplegable y + para crear nueva.
- API Windows↔Android: GET/POST de categorías y persistencia de `adds_iva_21`.
- Ventas Android: botón PRODUCTO EN COMÚN debajo de PROMOCIONES; descripción + precio; se guarda como línea común y entra en los reportes de venta/cierre.
- Windows: prueba nueva de 10 días para instalaciones nuevas.
- Windows: fondo principal plano, sin la imagen anterior; tema Claro conservado y paletas alineadas con Android.
- Windows: icono ICO actualizado a partir del logo FerrariPOS existente.

## Verificación

- Estructura Kotlin modificada sin errores de sintaxis detectados por el parser; la compilación completa requiere el SDK/dependencias Android.
- C# no se compiló localmente porque este entorno no dispone de .NET/MSBuild.
- Se conservaron los archivos de conexión/Cloudflare y los MP3 existentes salvo los cambios de API necesarios para categorías/IVA.
