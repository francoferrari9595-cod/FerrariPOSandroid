#define MyAppName "FerrariPOS"
#define MyAppVersion "73.1.52"
#define MyAppPublisher "FerrariPOS"
#define MyAppExeName "FerrarisPOS.exe"
#ifndef BuildDir
  #define BuildDir "EXE"
#endif

[Setup]
AppId={{8D0D9E8A-9A0C-4A92-9A14-FERRARIPOS73151}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FerrariPOS
DefaultGroupName=FerrariPOS
OutputDir=.
OutputBaseFilename=FerrariPOS_Setup_V73.1.52
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Dirs]
; El respaldo oculto de clientes/cuentas se guarda dentro de la instalación.
; Se permite modificar únicamente esta subcarpeta para que el POS pueda actualizar
; el Excel aunque la aplicación esté instalada bajo Program Files.
Name: "{app}\RespaldoClientes"; Permissions: users-modify

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectDir}Setup_Network_Cloudflare.ps1"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autodesktop}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Iniciar FerrariPOS automaticamente"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup_Network_Cloudflare.ps1"""; StatusMsg: "Configurando red, DNS y firewall para FerrariPOS..."; Flags: waituntilterminated runhidden skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar FerrariPOS"; Flags: nowait postinstall skipifsilent
