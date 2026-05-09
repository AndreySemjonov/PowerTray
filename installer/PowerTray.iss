#define AppName "PowerTray"
#define AppVersion "0.3.1"
#define Publisher "PowerTray"
#define AppExe "PowerTray.exe"
#define ServiceName "PowerTrayBatteryImpact"
#define ServiceDisplayName "PowerTray Battery Impact Helper"
#define PublishDir "..\.private\installer-publish\PowerTray"
#define ServicePublishDir "..\.private\installer-publish\PowerTray.Service"

[Setup]
AppId={{F4032CF6-6426-4A2E-A098-8DE999F26105}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\PowerTray
DefaultGroupName=PowerTray
DisableProgramGroupPage=yes
OutputDir=..\.private\dist
OutputBaseFilename=PowerTraySetup
SetupIconFile=..\PowerTray\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ServicePublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "install-service.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "uninstall-service.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PowerTray"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\PowerTray"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-service.ps1"" -InstallDir ""{app}"""; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "Launch PowerTray"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall-service.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "PowerTrayBatteryImpactService"
