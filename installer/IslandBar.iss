; Crée installer\Output\IslandBar-Lite-Setup.exe. Depuis la racine du dépôt :
;   dotnet publish -c Release -r win-x64 --self-contained true -o dist
;   ISCC.exe installer\IslandBar.iss
; La version autonome embarque .NET : rien d'autre à installer sur le PC cible.

#define Version GetVersionNumbersString("..\dist\IslandBar.dll")

[Setup]
AppId={{E1B1D9A2-6C1F-4E56-9C3B-7F5E1F3A2D10}
AppName=IslandBar Lite
AppVersion={#Version}
AppPublisher=LKDM7
DefaultDirName={autopf}\IslandBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputBaseFilename=IslandBar-Lite-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
SetupIconFile=..\assets\logo\IslandBar.ico
UninstallDisplayIcon={app}\IslandBar.exe
CloseApplications=force
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.AutoStart=Start IslandBar Lite with Windows
fr.AutoStart=Lancer IslandBar Lite au démarrage de Windows
en.LaunchNow=Launch IslandBar Lite
fr.LaunchNow=Lancer IslandBar Lite

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Excludes: "*.pdb,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

; L'exe, le dossier et AppId gardent l'ancien nom : une installation 1.4 est mise à jour sur place, réglages compris.
[InstallDelete]
Type: files; Name: "{autoprograms}\IslandBar.lnk"

[Icons]
Name: "{autoprograms}\IslandBar Lite"; Filename: "{app}\IslandBar.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IslandBar"; ValueData: """{app}\IslandBar.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\IslandBar.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent
; Une mise à jour lancée par l'île est silencieuse : c'est elle qui doit relancer l'île.
Filename: "{app}\IslandBar.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{app}\IslandBar.exe"; Parameters: "--restore-banners"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreBanners"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM IslandBar.exe"; Flags: runhidden; RunOnceId: "StopIslandBar"

; Le jeton Discord ne doit pas survivre à la désinstallation.
[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\IslandBar"
Type: files; Name: "{app}\IslandBar.log"
