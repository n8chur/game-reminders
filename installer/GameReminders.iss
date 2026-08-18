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
AppVerName={#AppName}
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
Name: "launchatlogin"; Description: "Launch Game Reminders when I sign in to Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace

[Icons]
Name: "{group}\Game Reminders"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Game Reminders"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GameReminders"; ValueData: """{app}\{#AppExeName}"" --hidden-at-login"; Tasks: launchatlogin

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Game Reminders"; Flags: nowait postinstall skipifsilent

[Code]
var
  LaunchAtLoginTaskInitialized: Boolean;

procedure InitializeLaunchAtLoginTask;
var
  RegisteredCommand: String;
  ExpectedCommand: String;
begin
  if LaunchAtLoginTaskInitialized then
    Exit;

  if FileExists(ExpandConstant('{app}\{#AppExeName}')) then
  begin
    ExpectedCommand := '"' + ExpandConstant('{app}\{#AppExeName}') + '" --hidden-at-login';
    if RegQueryStringValue(
         HKCU,
         'Software\Microsoft\Windows\CurrentVersion\Run',
         'GameReminders',
         RegisteredCommand) and
       (CompareText(RegisteredCommand, ExpectedCommand) = 0) then
      WizardSelectTasks('launchatlogin')
    else
      WizardSelectTasks('!launchatlogin');
  end;

  LaunchAtLoginTaskInitialized := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectTasks then
    InitializeLaunchAtLoginTask;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  InitializeLaunchAtLoginTask;
  Result := '';
end;

procedure RemoveOwnedLaunchAtLoginRegistration;
var
  RegisteredCommand: String;
  InstalledExecutable: String;
begin
  if not RegQueryStringValue(
           HKCU,
           'Software\Microsoft\Windows\CurrentVersion\Run',
           'GameReminders',
           RegisteredCommand) then
    Exit;

  InstalledExecutable := '"' + ExpandConstant('{app}\{#AppExeName}') + '"';
  if (CompareText(RegisteredCommand, InstalledExecutable + ' --hidden-at-login') = 0) or
     (CompareText(RegisteredCommand, InstalledExecutable) = 0) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'GameReminders');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('launchatlogin')) then
    RemoveOwnedLaunchAtLoginRegistration;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveOwnedLaunchAtLoginRegistration;
end;
