; Compiled by package-exe.ps1. Keep AppId stable across upgrades.
#ifndef BundlePath
  #error BundlePath must point to a complete, staged app bundle.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef OutputPath
  #error OutputPath is required.
#endif
#ifndef AppIcon
  #error AppIcon is required.
#endif
#ifndef CleanupFile
  #error CleanupFile is required. Use package-exe.ps1 to generate it.
#endif

[Setup]
AppId={{8A1799B5-2AA0-45C6-837D-2D87268F1874}
AppName=ZestDrop
AppVersion={#AppVersion}
AppVerName=ZestDrop {#AppVersion}
AppPublisher=Maxaccurate
AppPublisherURL=https://github.com/Maxaccurate/ZestDrop
AppSupportURL=https://github.com/Maxaccurate/ZestDrop/issues
AppUpdatesURL=https://github.com/Maxaccurate/ZestDrop/releases
DefaultDirName={localappdata}\Programs\ZestDrop
DefaultGroupName=ZestDrop
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
WizardStyle=modern
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\ZestDrop.exe
OutputDir={#OutputPath}
OutputBaseFilename=ZestDrop-v{#AppVersion}-windows-x64-setup
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=ZestDrop Setup
Compression=lzma2/max
SolidCompression=yes
LZMADictionarySize=131072
LZMANumBlockThreads=1
LZMAUseSeparateProcess=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BundlePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ZestDrop"; Filename: "{app}\ZestDrop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\ZestDrop"; Filename: "{app}\ZestDrop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ZestDrop.exe"; Description: "{cm:LaunchProgram,ZestDrop}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

#include CleanupFile

[CustomMessages]
english.AppRunning=ZestDrop is running from this installation folder. Finish your jobs and quit it from the tray menu, then try again.
chinesesimplified.AppRunning=此安装目录中的 ZestDrop 正在运行。请完成任务，从托盘菜单退出软件后重试。
chinesetraditional.AppRunning=此安裝目錄中的 ZestDrop 正在執行。請完成工作，從系統匣選單結束軟體後重試。
arabic.AppRunning=يعمل ZestDrop من مجلد التثبيت هذا. أكمل مهامك وأغلقه من قائمة علبة النظام، ثم حاول مرة أخرى.
english.UnsafeFolder=Choose a new, empty folder, or an existing ZestDrop installation folder.
chinesesimplified.UnsafeFolder=请选择新的空文件夹，或已有的 ZestDrop 安装文件夹。
chinesetraditional.UnsafeFolder=請選擇新的空資料夾，或現有的 ZestDrop 安裝資料夾。
arabic.UnsafeFolder=اختر مجلدًا جديدًا فارغًا أو مجلد تثبيت ZestDrop موجودًا.
english.ProcessCheckFailed=Windows could not check running applications. Please retry after Windows Management Instrumentation is available.
chinesesimplified.ProcessCheckFailed=Windows 无法检查运行中的程序。请在 Windows Management Instrumentation 服务可用后重试。
chinesetraditional.ProcessCheckFailed=Windows 無法檢查執行中的程式。請在 Windows Management Instrumentation 服務可用後重試。
arabic.ProcessCheckFailed=تعذر على Windows التحقق من التطبيقات قيد التشغيل. حاول مرة أخرى عندما تتوفر خدمة Windows Management Instrumentation.

[Code]
function InstallFolderAllowed: Boolean;
var
  Folder: String;
  Entry: TFindRec;
begin
  Folder := ExpandConstant('{app}');
  Result := (ExtractFileDrive(Folder) <> '') and
    (CompareText(RemoveBackslashUnlessRoot(Folder), AddBackslash(ExtractFileDrive(Folder))) <> 0);
  if not Result then exit;
  if FileExists(AddBackslash(Folder) + 'ZestDrop.exe') then exit;
  if FindFirst(AddBackslash(Folder) + '*', Entry) then begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then begin
          Result := False;
          exit;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function InstalledAppRunning: Boolean;
var
  Locator, Services, Processes, Process: Variant;
  I: Integer;
  ExePath: String;
begin
  Result := False;
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Services := Locator.ConnectServer('', 'root\CIMV2');
  Processes := Services.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name = ''ZestDrop.exe''');
  for I := 0 to Processes.Count - 1 do begin
    Process := Processes.ItemIndex(I);
    if not VarIsNull(Process.ExecutablePath) then begin
      ExePath := Process.ExecutablePath;
      if CompareText(ExePath, ExpandConstant('{app}\ZestDrop.exe')) = 0 then begin
        Result := True;
        exit;
      end;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not InstallFolderAllowed then begin
    Result := CustomMessage('UnsafeFolder');
    exit;
  end;
  try
    if InstalledAppRunning then Result := CustomMessage('AppRunning');
  except
    Log(GetExceptionMessage);
    Result := CustomMessage('ProcessCheckFailed');
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := False;
  try
    if InstalledAppRunning then begin
      if not UninstallSilent then MsgBox(CustomMessage('AppRunning'), mbError, MB_OK);
      exit;
    end;
  except
    Log(GetExceptionMessage);
    if not UninstallSilent then MsgBox(CustomMessage('ProcessCheckFailed'), mbError, MB_OK);
    exit;
  end;
  Result := True;
end;

function CommandTargetsInstallation(Command: String): Boolean;
var
  Prefix: String;
begin
  Prefix := '"' + ExpandConstant('{app}\ZestDrop.exe') + '"';
  Command := Trim(Command);
  Result := (CompareText(Command, Prefix) = 0) or
    (CompareText(Copy(Command, 1, Length(Prefix) + 1), Prefix + ' ') = 0);
end;

procedure RemoveInstalledExplorerEntry(Key: String);
var
  Command: String;
begin
  if RegQueryStringValue(HKCU, Key + '\command', '', Command) and
    CommandTargetsInstallation(Command) then
    RegDeleteKeyIncludingSubkeys(HKCU, Key);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep <> usUninstall then exit;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ZestDrop', Command) and
    CommandTargetsInstallation(Command) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ZestDrop');
  RemoveInstalledExplorerEntry('Software\Classes\*\shell\ZestDrop');
  RemoveInstalledExplorerEntry('Software\Classes\Directory\shell\ZestDrop');
  { User settings, logs, generated files and registrations belonging to a portable copy are preserved. }
end;
