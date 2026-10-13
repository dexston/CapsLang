; =====================================================================
; CapsLang Inno Setup Script
; Builds the official Windows installer with UIAccess certificate setup
; =====================================================================

#define MyAppName "CapsLang"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define MyAppPublisher "Sergey Matveev (dexston)"
#define MyAppURL "https://github.com/dexston/CapsLang"
#define MyAppExeName "CapsLang.exe"
#define CertSubjectName "CapsLang"

[Setup]
; Unique application GUID for Windows Add/Remove Programs
AppId={{8B1D4F3A-4E90-449D-9B8A-701C5DCE6391}}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\build\dist
OutputBaseFilename=CapsLang-{#AppVersion}-Setup
SetupIconFile=..\resources\icons\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic

; Require administrative privileges for Program Files and root certificate import
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

; Add/Remove Programs branding
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UsedUserAreasWarning=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\build\bin\Release\Installer\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\resources\cert\CapsLang.cer"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Clean up registry configuration on uninstall
Root: HKCU; Subkey: "Software\CapsLang"; Flags: uninsdeletekey
; Clean up autostart entry on uninstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "CapsLang"; Flags: uninsdeletevalue

[Run]
; Post-install launch via ShellExecute (required by Windows to grant UIAccess token)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

// Helper to gracefully close any running instance before file replacement/deletion
procedure StopRunningInstance();
var
  QuitEvent: THandle;
  ResultCode: Integer;
begin
  QuitEvent := OpenEvent(EVENT_MODIFY_STATE, False, 'Local\CapsLang_Quit_Event');
  if QuitEvent <> 0 then
  begin
    SetEvent(QuitEvent);
    CloseHandle(QuitEvent);
    Sleep(300);
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Gracefully shut down any running instance before install
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StopRunningInstance();
end;

// Manage certificate installation and upgrade in post-install step
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  CerPath: String;
  CertUtilPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    CertUtilPath := ExpandConstant('{sys}\certutil.exe');
    // Clean up any older CapsLang certificates to prevent duplicate accumulation
    Exec(CertUtilPath, '-delstore -f Root "{#CertSubjectName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Extract certificate to temporary directory and silently import into Trusted Root store
    ExtractTemporaryFile('CapsLang.cer');
    CerPath := ExpandConstant('{tmp}\CapsLang.cer');
    Exec(CertUtilPath, '-addstore -f Root "' + CerPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

// Clean up running instances before file removal and remove root certificate after uninstallation
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRunningInstance();
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    Exec(ExpandConstant('{sys}\certutil.exe'), '-delstore -f Root "{#CertSubjectName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
