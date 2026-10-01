# V22.1 · Corrección de compilación + auditoría + Android

- Se reemplaza/corrige `ReportService.cs` para que todas las consultas de cierre usen `sessionId` y no una variable inexistente `d`.
- Se agrega `price_change_log`.
- Clic derecho `EDITAR PRECIO` ahora exige motivo obligatorio y registra producto, código, precio anterior, precio nuevo, usuario y fecha/hora.
- El resumen detallado del cierre incluye los cambios de precio.
- Se agrega `VINCULAR QR` a la izquierda de `SALÓN` en la barra inferior de Windows, sin cambiar el tamaño de los demás controles.
- El QR usa el flujo existente `MobilePairingForm` y no modifica Cloudflare/API/conexión.
- Android agrega tema `Plateado`.
- Android agrega selección de color del halo del título: verde, naranja, azul, rosa, morado, cian y blanco.
- El halo se dibuja a todo el ancho del encabezado.
- El workflow publica el proyecto Windows (`FerrarisPOS.csproj`) en lugar de publicar la solución, evitando NETSDK1194.
- GitHub entrega Android por separado y un ZIP Windows que contiene EXE + SETUP.
- No se inventa ninguna clave privada de licencias. La integración de LicenseAdmin Windows/Android queda pendiente del archivo/proyecto de licencias real que todavía no está adjunto.
