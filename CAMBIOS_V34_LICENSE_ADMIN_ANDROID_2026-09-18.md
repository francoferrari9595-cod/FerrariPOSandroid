# FerrariPOS V34 — Reparación License Admin Android

## Problema encontrado

El administrador Android almacenaba la clave privada como Base64 del PEM completo, pero al generar la licencia hacía un único `Base64.decode(...)` y pasaba el resultado (texto PEM, incluyendo `BEGIN PRIVATE KEY`) directamente a `PKCS8EncodedKeySpec`.

Eso provoca el error:
`com.android.org.conscrypt.OpenSSLX509CertificateFactory$ParsingException: Error parsing private key`

Android `PKCS8EncodedKeySpec` espera los bytes DER de una clave PKCS#8, no el texto PEM. La documentación oficial de Android define precisamente ese formato como `PrivateKeyInfo` PKCS#8.

## Correcciones

- Se recupera correctamente el PEM embebido.
- Se eliminan los encabezados `BEGIN/END PRIVATE KEY`.
- Se decodifica el cuerpo Base64 a DER PKCS#8.
- Se genera la clave RSA sin depender de Internet.
- Se mantiene RSA + SHA-256 + PSS con salt length 32, compatible con `RSASignaturePadding.Pss` de Windows.
- Se comprobó que la clave privada embebida en Android y la `private_key.pem` del Desarrollador Windows producen exactamente la misma clave pública.

## Duración

Android deja de usar los 30 días fijos.

Ahora permite introducir manualmente entre 1 y 36500 días.

Para licencias finitas, Android genera el mismo esquema de Windows:
- `FPOS-LIC-3`
- `Action = ACTIVATE`
- `Type = CUSTOM`
- `ExpiresAtUtc = fecha absoluta`
- `DurationDays = null`
- `CustomerName = ""`

Windows puede verificar y aplicar ese token con su clave pública.

## Funcionamiento

1. Obtener Machine ID de Windows.
2. Introducir Machine ID en Android License Admin.
3. Introducir cantidad de días.
4. Generar código.
5. Copiar o enviar el token por WhatsApp/mensaje.
6. El cliente lo pega en FerrariPOS Windows.
7. No se necesita Internet, API, servidor ni Cloudflare.
