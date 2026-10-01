# FerrariPOS V38 — corrección de compilación y licencias offline

## Correcciones

### Windows
- `FerrariPOS.Windows/Forms/ProductsForm.cs`: agregado `using FerrarisPOS.Data;` para que el selector/asignación de proveedores compile y pueda usar `Database.Open()`.
- `FerrariPOS.Windows/Services/WebDashboardServer.cs`: normalizada la auditoría de actualización/recepción de compras para evitar que el compilador quede desincronizado en esa sección y reporte `CS1002` en el `if` siguiente.

### Android Manager
- Corregido el callback del escáner de producto para que use un parámetro explícito (`code`) y no dependa del `it` implícito en una expresión anidada.
- Esto evita el efecto cascada de Kotlin 2.x que terminaba mostrando múltiples `Unresolved reference: Field`, `it` y `common` en `MainActivity.kt`.

### Android License Admin
- Se mantiene **100% offline**.
- La clave privada RSA es exactamente la misma que utiliza `FerrariPOS.LicenseManager.Windows`, pero ahora se almacena como **DER PKCS#8 puro en Base64**, evitando el error de Conscrypt `OpenSSLX509CertificateFactory$ParsingException: Error parsing private key`.
- Firma con `RSA-PSS + SHA-256`, compatible con la verificación de Windows.
- Formato de token: `FPOS-LIC-3.<payload>.<firma>`.
- Se puede indicar la cantidad de días desde Android.
- Las licencias finitas usan `ExpiresAtUtc` como fecha absoluta y `DurationDays = null`, igual que el desarrollador de licencias de Windows actual.
- Incluye `ACTIVATE`, `RENEW`, `DEACTIVATE`, `CUSTOM`, `ANNUAL` y `PERMANENT`, además de cliente/empresa e ID de licencia.
- Permite copiar o enviar el código largo generado sin depender de Internet.

## Verificación realizada
- La clave DER embebida en Android coincide criptográficamente con la clave privada PEM de Windows.
- Se comprobó con RSA-PSS/SHA-256 que un token `FPOS-LIC-3` puede firmarse y verificarse con la misma pareja de claves.
- No se modificó la lógica de red/Cloudflare fuera de las correcciones de compilación indicadas.
