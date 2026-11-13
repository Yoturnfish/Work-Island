; Work Island per-user installer. App data is never part of the installed file set.
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#define FileVersion GetFileVersion(PayloadDir + "\WorkIsland.exe")
#ifndef ProductVersion
  #define ProductVersion FileVersion
#endif
#ifndef PackageName
  #define PackageName "WorkIsland-" + ProductVersion + "-win-x64-setup"
#endif
#ifdef TestRoot
  #ifndef TestId
    #error TestId is required with TestRoot
  #endif
  #define AppIdValue TestId
  #define AppDirectory TestRoot + "\Application"
  #define DataDirectory TestRoot + "\Data"
  #define ShortcutDirectory TestRoot + "\Shortcuts"
  #define RunKey "Software\WorkIsland\Acceptance\" + TestId + "\Run"
#else
  #define AppIdValue "6B22A7C8-B238-4372-93DF-B942E8E62435"
  #define AppDirectory "{localappdata}\Programs\WorkIsland"
  #define DataDirectory "{localappdata}\WorkIsland"
  #define ShortcutDirectory "{userprograms}\Work Island"
  #define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#endif

[Setup]
AppId={{{#AppIdValue}}
AppName=Work Island
AppVersion={#ProductVersion}
AppVerName=Work Island {#ProductVersion}
AppPublisher=鱼小兑
DefaultDirName={#AppDirectory}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
MinVersion=10.0.17763
WizardStyle=modern
WizardImageFile=
WizardSmallImageFile=
SetupIconFile=..\WorkIsland.App\Assets\WorkIsland.ico
UninstallDisplayIcon={app}\WorkIsland.exe
OutputDir={#OutputDir}
OutputBaseFilename={#PackageName}
Compression=lzma2/normal
SolidCompression=yes
LicenseFile=..\LICENSE
CloseApplications=no
RestartApplications=no
AlwaysRestart=no
AppMutex=Local\WorkIsland-Installed-{#AppIdValue}
SetupMutex=Local\WorkIsland-Setup-{#AppIdValue}
VersionInfoVersion={#FileVersion}
VersionInfoDescription=Work Island Installer
UninstallDisplayName=Work Island {#ProductVersion}
AllowCancelDuringInstall=yes
DisableWelcomePage=no
DisableDirPage=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.DataPreserved=安装和升级保留工作记录与设置。卸载后数据仍保存在：
english.DataPreserved=Installation and upgrades preserve work history and settings. Uninstall also keeps your data at:
chinesesimplified.Launch=启动 Work Island
english.Launch=Launch Work Island
chinesesimplified.DesktopShortcut=创建桌面快捷方式
english.DesktopShortcut=Create a desktop shortcut
chinesesimplified.CloseFirst=请先从托盘菜单退出此安装目录中的 Work Island，再继续安装或升级。
english.CloseFirst=Exit Work Island from this installation directory using its tray menu, then retry installation or upgrade.
chinesesimplified.InvalidDirectory=程序目录不能与数据目录重叠，也不能是磁盘根目录。
english.InvalidDirectory=The application directory must not overlap the data directory or be a drive root.
chinesesimplified.OtherInstallation=此目录属于其他安装，或者已有数据文件。请选择独立的程序目录。
english.OtherInstallation=This directory belongs to another installation or contains data files. Choose a separate application directory.
chinesesimplified.KeepUpgradePath=升级时请使用原安装目录；如需移动，请先卸载后重新安装。工作记录将保留。
english.KeepUpgradePath=Use the existing installation directory when upgrading. To move it, uninstall then install again. Work history is retained.
chinesesimplified.NoDowngrade=此目录中的程序版本更新，不能用旧安装包覆盖。请使用更新的安装包。
english.NoDowngrade=This directory contains a newer version. Use a newer installer instead of overwriting it with an older one.
chinesesimplified.UnreadableVersion=无法核实已有程序版本，未覆盖文件。请检查目录后重试。
english.UnreadableVersion=The existing application version cannot be verified. No files were overwritten. Check the directory and retry.
chinesesimplified.PendingRecovery=上次升级的恢复副本仍保留在下列目录。请先核实恢复结果，再继续安装：
english.PendingRecovery=A previous upgrade recovery copy remains at the following directory. Verify recovery before continuing:
chinesesimplified.RecoveryFailed=升级失败，旧程序文件未能全部恢复。恢复副本仍保留在：
english.RecoveryFailed=The upgrade failed and some previous files could not be restored. The recovery copy is retained at:

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef TestRoot
; Intentional missing external source triggers the native installer's abort/rollback path.
Source: "{#TestRoot}\missing-rollback-fixture.bin"; DestDir: "{app}\acceptance"; ExternalSize: 1; Flags: external ignoreversion; Check: InjectFailureRequested
#endif

[INI]
Filename: "{app}\installation.ini"; Section: "installation"; Key: "AppId"; String: "{#AppIdValue}"

[UninstallDelete]
Type: files; Name: "{app}\installation.ini"

[Icons]
Name: "{#ShortcutDirectory}\Work Island"; Filename: "{app}\WorkIsland.exe"; Parameters: "{code:GetLaunchArguments}"; WorkingDir: "{app}"; IconFilename: "{app}\WorkIsland.exe"
Name: "{#ShortcutDirectory}\卸载 Work Island"; Filename: "{uninstallexe}"; WorkingDir: "{app}"
#ifdef TestRoot
Name: "{#ShortcutDirectory}\Desktop Work Island"; Filename: "{app}\WorkIsland.exe"; Parameters: "{code:GetLaunchArguments}"; WorkingDir: "{app}"; Tasks: desktopicon
#else
Name: "{userdesktop}\Work Island"; Filename: "{app}\WorkIsland.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

[Run]
Filename: "{app}\WorkIsland.exe"; Parameters: "{code:GetLaunchArguments}"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  AppIdValue = '{#AppIdValue}';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppIdValue}}_is1';

var
  BackupReady, InstallCommitted: Boolean;
  BackupDirectory: String;

function RecoveryDirectory: String;
begin
#ifdef TestRoot
  Result := '{#TestRoot}\Recovery';
#else
  Result := ExpandConstant('{localappdata}\WorkIsland-InstallerRecovery\') + AppIdValue;
#endif
end;

procedure CopyTree(Source, Destination: String);
var Item: TFindRec; SourceItem, DestinationItem: String;
begin
  if not ForceDirectories(Destination) then
    RaiseException('Unable to create recovery directory: ' + Destination);
  if FindFirst(AddBackslash(Source) + '*', Item) then begin
    try
      repeat
        if (Item.Name <> '.') and (Item.Name <> '..') then begin
          SourceItem := AddBackslash(Source) + Item.Name;
          DestinationItem := AddBackslash(Destination) + Item.Name;
          { Never traverse a junction or symbolic link into user data. }
          if (Item.Attributes and $400) <> 0 then
            RaiseException('Recovery refuses linked files or directories: ' + SourceItem);
          if (Item.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
            CopyTree(SourceItem, DestinationItem)
          else if not FileCopy(SourceItem, DestinationItem, False) then
            RaiseException('Unable to copy recovery file: ' + SourceItem);
        end;
      until not FindNext(Item);
    finally
      FindClose(Item);
    end;
  end else
    RaiseException('Unable to enumerate recovery source directory: ' + Source);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then begin
    BackupDirectory := RecoveryDirectory;
    if FileExists(ExpandConstant('{app}\WorkIsland.exe')) then begin
      Log('Work Island: preserving existing application before upgrade at ' + BackupDirectory);
      CopyTree(ExpandConstant('{app}'), BackupDirectory);
      BackupReady := True;
    end;
  end else if CurStep = ssPostInstall then
    InstallCommitted := True;
end;

procedure DeinitializeSetup;
var Restored: Boolean;
begin
  if BackupReady and not InstallCommitted then begin
    Restored := False;
    try
      { Native rollback removes newly installed files, but does not restore overwrites. }
      CopyTree(BackupDirectory, ExpandConstant('{app}'));
      Restored := True;
      Log('Work Island: previous application restored from upgrade recovery copy.');
    except
      Log('Work Island: recovery failed: ' + GetExceptionMessage);
      SuppressibleMsgBox(CustomMessage('RecoveryFailed') + #13#10 + BackupDirectory,
        mbError, MB_OK, IDOK);
    end;
    if not Restored then exit;
  end;
  if (BackupDirectory <> '') and DirExists(BackupDirectory) then begin
    if not DelTree(BackupDirectory, True, True, True) then
      Log('Work Island: unused recovery copy retained at ' + BackupDirectory);
  end;
end;

function CreateFileW(FileName: String; DesiredAccess, ShareMode: LongWord;
  SecurityAttributes: NativeUInt; CreationDisposition, Flags: LongWord; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CanonicalFolder(Value: String): String;
begin
  Result := Uppercase(AddBackslash(ExpandFileName(Value)));
end;

function DataDir: String;
begin
  Result := ExpandConstant('{#DataDirectory}');
end;

function GetLaunchArguments(Param: String): String;
begin
#ifdef TestRoot
  Result := '--interaction-review --data-dir "' + DataDir + '"';
#else
  Result := '';
#endif
end;

#ifdef TestRoot
function InjectFailureRequested: Boolean;
begin
  Result := ExpandConstant('{param:InjectFailure|0}') = '1';
end;
#endif

function ApplicationFileWritable: Boolean;
var Handle: THandle;
begin
  Result := True;
  if not FileExists(ExpandConstant('{app}\WorkIsland.exe')) then exit;
  Handle := CreateFileW(ExpandConstant('{app}\WorkIsland.exe'), $40000000, 0, 0, 3, 0, 0);
  Result := Handle <> THandle(-1);
  if Result then CloseHandle(Handle);
end;

function ValidateDirectory: String;
var
  Target, Data, Recovery, ExistingPath, Marker: String;
  OldVersion, NewVersion: Int64;
begin
  Result := '';
  Target := CanonicalFolder(ExpandConstant('{app}'));
  Data := CanonicalFolder(DataDir);
  Recovery := CanonicalFolder(RecoveryDirectory);
  if DirExists(RecoveryDirectory) then begin
    Result := CustomMessage('PendingRecovery') + #13#10 + RecoveryDirectory; exit;
  end;
  if (Pos(Target, Data) = 1) or (Pos(Data, Target) = 1) or
     (Pos(Target, Recovery) = 1) or (Pos(Recovery, Target) = 1) or
     (Target = CanonicalFolder(AddBackslash(ExtractFileDrive(Target)))) then begin
    Result := CustomMessage('InvalidDirectory'); exit;
  end;
#ifdef TestRoot
  if (Pos(CanonicalFolder('{#TestRoot}'), Target) <> 1) or
     (Target = CanonicalFolder('{#TestRoot}')) then begin
    Result := 'Acceptance installer refuses paths outside its compiled test root.'; exit;
  end;
#endif
  if RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', ExistingPath) and
     (CanonicalFolder(ExistingPath) <> Target) then begin
    Result := CustomMessage('KeepUpgradePath'); exit;
  end;
  Marker := ExpandConstant('{app}\installation.ini');
  if (FileExists(Marker) and (CompareText(GetIniString('installation','AppId','',Marker), AppIdValue) <> 0)) or
     FileExists(ExpandConstant('{app}\state.json')) then begin
    Result := CustomMessage('OtherInstallation'); exit;
  end;
  if FileExists(ExpandConstant('{app}\WorkIsland.exe')) then begin
    if not GetPackedVersion(ExpandConstant('{app}\WorkIsland.exe'), OldVersion) or
       not StrToVersion('{#FileVersion}', NewVersion) then begin
      Result := CustomMessage('UnreadableVersion'); exit;
    end;
    if ComparePackedVersion(OldVersion, NewVersion) > 0 then begin
      Result := CustomMessage('NoDowngrade'); exit;
    end;
    if not ApplicationFileWritable then begin
      Result := CustomMessage('CloseFirst'); exit;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := ValidateDirectory;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var Error: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    Error := ValidateDirectory;
    Result := Error = '';
    if not Result then begin
      Log(Error);
      if not WizardSilent then SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
    end;
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := CustomMessage('DataPreserved') + #13#10 + DataDir;
  WizardForm.FinishedLabel.Caption := CustomMessage('DataPreserved') + #13#10 + DataDir;
end;

function InitializeUninstall: Boolean;
begin
  Result := ApplicationFileWritable;
  if not Result then begin
    Log(CustomMessage('CloseFirst'));
    if not UninstallSilent then SuppressibleMsgBox(CustomMessage('CloseFirst'), mbError, MB_OK, IDOK);
  end;
end;

function StartupExecutable(Command: String): String;
var Finish, Start: Integer; Delimiter: Char;
begin
  Command := Trim(Command);
  Start := 1; Delimiter := ' ';
  if (Length(Command) > 0) and (Command[1] = '"') then begin
    Start := 2; Delimiter := '"';
  end;
  { Keep delimiter search and Copy on the same Unicode character indices. }
  Finish := Start;
  while Finish <= Length(Command) do begin
    if Command[Finish] = Delimiter then break;
    Finish := Finish + 1;
  end;
  Result := Copy(Command, Start, Finish - Start);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command, Candidate, Installed: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, '{#RunKey}', 'WorkIsland', Command) then begin
      Candidate := ExpandFileName(StartupExecutable(Command));
      Installed := ExpandFileName(ExpandConstant('{app}\WorkIsland.exe'));
      if CompareText(Candidate, Installed) = 0 then begin
        if RegDeleteValue(HKCU, '{#RunKey}', 'WorkIsland') then
          Log('Work Island: removed startup command owned by this installation.')
        else
          Log('Work Island: unable to remove owned startup command.');
      end else
        Log('Work Island: retained startup command belonging to another path.');
    end else
      Log('Work Island: no startup command found.');
    Log('User data retained at ' + DataDir);
  end;
end;
