#ifndef AppVersion
  #error AppVersion must be supplied with /DAppVersion=x.y.z
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\game-reminders-win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\packages"
#endif

#define AppName "Game Reminders"
#define AppExeName "GameReminders.exe"
#define AppPublisher "Game Reminders"
#define AppId "{{D7D90A76-6652-47D5-8D45-22CAEE4D6BA9}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/n8chur/game-reminders
AppSupportURL=https://github.com/n8chur/game-reminders/issues
AppUpdatesURL=https://github.com/n8chur/game-reminders/releases
DefaultDirName={localappdata}\Programs\Game Reminders
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=GameReminders-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\GameReminders.App\Assets\GameReminders.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Game launch reminders synchronized through iCloud Drive
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no
SetupMutex=GameReminders.Setup.v1
AppMutex=Local\GameReminders.SingleInstance.v1
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace

[Icons]
Name: "{group}\Game Reminders"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Game Reminders"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Game Reminders"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'GameReminders');
end;
