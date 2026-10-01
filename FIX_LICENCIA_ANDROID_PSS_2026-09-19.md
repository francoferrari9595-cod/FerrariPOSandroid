# Corrección — Administrador de Licencias Android / RSASSA-PSS

Fecha: 2026-09-19

## Problema
En algunos dispositivos Android, `Signature.getInstance("RSASSA-PSS")` no está disponible en el proveedor criptográfico del sistema. Al generar una licencia aparecía:

`NoSuchAlgorithmException · RSASSA-PSS Signature not available`

## Corrección
Se mantiene primero el algoritmo nativo `RSASSA-PSS` cuando el dispositivo lo ofrece. Si no está disponible, el Administrador de Licencias Android utiliza un fallback local de EMSA-PSS/RSA con:

- RSA
- SHA-256
- MGF1 SHA-256
- salt de 32 bytes
- formato compatible con `.NET RSA` + `RSASignaturePadding.Pss` usado por Windows
- sin Internet y sin dependencia externa adicional

No se cambia la clave privada ni el formato `FPOS-LIC-3`.

## Validación
La implementación PSS fallback fue verificada criptográficamente contra una clave RSA de 3072 bits y validada con RSA-PSS SHA-256, salt de 32 bytes.

La compilación Android no pudo ejecutarse en este entorno porque el Gradle Wrapper intentó descargar Gradle y el entorno no tiene acceso a `services.gradle.org`. No se afirma que el APK haya sido compilado aquí.
