; Crée installer\Output\IslandBar-Setup.exe. Depuis la racine du dépôt :
;   dotnet publish -c Release -r win-x64 --self-contained true -o dist
;   ISCC.exe installer\IslandBar.iss
; La version autonome embarque .NET : rien d'autre à installer sur le PC cible.

#define Version "1.0.0"

[Setup]
AppId={{E1B1D9A2-6C1F-4E56-9C3B-7F5E1F3A2D10}
AppName=IslandBar
AppVersion={#Version}
AppPublisher=LKDM7
DefaultDirName={autopf}\IslandBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputBaseFilename=IslandBar-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\IslandBar.exe
CloseApplications=force
RestartApplications=no

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "autostart"; Description: "Lancer IslandBar au démarrage de Windows"
Name: "nobanner"; Description: "Masquer les bannières Windows de Discord (évite les notifications en double)"

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Excludes: "*.pdb,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\IslandBar"; Filename: "{app}\IslandBar.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IslandBar"; ValueData: """{app}\IslandBar.exe"""; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\com.squirrel.Discord.Discord"; ValueType: dword; ValueName: "ShowBanner"; ValueData: 0; Flags: uninsdeletevalue; Tasks: nobanner

[Run]
Filename: "{app}\IslandBar.exe"; Description: "Lancer IslandBar"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM IslandBar.exe"; Flags: runhidden; RunOnceId: "StopIslandBar"

; Le jeton Discord ne doit pas survivre à la désinstallation.
[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\IslandBar"
Type: files; Name: "{app}\IslandBar.log"
