; A small downloader for the immutable, checksum-pinned offline installer.
; This bootstrapper creates no app directory, shortcuts or uninstall entry.
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef InstallerURL
  #error InstallerURL is required.
#endif
#ifndef InstallerSHA256
  #error InstallerSHA256 is required.
#endif
#ifndef OutputPath
  #error OutputPath is required.
#endif
#ifndef AppIcon
  #error AppIcon is required.
#endif
#ifndef DownloadSizeMB
  #define DownloadSizeMB "151"
#endif

[Setup]
AppId=ZestDropOnlineSetup
AppName=ZestDrop
AppVersion={#AppVersion}
AppVerName=ZestDrop {#AppVersion}
AppPublisher=Maxaccurate
AppPublisherURL=https://github.com/Maxaccurate/ZestDrop
AppSupportURL=https://github.com/Maxaccurate/ZestDrop/issues
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CreateAppDir=no
Uninstallable=no
CreateUninstallRegKey=no
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
DisableFinishedPage=yes
UsePreviousLanguage=no
WizardStyle=modern
SetupIconFile={#AppIcon}
OutputDir={#OutputPath}
OutputBaseFilename=ZestDrop-v{#AppVersion}-windows-x64-online-setup
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=ZestDrop Online Setup
Compression=lzma2/max
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[CustomMessages]
english.DownloadTitle=Downloading ZestDrop
english.DownloadDescription=Keep this window open. The installation wizard will open when the download is ready.
english.DownloadSummary=This online installer downloads the complete ZestDrop installation package (about {#DownloadSizeMB} MB). An internet connection is required.%n%nClick Install to download and open the installation wizard.
english.DownloadFailed=The download failed or the file is incomplete. Check your connection and try again, or use the offline installer from the ZestDrop release page.
english.DownloadCancelled=The download was cancelled. Nothing was installed.
english.LaunchFailed=The installation wizard could not be opened. Please try the offline installer from the ZestDrop release page.
chinesesimplified.DownloadTitle=正在下载 ZestDrop
chinesesimplified.DownloadDescription=请保持窗口打开，下载完成后将打开安装向导。
chinesesimplified.DownloadSummary=联网安装器将下载完整的 ZestDrop 安装包（约 {#DownloadSizeMB} MB），需要连接互联网。%n%n点击“安装”开始下载，完成后进入安装向导。
chinesesimplified.DownloadFailed=下载失败或文件不完整。请检查网络后重试，也可从 ZestDrop 发布页面下载离线安装版。
chinesesimplified.DownloadCancelled=下载已取消，未安装任何内容。
chinesesimplified.LaunchFailed=无法打开安装向导，请从 ZestDrop 发布页面下载离线安装版后重试。
chinesetraditional.DownloadTitle=正在下載 ZestDrop
chinesetraditional.DownloadDescription=請保持視窗開啟，下載完成後將開啟安裝精靈。
chinesetraditional.DownloadSummary=線上安裝程式將下載完整的 ZestDrop 安裝套件（約 {#DownloadSizeMB} MB），需要連線至網際網路。%n%n按一下「安裝」開始下載，完成後進入安裝精靈。
chinesetraditional.DownloadFailed=下載失敗或檔案不完整。請檢查網路後重試，也可從 ZestDrop 發布頁面下載離線安裝版。
chinesetraditional.DownloadCancelled=下載已取消，未安裝任何內容。
chinesetraditional.LaunchFailed=無法開啟安裝精靈，請從 ZestDrop 發布頁面下載離線安裝版後重試。
arabic.DownloadTitle=جارٍ تنزيل ZestDrop
arabic.DownloadDescription=اترك هذه النافذة مفتوحة. سيفتح معالج التثبيت عند اكتمال التنزيل.
arabic.DownloadSummary=سيتم تنزيل حزمة تثبيت ZestDrop الكاملة (حوالي {#DownloadSizeMB} ميغابايت). يلزم الاتصال بالإنترنت.%n%nانقر على تثبيت لبدء التنزيل وفتح معالج التثبيت.
arabic.DownloadFailed=فشل التنزيل أو أن الملف غير مكتمل. تحقق من اتصالك وحاول مرة أخرى، أو استخدم برنامج التثبيت دون اتصال من صفحة إصدارات ZestDrop.
arabic.DownloadCancelled=تم إلغاء التنزيل. لم يتم تثبيت أي شيء.
arabic.LaunchFailed=تعذر فتح معالج التثبيت. استخدم برنامج التثبيت دون اتصال من صفحة إصدارات ZestDrop.

[Code]
var
  DownloadPage: TDownloadWizardPage;
  ChildExitCode: Integer;

procedure InitializeWizard;
begin
  ChildExitCode := 0;
  DownloadPage := CreateDownloadPage(CustomMessage('DownloadTitle'), CustomMessage('DownloadDescription'), nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfoInfo, MemoTypeInfoInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := CustomMessage('DownloadSummary');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  DownloadPage.Clear;
  DownloadPage.Add('{#InstallerURL}', 'ZestDrop-setup.exe', '{#InstallerSHA256}');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      Log('Verified installer download completed.');
    except
      Log(GetExceptionMessage);
      if DownloadPage.AbortedByUser then Result := CustomMessage('DownloadCancelled')
      else Result := CustomMessage('DownloadFailed');
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function ChildParameters: String;
var
  I: Integer;
  Arg, Lower: String;
begin
  Result := '/LANG=' + ActiveLanguage;
  { Only documented installation switches are forwarded. Inno's internal
    /SL5 launch data and the outer installer's /LOG must not reach the child. }
  for I := 1 to ParamCount do begin
    Arg := ParamStr(I);
    Lower := Lowercase(Arg);
    if (Lower = '/silent') or (Lower = '/verysilent') or
       (Lower = '/suppressmsgboxes') or (Lower = '/norestart') or
       (Lower = '/nocloseapplications') or (Lower = '/norestartapplications') or
       (Lower = '/noicons') or (Pos('/dir=', Lower) = 1) or
       (Pos('/tasks=', Lower) = 1) or (Pos('/mergetasks=', Lower) = 1) then
      Result := Result + ' ' + AddQuotes(Arg);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssInstall then exit;
  WizardForm.Hide;
  if not Exec(ExpandConstant('{tmp}\ZestDrop-setup.exe'), ChildParameters,
    ExpandConstant('{tmp}'), SW_SHOWNORMAL, ewWaitUntilTerminated, ChildExitCode) then begin
    Log('Could not launch the verified installer: ' + IntToStr(ChildExitCode));
    ChildExitCode := 2;
    if not WizardSilent then MsgBox(CustomMessage('LaunchFailed'), mbError, MB_OK);
  end;
  Log('Offline installer exit code: ' + IntToStr(ChildExitCode));
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := ChildExitCode;
end;
