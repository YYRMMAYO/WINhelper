; 司南工具箱 安装脚本 (v4.6.0) — 中文、优美、含正常安装内容
; 本机 Inno Setup 未附带 ChineseSimplified.isl，故以 Default.isl 为基，
; 并在 [Messages]/[CustomMessages] 中将所有可见文案覆盖为中文，实现完全中文的安装界面。
; 注：Inno Setup 6 的 [Messages] 标识符以下方为准（参考 Default.isl 6.5.0）。

#define MyAppName "司南工具箱"
#define MyAppVersion "5.5.0"
#define MyAppPublisher "YYRMM"
#define MyAppURL "【完全免费的电脑助手!可以实现官网跳转,电脑帮助等功能】 https://www.bilibili.com/video/BV1gk3g6yEXp/?share_source=copy_web&vd_source=c804f5334fbb4541224a8910a55f757d"
#define MyAppExeName "司南工具箱.exe"
#define MyAppAssocName MyAppName + " File"
#define MyAppAssocExt ".myp"
#define MyAppAssocKey StringChange(MyAppAssocName, " ", "") + MyAppAssocExt

[Setup]
; AppId 唯一标识本应用，请勿在其他安装程序中使用相同值
AppId={{A1449240-6BF4-44D0-8040-9AEE2559BC91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes
DisableProgramGroupPage=yes
; ===== 一键升级支持 =====
; AppMutex：与 CloseApplications 配合，安装时自动关闭正在运行的旧实例并覆盖。
; CloseApplications/RestartApplications：安装前关闭占用文件的旧进程，安装后自动重启，实现"一键流程转移覆盖"。
; DisableDirPage=auto：检测到已安装时复用原目录、跳过目录选择页，升级更顺畅。
AppMutex=YayuToolboxMutex
CloseApplications=yes
RestartApplications=yes
DisableDirPage=auto
PrivilegesRequiredOverridesAllowed=dialog
OutputBaseFilename=司南工具箱_Setup_v{#MyAppVersion}
OutputDir=F:\new\WINHELP\dist\BAND
SetupIconFile=F:\new\WINHELP\AppIcon.ico
SolidCompression=yes
WizardStyle=modern
; 中文优美界面（安装背景图：采用软件图标 AppIcon.ico，置于品牌蓝渐变上）
WizardImageFile=F:\new\WINHELP\setup_bg.bmp
WizardImageStretch=True
; GPL v2 许可协议（v4.9.0：从非法的 [License] 段移入 [Setup]，安装向导将显示许可页）
LicenseFile=F:\new\WINHELP\license.txt

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Messages]
; ===== 欢迎页 =====
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=本向导将引导您完成 [name/ver] 的安装。%n%n建议在继续之前关闭其他所有应用程序。
; ===== 许可协议 =====
WizardLicense=许可协议
LicenseLabel=在安装 [name] 之前，请仔细阅读以下许可协议。
LicenseLabel3=如果您接受协议中的条款，请选择“我接受协议”，然后点击“下一步”继续。如果您选择“我不接受协议”，安装程序将终止。
LicenseAccepted=我接受协议(&A)
LicenseNotAccepted=我不接受协议(&D)
; ===== 选择目录 =====
WizardSelectDir=选择目标位置
SelectDirLabel3=选择 [name] 的安装位置。%n%n安装程序将把 [name] 安装到以下文件夹。
SelectDirBrowseLabel=要安装到其他文件夹，请点击“浏览”。%n%n您也可以直接编辑下面的路径。
DiskSpaceMBLabel=至少需要 [mb] MB 的空闲磁盘空间。
InvalidDirName=文件夹名称无效。
DiskSpaceWarningTitle=磁盘空间不足
DiskSpaceWarning=安装需要至少 %1 KB 的空闲空间，但所选驱动器只有 %2 KB 可用。%n%n是否仍要继续？
; ===== 开始菜单文件夹 =====
WizardSelectProgramGroup=选择“开始”菜单文件夹
SelectStartMenuFolderLabel3=选择“开始”菜单文件夹。%n%n安装程序将以下面的文件夹来创建程序的快捷方式。
SelectStartMenuFolderBrowseLabel=要安装到其他文件夹，请点击“浏览”。
; ===== 附加任务 =====
WizardSelectTasks=选择附加任务
SelectTasksLabel2=请选择安装 [name] 时需要执行的附加任务，然后点击“下一步”。
; ===== 准备安装 =====
WizardReady=准备安装
ReadyLabel1=准备安装
ReadyLabel2a=点击“安装”继续，或点击“上一步”查看或修改设置。
ReadyLabel2b=点击“安装”继续。
ReadyMemoDir=安装位置：
ReadyMemoGroup=开始菜单文件夹：
ReadyMemoTasks=附加任务：
; ===== 安装中 =====
WizardPreparing=正在准备安装…
PreparingDesc=安装程序正在准备安装 [name]，请稍候。
WizardInstalling=正在安装 [name]…
InstallingLabel=安装程序正在安装 [name]，请稍候。
; ===== 完成 =====
FinishedHeadingLabel=完成 [name] 安装
FinishedLabelNoIcons=安装程序已在您的计算机上安装了 [name]。
FinishedLabel=安装程序已完成 [name] 的安装。%n%n点击“完成”以关闭本安装程序。
ClickFinish=点击“完成”以退出安装。
; ===== 通用按钮与提示 =====
ButtonBack=< &上一步
ButtonNext=&下一步 >
ButtonInstall=&安装
ButtonFinish=&完成
ButtonCancel=取消
ButtonBrowse=&浏览...
ButtonYes=&是
ButtonNo=&否
ExitSetupTitle=退出安装
ExitSetupMessage=安装尚未完成。如果您现在退出，程序将不会被安装。%n%n您可以稍后再次运行安装程序来完成安装。%n%n退出安装？
ErrorTitle=错误
SetupAppRunningError=安装程序检测到 %1 正在运行。%n%n请关闭它的所有实例，然后点击“确定”继续，或点击“取消”退出。

[CustomMessages]
CreateDesktopIcon=创建桌面快捷方式(&D)
AdditionalIcons=附加图标：
LaunchProgram=运行 %1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; v5.4.0：自包含文件夹发布（弃用单文件），打包 dist 目录全部内容（exe + 运行时 DLL 等）。
; Excludes 排除安装包输出目录 BAND 与调试符号 / 图标源文件，避免循环打包。
Source: "F:\new\WINHELP\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "BAND,*.pdb,*.svg"
; 注意：不要在任何共享系统文件上使用 "Flags: ignoreversion"

[Registry]
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocExt}\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
