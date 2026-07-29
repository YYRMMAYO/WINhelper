; ===== WIN HELP 安装程序脚本 =====
; 使用 Inno Setup 编译生成安装程序
; 编译命令: ISCC.exe WINHELP_Installer.iss

#define MyAppName "WIN HELP"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "YYRMM"
#define MyAppExeName "WINHELP.exe"
#define MyAppURL "https://github.com/YYRMMAYO/WINhelper"

[Setup]
; 应用信息
AppId={{B8F3A2E1-7D5C-4A9E-B6F1-3C2D8E7A5F90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; 安装程序输出
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=F:\new\WINHELP\dist\BAND
OutputBaseFilename=WINHELP_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 卸载设置
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; 界面设置
SetupIconFile=F:\new\WINHELP\AppIcon.ico
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Messages]
; 简体中文界面消息
SetupAppTitle=安装 - {#MyAppName}
SetupWindowTitle={#MyAppName} {#MyAppVersion} 安装程序
WelcomeLabel1=欢迎使用 {#MyAppName} 安装程序
WelcomeLabel2=此向导将引导您完成 {#MyAppName} 的安装。%n%n建议在继续之前关闭其他正在运行的应用程序。
SelectDirLabel3=安装程序将把 {#MyAppName} 安装到以下文件夹。%n%n如需安装到其他位置，请点击「浏览」选择目标文件夹。
SelectDirBrowseLabel=如需继续，请点击「下一步」；如需选择其他文件夹，请点击「浏览」。
DiskSpaceMBLabel=至少需要 [mb] MB 的可用磁盘空间。
ReadyLabel1=安装程序已准备好开始安装 {#MyAppName} 到您的计算机。
ReadyLabel2a=点击「安装」开始安装，或点击「上一步」修改设置。
InstallingLabel=正在安装 {#MyAppName}，请稍候...
FinishedHeadingLabel={#MyAppName} 安装完成
FinishedLabelNoIcons={#MyAppName} 已成功安装到您的计算机。
FinishedLabel={#MyAppName} 已成功安装到您的计算机。
ClickFinish=点击「完成」退出安装程序。
SetupAbortedMessage=安装未完成。请修正问题后重新运行安装程序。
UninstallAppTitle=卸载 - {#MyAppName}
UninstallAppFullTitle=卸载 {#MyAppName}
ConfirmUninstall=确定要完全卸载 {#MyAppName} 及其所有组件吗？
UninstalledAll={#MyAppName} 已成功从您的计算机卸载。
UninstallNotFound={#MyAppName} 未安装或已被卸载。

; 按钮文字
ButtonNext=下一步(&N) >
ButtonBack=< 上一步(&B)
ButtonInstall=安装(&I)
ButtonFinish=完成(&F)
ButtonCancel=取消
ButtonBrowse=浏览(&B)...
ButtonYes=是(&Y)
ButtonNo=否(&N)
ButtonOK=确定

; 目录选择
BrowseDialogTitle=选择目标文件夹
BrowseDialogLabel=选择 {#MyAppName} 的安装位置：
NewFolderName=新建文件夹

; 任务
TasksNameTask=选择附加任务
TasksDescriptionTask=选择安装程序要执行的附加任务，然后点击「安装」：

[Tasks]
Name: "desktopicon"; Description: "在桌面创建快捷方式"; GroupDescription: "附加选项:"; Flags: checkedonce
Name: "startmenuicon"; Description: "在开始菜单创建快捷方式"; GroupDescription: "附加选项:"; Flags: checkedonce

[Files]
; 主程序文件（单文件自包含 exe）
Source: "F:\new\WINHELP\dist\WINHELP.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 开始菜单快捷方式
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"

; 桌面快捷方式
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 安装完成后可选择启动程序
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 卸载时关闭正在运行的程序
Filename: "{cmd}"; Parameters: "/c taskkill /F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// 安装前检查：如果程序正在运行，提示用户关闭
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // 尝试关闭正在运行的旧版本
  Exec(ExpandConstant('{cmd}'), '/c taskkill /F /IM {#MyAppExeName}', '', SW_HIDE, ewNoWait, ResultCode);
  Result := True;
end;

// 卸载前检查：如果程序正在运行，先关闭
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /F /IM {#MyAppExeName}', '', SW_HIDE, ewNoWait, ResultCode);
  Result := True;
end;
