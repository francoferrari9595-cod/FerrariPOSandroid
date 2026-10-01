#define MyAppName "FerrariPOS"
#define MyAppVersion "73.1.57"
#define MyAppPublisher "FerrariPOS"
#define MyAppExeName "FerrarisPOS.exe"
#ifndef BuildDir
#define BuildDir "EXE"
#endif
#ifndef ProjectDir
#define ProjectDir "."
#endif
#ifndef OutputDir
#define OutputDir "Installer"
#endif

[Setup]
AppId={{8D0D9E8A-9A0C-4A92-9A14-FERRARIPOS73156}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FerrariPOS
DefaultGroupName=FerrariPOS
OutputDir={#OutputDir}
OutputBaseFilename=FerrariPOS_Setup_V73.1.57
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#ProjectDir}\FerrariPOS_icono.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectDir}\Setup_Network_Cloudflare.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectDir}\docs\MANUAL_FERRARIPOS_MANAGER.pdf"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\FerrariPOS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Iniciar FerrariPOS automáticamente"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup_Network_Cloudflare.ps1"""; StatusMsg: "Configurando red y firewall para FerrariPOS..."; Flags: waituntilterminated runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar FerrariPOS"; Flags: nowait postinstall skipifsilent
