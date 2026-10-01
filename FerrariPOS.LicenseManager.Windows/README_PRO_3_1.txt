FERRARI'SPOS - LICENSE MANAGER PRO 3.1

CORRECCION PRINCIPAL
La logica de vencimiento exacto de la version que ya fue probada se mantiene sin cambios.

COMO CREAR EL INSTALADOR
1. Instala .NET 8 SDK.
2. Instala Inno Setup 6.
3. Deja todos los archivos de esta carpeta juntos.
4. Ejecuta SETUP.bat.
5. SETUP.bat llama a CREAR_INSTALADOR.bat.
6. El resultado queda en:
   installer\FerrariPOS_LicenseManager_Setup.exe

CREAR_INSTALADOR.bat es el archivo principal para fabricar el instalador.
FerrariPOS_LicenseManager_Setup.iss es el script de Inno Setup incluido.

PARA SOLO PUBLICAR EL EXE
Ejecuta PUBLICAR.bat.

IMPORTANTE
private_key.pem es la clave privada administrativa. No distribuirla a clientes.

ACCESO
Usuario: admin
Contraseña maestra: 600613
