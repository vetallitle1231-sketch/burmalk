#ifndef AppVersion
  #define AppVersion "1.1.11"
#endif
#ifndef PublishDir
  #define PublishDir "..\.build\publish-v1.1.11"
#endif
[Setup]
AppId=Burmalk.Experimental.App
AppName=Burmalk Experimental
AppVersion={#AppVersion}
AppVerName=Burmalk {#AppVersion} Experimental
AppPublisher=Burmalk
DefaultDirName={localappdata}\Programs\Burmalk Experimental
DefaultGroupName=Burmalk Experimental
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
CompressionThreads=1
LZMANumBlockThreads=1
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Burmalk Experimental {#AppVersion}
VersionInfoProductName=Burmalk
VersionInfoDescription=Burmalk Setup
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\.build\tools\PawnIO_setup.exe"; DestDir: "{app}\Support"; Flags: ignoreversion
Source: "..\docs\CPU-SENSORS.txt"; DestDir: "{app}\Support"; Flags: ignoreversion

[Icons]
Name: "{userdesktop}\Burmalk Experimental {#AppVersion}"; Filename: "{app}\Burmalk.exe"; Parameters: "--profile-experimental"; WorkingDir: "{app}"; IconFilename: "{app}\Burmalk.exe"; AppUserModelID: "Burmalk.Experimental.App"
Name: "{userprograms}\Burmalk Experimental {#AppVersion}"; Filename: "{app}\Burmalk.exe"; Parameters: "--profile-experimental"; WorkingDir: "{app}"; IconFilename: "{app}\Burmalk.exe"; AppUserModelID: "Burmalk.Experimental.App"

[InstallDelete]
Type: files; Name: "{userdesktop}\Burmalk Experimental 1.1.8.lnk"
Type: files; Name: "{userprograms}\Burmalk Experimental 1.1.8.lnk"
Type: files; Name: "{userdesktop}\Burmalk Experimental 1.1.9.lnk"
Type: files; Name: "{userprograms}\Burmalk Experimental 1.1.9.lnk"

[Run]
Filename: "{app}\Burmalk.exe"; Parameters: "--profile-experimental"; Description: "Запустить Burmalk экспериментальную версию"; Flags: nowait postinstall skipifsilent

