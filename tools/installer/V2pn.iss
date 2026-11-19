; The engine of the Windows installer. People do not see it: V2pn-windows-<arch>-setup.exe is V2pn's own
; window (v2rayN/V2pnSetup) with this compiled script appended; the window runs it with /VERYSILENT and
; it posts its progress back (/V2PNHWND=<window>, see Notify). tools/release.ps1 compiles it from the
; folder it packs into V2pn-windows-<arch>.zip, so both carry the same files:
;
;   ISCC.exe /DAppVersion=1.3.0 /DArch=x64 /DSourceDir=<folder with V2pn.exe> /DUninstaller=<V2pnSetup.exe> /DOutputDir=<dir> tools\installer\V2pn.iss
;
; The window goes next to V2pn as uninstall.exe, and "Installed apps" starts it instead of Inno's own
; unins000.exe, which it then runs silently (/REMOVEDATA=1 removes servers and settings too). Started by
; hand, the engine still works as an ordinary Inno Setup installer.
;
; It installs for the current Windows user, without administrator rights, into the folder install.ps1
; uses (%LOCALAPPDATA%\Programs\V2pn), so it also updates a copy installed by that script. V2pn keeps
; itself up to date afterwards; the entry in "Installed apps" follows (V2pnUninstallEntry.cs).
;
; Cores (bin\) are put only where there are none: an update never replaces cores either, and the ones
; installed may be newer than the ones this installer carries.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<folder with V2pn.exe>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef OutputName
  #define OutputName "V2pn-windows-" + Arch + "-setup"
#endif

; 1.3.0-local.2026 -> 1.3.0: Windows version fields take numbers only.
#define NumericVersion Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)

