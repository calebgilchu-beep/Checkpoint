; Instalador de Checkpoint (Inno Setup 6)
; Compilar: ISCC.exe /DSrcExe=<ruta Checkpoint.exe> /O<salida> Checkpoint.iss
#ifndef SrcExe
  #define SrcExe "..\publish\Checkpoint.exe"
#endif
#define AppVer "1.1.0"

[Setup]
AppId={{B7C1E2A4-6F3D-4E8B-9A21-C4E5D7F80A11}
AppName=Checkpoint
AppVersion={#AppVer}
AppPublisher=Checkpoint
DefaultDirName={autopf}\Checkpoint
DefaultGroupName=Checkpoint
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=Checkpoint-Setup-{#AppVer}
SetupIconFile=..\src\ListaJuegos.App\app.ico
UninstallDisplayIcon={app}\Checkpoint.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SrcExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Checkpoint"; Filename: "{app}\Checkpoint.exe"
Name: "{autodesktop}\Checkpoint"; Filename: "{app}\Checkpoint.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Checkpoint.exe"; Description: "{cm:LaunchProgram,Checkpoint}"; Flags: nowait postinstall skipifsilent
