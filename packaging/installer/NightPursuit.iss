; Inno Setup script for the Night Pursuit installer (NightPursuit-Setup.exe).
; Built by .github/workflows/release.yml:  iscc /DAppVersion=1.2.3 /DPublishDir=<publish folder> /DOutputDir=<out> NightPursuit.iss
; Installs to C:\Program Files\Qapow\NightPursuit by default (every QAPOW program goes under C:\Program Files\Qapow),
; and the user can pick another folder. The game keeps settings and high scores in %AppData%\NightPursuit, not here.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\out"
#endif

#define AppName "Night Pursuit"
#define AppFolder "NightPursuit"
#define AppExe "NightPursuit.exe"

[Setup]
; AppId identifies this program to Windows across versions. Never change it.
AppId={{05DCF440-8855-5D86-B080-88D03C63FD09}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=John Apolloni
AppPublisherURL=https://qapow.com/
AppSupportURL=https://github.com/johnovanni/NightPursuit
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Qapow\{#AppFolder}
DisableDirPage=no
DefaultGroupName=Qapow
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\..\src\NightPursuit.App\NightPursuit.ico
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
OutputDir={#OutputDir}
OutputBaseFilename=NightPursuit-Setup

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Qapow\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
