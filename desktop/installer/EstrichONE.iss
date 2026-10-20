; EstrichONE für Windows – Installationsprogramm (Inno Setup 6).
; Aufruf: ISCC.exe /DAppVersion=0.1.0 /DSourceDir=<publish-Ordner> /DOutputDir=<Ziel> EstrichONE.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6F3B2A61-8E0C-4C3A-9B7E-2E5D1A4C9F10}
AppName=EstrichONE
AppVersion={#AppVersion}
AppVerName=EstrichONE {#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=florians25
AppPublisherURL=https://github.com/florians25/EstrichONE
AppSupportURL=https://github.com/florians25/EstrichONE/issues
DefaultDirName={autopf}\EstrichONE
DefaultGroupName=EstrichONE
DisableProgramGroupPage=yes
; Ohne Administratorrechte für den aktuellen Benutzer installierbar; mit Rechten für alle Benutzer.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=EstrichONE-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=EstrichONE
UninstallDisplayIcon={app}\EstrichONE.exe
CloseApplications=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\EstrichONE"; Filename: "{app}\EstrichONE.exe"
Name: "{group}\{cm:UninstallProgram,EstrichONE}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\EstrichONE"; Filename: "{app}\EstrichONE.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EstrichONE.exe"; Description: "{cm:LaunchProgram,EstrichONE}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Anmeldedaten liegen in der Windows-Anmeldeinformationsverwaltung, Einstellungen unter %LOCALAPPDATA%\EstrichONE –
; beides bleibt bei einer Deinstallation erhalten (erneute Installation ohne neue Anmeldung).