[Setup]
; The uninstall entry is HKCU\...\Uninstall\V2pn_is1; V2pn reads it by this name.
AppId=V2pn
AppName=V2pn
AppVersion={#AppVersion}
AppVerName=V2pn {#AppVersion}
AppPublisher=Piwqust
AppPublisherURL=https://github.com/Piwqust/V2pn
AppSupportURL=https://github.com/Piwqust/V2pn/issues
AppUpdatesURL=https://github.com/Piwqust/V2pn/releases
AppCopyright=V2pn contributors. Based on v2rayN by 2dust. GPL-3.0
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription=V2pn {#AppVersion} setup
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={localappdata}\Programs\V2pn
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=yes
UsedUserAreasWarning=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.17763
; V2pn is closed by the person (the tray's Exit puts the system proxy back); see CheckNotRunning.
CloseApplications=no
RestartApplications=no
SetupMutex=V2pnSetup
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile={#SourcePath}..\..\v2rayN\v2rayN.Desktop\Assets\V2pn.ico
UninstallDisplayIcon={app}\V2pn.exe
UninstallDisplayName=V2pn
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern dynamic
WizardSizePercent=100
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.ImportV2rayN=Move servers, subscriptions and settings over from v2rayN, if it is on this computer
ru.ImportV2rayN=Перенести серверы, подписки и настройки из v2rayN, если он есть на этом компьютере
en.V2rayNGroup=At the first start:
ru.V2rayNGroup=При первом запуске:
en.Running=V2pn is running.%n%nQuit it (the tray icon, then Exit) and press Retry. Quitting from the tray puts the system proxy back.
ru.Running=V2pn запущен.%n%nЗакройте его (значок в трее, затем «Выход») и нажмите «Повтор». При выходе через трей системный прокси возвращается как был.
en.NotClosed=V2pn is still running, so nothing was installed. Quit it from the tray and run the installer again.
ru.NotClosed=V2pn всё ещё запущен, поэтому ничего не установлено. Закройте его через трей и запустите установщик снова.
en.Newer=V2pn %1 is installed, newer than this installer (%2).%n%nReplace it with the older version?
ru.Newer=Уже установлен V2pn %1, он новее этого установщика (%2).%n%nЗаменить его более старой версией?
en.RemoveData=Remove your servers, subscriptions and settings as well?%n%nChoose No to keep them for the next install.
ru.RemoveData=Удалить заодно серверы, подписки и настройки?%n%nВыберите «Нет», чтобы сохранить их для следующей установки.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "importv2rayn"; Description: "{cm:ImportV2rayN}"; GroupDescription: "{cm:V2rayNGroup}"; Check: IsFreshInstall

[Files]
Source: "{#SourceDir}\*"; Excludes: "\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\bin\*"; DestDir: "{app}\bin"; Flags: onlyifdoesntexist recursesubdirs createallsubdirs skipifsourcedoesntexist
#ifdef Uninstaller
Source: "{#Uninstaller}"; DestDir: "{app}"; DestName: "uninstall.exe"; Flags: ignoreversion
#endif

[Icons]
Name: "{autoprograms}\V2pn"; Filename: "{app}\V2pn.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\V2pn"; Filename: "{app}\V2pn.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\V2pn.exe"; Description: "{cm:LaunchProgram,V2pn}"; Flags: nowait postinstall skipifsilent

[Code]
var
  WasInstalled: Boolean;

// ---- Talking to V2pn's window -------------------------------------------------------------------
// The messages V2pnSetup/Engine.cs reads: WM_APP+1 with per mille done, WM_APP+2 with a step.
const
  ProgressMessage = $8001;
  StepMessage = $8002;
  StepFiles = 1;
  StepFinish = 2;
  StepUninstall = 11;
  StepUninstallFinish = 12;

procedure Notify(Msg, Value: Integer);
var
  Window: Integer;
begin
  Window := StrToIntDef(ExpandConstant('{param:V2PNHWND|0}'), 0);
  if Window <> 0 then
    PostMessage(Window, Msg, Value, 0);
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if MaxProgress > 0 then
    Notify(ProgressMessage, Integer(Int64(CurProgress) * 1000 div MaxProgress));
end;

function AppDir(): String;
begin
  Result := RemoveBackslash(ExpandConstant('{app}'));
end;

function IsFreshInstall(): Boolean;
begin
  Result := not WasInstalled;
end;

// A process started from the install folder: V2pn, its updater or a core. An elevated V2pn hides its
// path from this query, so a V2pn.exe without one counts as well.
function RunningFromApp(Dir: String): Boolean;
var
  Locator, Service, Processes, Process: Variant;
  I: Integer;
  Path, Name, Prefix, Wql: String;
begin
  Result := False;
  Prefix := Lowercase(AddBackslash(Dir));
  // The filter runs in WMI: reading every process of a busy computer one by one takes minutes.
  // LIKE's "_" matches any letter, so the query may find a little more; the path is compared below.
  Wql := Prefix;
  StringChangeEx(Wql, '\', '\\', True);
  StringChangeEx(Wql, '''', '\''', True);
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process WHERE ExecutablePath LIKE ''' + Wql +
      '%'' OR (Name = ''V2pn.exe'' AND ExecutablePath IS NULL)');
    for I := 0 to Processes.Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      Path := '';
      if not VarIsNull(Process.ExecutablePath) then
        Path := Process.ExecutablePath;
      Path := Lowercase(Path);
      Name := Process.Name;
      // The uninstaller started from the folder waits there for its copy in %TEMP% to finish.
      if (Path <> '') and (Pos(Prefix, Path) = 1) and not WildcardMatch(ExtractFileName(Path), 'unins*.exe')
         and (CompareText(ExtractFileName(Path), 'uninstall.exe') <> 0) then
        Result := True
      else if (Path = '') and (CompareText(Name, 'V2pn.exe') = 0) then
        Result := True;
      if Result then
      begin
        Log('Running from the install folder: ' + Name + ' ' + Path);
        Exit;
      end;
    end;
  except
    Log('Could not list processes: ' + GetExceptionMessage);
  end;
end;

function CheckNotRunning(Dir: String): Boolean;
begin
  while RunningFromApp(Dir) do
  begin
    // Silent runs take the default button, Cancel, instead of asking forever.
    if SuppressibleMsgBox(CustomMessage('Running'), mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
  end;
  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectTasks then
    WasInstalled := FileExists(AddBackslash(WizardDirValue) + 'V2pn.exe');
end;

function GetVersionString(Exe: String): String;
begin
  if not GetVersionNumbersString(Exe, Result) then
    Result := '?';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Exe: String;
  Installed, Own: Int64;
begin
  Result := '';
  Exe := AppDir() + '\V2pn.exe';
  WasInstalled := FileExists(Exe);
  if not CheckNotRunning(AppDir()) then
  begin
    Result := CustomMessage('NotClosed');
    Exit;
  end;
  // V2pn's window has already asked about an older version and passes /ALLOWDOWNGRADE=1 when the person agreed.
  if WasInstalled and (ExpandConstant('{param:ALLOWDOWNGRADE|0}') <> '1')
     and GetPackedVersion(Exe, Installed) and StrToVersion('{#NumericVersion}', Own) then
    if ComparePackedVersion(Installed, Own) > 0 then
      if SuppressibleMsgBox(FmtMessage(CustomMessage('Newer'), [GetVersionString(Exe), '{#AppVersion}']),
          mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then
        Result := SetupMessage(msgSetupAborted);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Temps, Uninstaller: String;
begin
  if CurStep = ssInstall then
    Notify(StepMessage, StepFiles)
  else if CurStep = ssPostInstall then
  begin
    Notify(StepMessage, StepFinish);
    // Read by V2pn at its first start (V2pnImport.AutoMarker), as install.ps1 leaves it.
    // Only on a first install: a silent run with /TASKS chooses the task before the folder is known.
    if WizardIsTaskSelected('importv2rayn') and not WasInstalled then
    begin
      Temps := AppDir() + '\guiTemps';
      ForceDirectories(Temps);
      SaveStringToFile(Temps + '\V2pnImport.txt', 'auto', False);
    end;
  end
  else if CurStep = ssDone then
  begin
    // "Installed apps" opens V2pn's own window, which runs unins000.exe silently itself.
    Uninstaller := AppDir() + '\uninstall.exe';
    if FileExists(Uninstaller) then
    begin
      RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\V2pn_is1', 'UninstallString', '"' + Uninstaller + '"');
      RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\V2pn_is1', 'QuietUninstallString', '"' + Uninstaller + '" /S');
    end;
  end;
end;

// ---- Uninstall ----------------------------------------------------------------------------------

var
  RemoveData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := CheckNotRunning(AppDir());
end;

function PointsHere(Value: String): Boolean;
begin
  Result := Pos(Lowercase(AppDir() + '\'), Lowercase(Value)) > 0;
end;

// What V2pn set up for itself and that would point at a missing V2pn.exe: the start at sign-in (a Run
// value or the V2pnAutoRun task, AutoStartupHandler.cs) and v2pn:// links (V2pnDeepLink.RegisterScheme).
// Only entries that point into this folder; another copy of V2pn keeps its own.
procedure RemoveIntegration();
var
  Value: String;
  Scheduler, Folder, Task: Variant;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'V2pnAutoRun', Value) and PointsHere(Value) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'V2pnAutoRun');

  // Through the Task Scheduler's own object: schtasks prints paths in the console's code page, which
  // mangles a Cyrillic user name. A task made with administrator rights stays: it cannot be removed without them.
  try
    Scheduler := CreateOleObject('Schedule.Service');
    Scheduler.Connect();
    Folder := Scheduler.GetFolder('\');
    Task := Folder.GetTask('V2pnAutoRun');
    Value := Task.Definition.Actions.Item(1).Path;
    if PointsHere(Value) then
      Folder.DeleteTask('V2pnAutoRun', 0);
  except
    Log('Sign-in task: ' + GetExceptionMessage);
  end;

  if RegQueryStringValue(HKCU, 'Software\Classes\v2pn\shell\open\command', '', Value) and PointsHere(Value) then
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\v2pn');
end;

// Updates add files the uninstall log does not list; everything goes except, when kept, the servers
// and settings (guiConfigs) and their backups.
procedure RemoveLeftovers();
var
  Find: TFindRec;
  Name: String;
begin
  if RemoveData then
  begin
    DelTree(AppDir(), True, True, True);
    Exit;
  end;
  if FindFirst(AppDir() + '\*', Find) then
  try
    repeat
      Name := Find.Name;
      if (Name = '.') or (Name = '..') or (CompareText(Name, 'guiConfigs') = 0) or (CompareText(Name, 'guiBackups') = 0) then
        Continue;
      if Find.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        DelTree(AppDir() + '\' + Name, True, True, True)
      else
        DeleteFile(AppDir() + '\' + Name);
    until not FindNext(Find);
  finally
    FindClose(Find);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    Notify(StepMessage, StepUninstall);
    // V2pn's window asks and passes the answer; started by hand, the uninstaller asks itself.
    if ExpandConstant('{param:REMOVEDATA|}') <> '' then
      RemoveData := ExpandConstant('{param:REMOVEDATA|}') = '1'
    else
      RemoveData := DirExists(AppDir() + '\guiConfigs') and
        (SuppressibleMsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES);
    RemoveIntegration();
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    Notify(StepMessage, StepUninstallFinish);
    RemoveLeftovers();
  end;
end;
