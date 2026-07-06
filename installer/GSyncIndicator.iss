; Inno Setup script for G-Sync Taskbar Indicator.
; Build the app first (see build.ps1 or the README), then compile this script with
; Inno Setup 6:  iscc installer\GSyncIndicator.iss
;
; Produces a per-user installer (no administrator rights required).

#define AppName "G-Sync Taskbar Indicator"
#define AppExeName "GSyncIndicator.exe"
#define AppVersion "1.0.3"
#define AppPublisher "GSync-Taskbar-Indicator"
#define AppUrl "https://github.com/rickvanoverbeek/gsync-taskbar-indicator"

; Path to the published output (relative to this .iss file).
#ifndef PublishDir
  #define PublishDir "..\GSyncIndicator\bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
; A stable, unique id for this application (do not reuse for other apps).
AppId={{7C4D2B9E-5A1F-4E6C-9B3A-8D0E2F1C6A44}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
VersionInfoVersion={#AppVersion}

; Per-user install: no UAC prompt, installs under the user's profile.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

; 64-bit application.
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Detect a running instance (matches the app's single-instance mutex).
AppMutex=GSyncTaskbarIndicator_{{6E2A9C7F-2C1B-4D0E-9C9A-4B0C3D5E6F70}
CloseApplications=yes
RestartApplications=no

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\GSyncIndicator\appicon.ico
OutputDir=..\dist
OutputBaseFilename=GSyncIndicator-Setup-{#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start automatically when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
; Bundle everything the publish step produced (single exe, or exe + native dlls).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; "Start with Windows" — uses the same HKCU Run value the app's own menu toggles,
; so the two stay in sync. Removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "GSyncIndicator"; ValueData: """{app}\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the tray app is closed before its files are removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExeName} /F"; \
    Flags: runhidden; RunOnceId: "StopGSyncIndicator"

[Code]
{ Stop a running instance before installing over it. }
procedure StopRunningInstance;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningInstance;
  Result := '';
end;
