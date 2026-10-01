#define MyAppName "FerrariPOS"
#define MyAppVersion "73.1.45"
#define MyAppPublisher "FerrariPOS"
#define MyAppExeName "FerrarisPOS.exe"

[Setup]
AppId={{8D0D9E8A-9A0C-4A92-9A14-FERRARIPOS73142}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FerrariPOS
DefaultGroupName=FerrariPOS
OutputDir=.
OutputBaseFilename=FerrariPOS_Setup_V73.1.45
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "EXE\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Setup_Network_Cloudflare.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Iniciar FerrariPOS automaticamente"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup_Network_Cloudflare.ps1"""; StatusMsg: "Configurando red, DNS y firewall para FerrariPOS..."; Flags: waituntilterminated runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar FerrariPOS"; Flags: nowait postinstall skipifsilent
