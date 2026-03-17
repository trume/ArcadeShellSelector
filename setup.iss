; ============================================================================
; ArcadeShell Setup — Inno Setup Script
; ============================================================================
;
; Prerequisites:
;   - Inno Setup 6.x installed (https://jrsoftware.org/isinfo.php)
;   - Run publish.ps1 first to populate deploy\ArcadeShell\
;
; Build from command line:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss
;
; Or pass version override:
;   ISCC.exe /DAppVersion=1.2.10 setup.iss
;
; Silent install:
;   ArcadeShellSetup.exe /SILENT /TASKS="shellreplace,firewall"
;
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.2.10"
#endif

#define AppName      "ArcadeShell"
#define AppPublisher "Trume76"
#define AppExe       "ArcadeShellSelector.exe"
#define AppCfgExe    "ArcadeShellConfigurator.exe"
#define AppSrvExe    "ArcadeShellServer.exe"
#define SourceDir    "deploy\ArcadeShell"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-ARCADESHELL01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName=C:\{#AppName}
DefaultGroupName={#AppName}
OutputDir=deploy
OutputBaseFilename=ArcadeShellSetup-v{#AppVersion}
SetupIconFile={#SourceDir}\app.ico
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile=LICENSE
MinVersion=10.0
; Allow user to restart into shell mode after install
AlwaysRestart=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "shellreplace";  Description: "Set ArcadeShell as the Windows shell (replaces Explorer desktop)"; Flags: unchecked
Name: "firewall";      Description: "Add Windows Firewall rule for the remote mobile server";          Flags: unchecked
Name: "desktopicon";   Description: "Create a Desktop shortcut";                                       Flags: unchecked
Name: "launchconfig";  Description: "Launch Configurator after installation";                           Flags: checkedonce

[Files]
; Deploy entire build output — recursive, preserves folder structure
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExe}";    IconFilename: "{app}\app.ico"
Name: "{group}\{#AppName} Configurator"; Filename: "{app}\{#AppCfgExe}"; IconFilename: "{app}\app.ico"
Name: "{group}\Uninstall {#AppName}";    Filename: "{uninstallexe}"
; Desktop (optional)
Name: "{commondesktop}\{#AppName}";      Filename: "{app}\{#AppExe}";    IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Registry]
; --- Shell replacement (only when task selected) -----------------------------------------------
; Back up current shell value before replacing it
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"; ValueType: string; ValueName: "Shell_Backup"; ValueData: "{code:GetCurrentShell}"; Flags: createvalueifdoesntexist uninsdeletevalue; Tasks: shellreplace
; Set ArcadeShell as the shell
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"; ValueType: string; ValueName: "Shell"; ValueData: "{app}\{#AppExe}"; Tasks: shellreplace

; --- App registration (always) -----------------------------------------------------------------
Root: HKLM; Subkey: "SOFTWARE\{#AppName}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\{#AppName}"; ValueType: string; ValueName: "Version";    ValueData: "{#AppVersion}"; Flags: uninsdeletekey

[Run]
; Add firewall rule (inbound for the server exe, private profile only)
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""ArcadeShell Server"" dir=in action=allow program=""{app}\{#AppSrvExe}"" enable=yes profile=private"; Flags: runhidden; Tasks: firewall
; Launch Configurator after install
Filename: "{app}\{#AppCfgExe}"; Flags: nowait postinstall skipifsilent; Description: "Launch {#AppName} Configurator"; Tasks: launchconfig

[UninstallRun]
; Remove firewall rule
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""ArcadeShell Server"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"

[Code]
// ── Pascal Script ─────────────────────────────────────────────────────────

function GetCurrentShell(Param: String): String;
var
  CurrentShell: String;
begin
  // Read the current shell so we can back it up
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon',
    'Shell', CurrentShell) then
    CurrentShell := 'explorer.exe';
  Result := CurrentShell;
end;

procedure RestoreOriginalShell;
var
  BackupShell: String;
begin
  // Restore the shell that was active before we replaced it
  if RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon',
    'Shell_Backup', BackupShell) then
  begin
    if BackupShell <> '' then
      RegWriteStringValue(HKEY_LOCAL_MACHINE,
        'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon',
        'Shell', BackupShell);
  end
  else
  begin
    // Fallback: restore explorer.exe
    RegWriteStringValue(HKEY_LOCAL_MACHINE,
      'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon',
      'Shell', 'explorer.exe');
  end;
  // Clean up backup key
  RegDeleteValue(HKEY_LOCAL_MACHINE,
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon',
    'Shell_Backup');
end;

function IsShellReplaced: Boolean;
var
  CurrentShell: String;
begin
  Result := False;
  if RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon',
    'Shell', CurrentShell) then
  begin
    // Check if the current shell points to our exe
    Result := (Pos('ArcadeShellSelector', CurrentShell) > 0);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // If we are the current shell, restore the previous one
    if IsShellReplaced then
      RestoreOriginalShell;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // Warn user about shell replacement before proceeding
  if CurPageID = wpSelectTasks then
  begin
    if WizardIsTaskSelected('shellreplace') then
    begin
      Result := (MsgBox(
        'WARNING: This will replace the Windows desktop with ArcadeShell.' + #13#10 + #13#10 +
        'After the next reboot, Windows will boot directly into ArcadeShell ' +
        'instead of the Explorer desktop.' + #13#10 + #13#10 +
        'The original shell will be restored automatically if you uninstall.' + #13#10 + #13#10 +
        'Are you sure you want to enable shell replacement?',
        mbConfirmation, MB_YESNO) = IDYES);
    end;
  end;
end;
