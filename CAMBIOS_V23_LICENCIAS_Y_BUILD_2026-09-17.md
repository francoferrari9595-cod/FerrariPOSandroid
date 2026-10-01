# V23 — Licencias + compilación GitHub

- Corregido ReportService para usar `sessionId` en CashSessionDetailed y `d` únicamente dentro de métodos con fecha declarada.
- Workflow Windows publica `FerrarisPOS.csproj` directamente para evitar NETSDK1194 de salida a nivel solución.
- Agregado FerrariPOS License Admin Windows PRO 3.2, incluyendo clave privada proporcionada por el propietario y su instalador.
- Agregado endpoint móvil autenticado `/api/mobile/license/activate` que valida el token firmado con LicenseService antes de aplicar la licencia.
- Agregado FerrariPOS License Admin Android, aplicación sencilla para enviar un token FPOS-LIC-3 al Windows autorizado.
- GitHub entrega cuatro artefactos: Manager Android, Manager Windows (EXE+SETUP juntos), License Admin Windows (EXE+SETUP juntos), License Admin Android.
- No se modifica la lógica existente de QR/Cloudflare de FerrariPOS Manager.
