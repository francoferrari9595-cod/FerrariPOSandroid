# Corrección GitHub — Setup + EXE + Android

La Action de GitHub ahora entrega cuatro artefactos independientes:
- `FerrariPOS-Manager-Android`: APK Release.
- `FerrariPOS-Licencias-Android`: APK Release de Licencias.
- `FerrariPOS-Windows-EXE`: `FerrarisPOS.exe` standalone publicado con .NET.
- `FerrariPOS-Windows-Setup`: Setup creado con Inno Setup.

El Windows Setup incluye el contenido publicado y el script de configuración de red/Cloudflare según el `.iss` existente.
La carpeta completa `publish/**` deja de ser el artefacto principal para evitar que GitHub entregue solamente una carpeta de instalación.
