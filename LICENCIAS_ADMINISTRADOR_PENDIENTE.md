# FerrariPOS · administración de licencias

El cliente FerrariPOS ya contiene `LicenseService.cs` con verificación RSA y la clave pública.
La aplicación administrativa que genere licencias reales necesita el proyecto/archivo de
licencias del propietario (en especial la clave privada o el servicio de firma).

No se inventa ni se incrusta una clave privada en el APK.

Al incorporar ese archivo real, el workflow se ampliará para entregar:
- FerrariPOS-Manager-Android.apk
- FerrariPOS-Manager-Windows-COMPLETO.zip (EXE + SETUP)
- LicenseAdmin-Windows-COMPLETO.zip (EXE + SETUP)
- LicenseAdmin-Android.apk
