; Installer for Concilium: per user, no administrator rights, no questions.
; Build: ISCC.exe /DVersion=0.4.0 /DSource=..\..\dist\release\Concilium Concilium.iss
; The people's list and memory state live in %APPDATA%\Concilium and are never touched here.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Source
  #define Source "..\..\dist\release\Concilium"
#endif

[Setup]
AppId={{6C0D6A52-3B7E-4F0B-9E57-2B8C1F4D7A31}
AppName=Concilium
AppVersion={#Version}
AppPublisher=Dmitry Verkhoturov
AppPublisherURL=https://github.com/dmitryverkhoturov/concilium
DefaultDirName={localappdata}\Programs\Concilium
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=yes
PrivilegesRequired=lowest
OutputDir=..\..\dist\release
OutputBaseFilename=Concilium-{#Version}-setup
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\Concilium.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DisableFinishedPage=yes
ShowLanguageDialog=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "Concilium data\*,portable.txt"

[InstallDelete]
; Leftovers of older builds (satellite folders); the data folder of 0.3.x is moved by the app itself.
Type: filesandordirs; Name: "{app}\ru"

[Icons]
Name: "{userprograms}\Concilium"; Filename: "{app}\Concilium.exe"
Name: "{userdesktop}\Concilium"; Filename: "{app}\Concilium.exe"

[Run]
Filename: "{app}\Concilium.exe"; Flags: nowait skipifsilent
