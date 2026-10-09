#define AppVersion "1.1.4"
[Setup]
AppId=Burmalk.Monitoring.App
AppName=Burmalk
AppVersion={#AppVersion}
AppVerName=Burmalk {#AppVersion}
AppPublisher=Burmalk
DefaultDirName={localappdata}\Programs\Burmalk
DefaultGroupName=Burmalk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=Burmalk-Setup-{#AppVersion}
SetupIconFile=..\src\Burmalk\Assets\Burmalk.ico
UninstallDisplayIcon={app}\Burmalk.exe
WizardSmallImageFile=..\src\Burmalk\Assets\Burmalk.png
WizardStyle=modern dark polar includetitlebar
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Burmalk
VersionInfoProductName=Burmalk
VersionInfoDescription=Burmalk Setup
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\.build\publish-v1.1.4\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\.build\tools\PawnIO_setup.exe"; DestDir: "{app}\Support"; Flags: ignoreversion
Source: "..\docs\CPU-SENSORS.txt"; DestDir: "{app}\Support"; Flags: ignoreversion

[Icons]
Name: "{userdesktop}\Burmalk"; Filename: "{app}\Burmalk.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Burmalk.exe"; AppUserModelID: "Burmalk.Monitoring.App"
Name: "{userprograms}\Burmalk"; Filename: "{app}\Burmalk.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Burmalk.exe"; AppUserModelID: "Burmalk.Monitoring.App"

[Run]
Filename: "{app}\Burmalk.exe"; Description: "{cm:LaunchProgram,Burmalk}"; Flags: nowait postinstall skipifsilent

