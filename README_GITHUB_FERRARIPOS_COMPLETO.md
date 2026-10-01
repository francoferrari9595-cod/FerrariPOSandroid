# FerrariPOS — paquete completo GitHub

Este repositorio contiene cuatro salidas de compilación independientes:

1. **FerrariPOS Manager Android** — APK.
2. **FerrariPOS Manager Windows** — ZIP con `manager.exe` + `manager setup.exe`.
3. **FerrariPOS License Admin Windows** — ZIP con EXE + SETUP.
4. **FerrariPOS License Admin Android** — APK.

## Licencias
El administrador Windows usa `FerrariPOS.LicenseManager.Windows/private_key.pem` para firmar tokens FPOS-LIC-3. La aplicación Android de licencias no contiene la clave privada: envía un token ya firmado al FerrariPOS Windows autorizado.

**Seguridad:** `private_key.pem` es una clave privada real. Si el repositorio de GitHub es público, no se debe publicar esa clave. Para un repositorio público, conviene migrarla a un GitHub Actions Secret antes de producción.

## Activación Android
La aplicación `FerrariPOS.LicenseAdmin.Android` necesita la URL del FerrariPOS Windows, el token de Manager obtenido mediante la vinculación existente y el token FPOS-LIC-3 generado por el administrador.
