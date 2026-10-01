#define MyAppName "Ferrari'sPOS - Desarrollador de Licencias"
#define MyAppVersion "3.2.0"
#define MyAppPublisher "Ferrari'sPOS"
#define MyAppExeName "FerrariPOS_LicenseManager_Corregido.exe"

[Setup]
AppId={{C9A2F9D7-6C15-4B12-8A73-0D8C7A0A3A31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FerrariPOS License Manager
DefaultGroupName=Ferrari'sPOS
OutputDir=installer
OutputBaseFilename=FerrariPOS_LicenseManager_PRO_3_2_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=FerrariPOS_icono.ico
UninstallDisplayIcon={app}\FerrariPOS_icono.ico
DisableProgramGroupPage=yes

[Files]
Source: "FerrariPOS_logo.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\FerrariPOS_LicenseManager_Corregido.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\private_key.pem"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist
Source: "FerrariPOS_icono.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Ferrari'sPOS - Desarrollador de Licencias"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\FerrariPOS_icono.ico"
Name: "{commondesktop}\Ferrari'sPOS - Desarrollador de Licencias"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\FerrariPOS_icono.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir Desarrollador de Licencias"; Flags: nowait postinstall skipifsilent
