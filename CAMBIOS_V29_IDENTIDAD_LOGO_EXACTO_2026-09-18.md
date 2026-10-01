# FerrariPOS V29 — Identidad visual exacta

## Objetivo
Aplicar exactamente el logo proporcionado por el usuario en los recursos de identidad de FerrariPOS, sin modificar botones, configuración funcional, conexión QR/Cloudflare, licencias, base de datos ni lógica comercial.

## Cambios
- Reemplazados los recursos principales de logo de Windows por la imagen proporcionada, conservando exactamente sus píxeles en 1536x1536 cuando el formato lo permite.
- Login de Windows: `FerrariPOS_login_poster.png` ahora usa exactamente el logo proporcionado. Se conserva el diseño/tamaño de los controles y botones existentes.
- Icono Windows de FerrariPOS: `FerrariPOS_icono.ico` generado desde el mismo logo en múltiples tamaños.
- Instalador Windows: `SetupIconFile` apunta al mismo icono.
- Administrador de Licencias Windows: mismo icono, logo visible en login y encabezados; no se modificaron botones ni lógica.
- Administrador de Licencias Android: mismo logo visible, mismo icono de aplicación y nombre `FerrariPOS · Licencias`.
- FerrariPOS Manager Android: splash e icono de aplicación usan el mismo logo.
- Recurso SVG legado de Windows actualizado para apuntar a la misma imagen.
- Se conserva `pos_background.png` como fondo operativo existente; no se reemplaza por el logo para no alterar la apariencia/configuración del programa.

## Halo Android
- Se reforzó `NeonAppTitle` para resolver el color de forma robusta según el valor guardado/seleccionado.
- Se aceptan nombres con y sin acentos y alias comunes.
- `key(haloName)` fuerza la recreación visual del encabezado cuando cambia el color.
- No se modificaron los botones de Configuración.

## Corte Z General
- Se conserva el botón existente `ENVIAR POR WHATSAPP / MENSAJE` en `GeneralZCard`, con selector de WhatsApp, Mensajes y otras apps mediante `ACTION_SEND`.
- No se modificó la lógica del reporte ni los botones de otras pantallas.

## Verificación
- Conteo de llaves/paréntesis de `MainActivity.kt`: balanceado.
- Los recursos principales de logo de 1536x1536 comparan exactamente con la imagen proporcionada después de decodificación.
- No fue posible ejecutar `dotnet build` en este entorno porque `dotnet` no está instalado.
- No se afirma compilación Android local: el entorno no dispone de acceso/entorno Gradle verificable.
