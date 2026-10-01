FERRARI'SPOS - DESARROLLADOR DE LICENCIAS 3.0

MEJORAS
- Mantiene la corrección de vencimiento exacto ya comprobada.
- Login obligatorio: usuario admin / contraseña maestra 600613.
- Botones visibles COPIAR TOKEN y GUARDAR TOKEN.
- Campo de token ampliado y con tipografía monoespaciada.
- Aviso automático al iniciar cuando hay licencias activas con 5 días o menos.
- Historial local y diagnóstico RSA se conservan.
- Ventana redimensionable para trabajar con más comodidad.
- SETUP.bat ahora crea el instalador; CREAR_SETUP.bat hace el proceso completo.

CREAR INSTALADOR
1. Instalar .NET 8 SDK.
2. Instalar Inno Setup 6.
3. Ejecutar SETUP.bat.
4. El instalador final queda en la carpeta installer.

IMPORTANTE
private_key.pem es la clave privada y nunca debe entregarse a los clientes.
