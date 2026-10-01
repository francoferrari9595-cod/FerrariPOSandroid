# FerrariPOS — Licencias 100% offline

## Flujo
- Cada instalación Windows obtiene un Machine ID estable.
- License Admin Windows genera tokens firmados para ese Machine ID.
- License Admin Android también puede generar tokens sin Internet.
- El token FPOS-LIC-3 contiene Machine ID, License ID, acción, tipo, fecha de emisión y vencimiento.
- Windows valida firma RSA y Machine ID localmente.
- La barra/estado de licencia de Windows continúa leyendo el estado local.
- No se requiere servidor para activar, renovar o desactivar mediante token firmado.

## Inicio del período
Los tokens generados por el administrador Android usan `ExpiresAtUtc` absoluto calculado al generar. El cliente Windows conserva el comportamiento de fecha absoluta de la versión existente.

## Seguridad
La clave privada no debe publicarse en un repositorio público. Para una distribución comercial, el APK de administración offline debe protegerse o reemplazarse por un mecanismo de firma externo controlado por el desarrollador.
