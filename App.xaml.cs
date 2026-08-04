// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace WINHELP
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 安装全局异常捕获：任何线程上的未处理异常都会被记录到 crash.log，
            // UI 线程异常还会被标记为已处理并弹出提示，避免“闪退”（直接关闭、无任何信息）。
            InstallGlobalExceptionHandlers();

            // base.OnStartup 必须调用一次（处理命令行、触发 Startup 事件等），且使用真实参数 e。
            base.OnStartup(e);

            // 1. 第一时间展示启动动画：点击软件优先播放动画，后台继续启动软件。
            //    这样即使后续初始化略有耗时，用户也能立刻获得"程序已启动"的反馈，
            //    主观上显著改善"启动响应慢"的体感。
            var splash = new SplashWindow();
            splash.Show();

            // 2. 将后续较重的初始化移到后台异步流程，避免阻塞启动动画的首帧渲染。
            _ = StartupCoreAsync(splash);
        }

        /// <summary>
        /// 启动核心流程（在启动动画呈现后于后台继续）。
        /// 逻辑与原 OnStartup 的同步流程一致，仅改为 async 以不阻塞启动动画。
        /// </summary>
        private async Task StartupCoreAsync(SplashWindow splash)
        {
            // 让启动动画先播放约 0.5s，给用户明确的"已启动"反馈，再继续后台启动。
            await Task.Delay(500);

            try
            {
                // 1. 加载主题配置
                ThemeManager.Load();
                // 1.0 应用已保存的全局界面字体（在首个窗口显示前生效）
                ThemeManager.ApplyFont();

            // 1.05 注册玻璃共享画刷到 Application.Resources
            //      （必须在首个窗口 Show 之前调用，DynamicResource 才能解析）
            ThemeManager.RegisterGlassResources();

            // 1.06 若开启「跟随系统深浅色」，订阅系统主题变化（P1-7）
            ThemeManager.InitFollowSystem();

            // 1.1 加载 UI 语言（中文 / 英文）
            UiLanguage.Load();

            // 2. 加载应用设置
            SettingsManager.Load();

            // 2.05 加载插件清单（New B：轻量扩展机制，无插件时静默跳过）
            PluginLoader.Load();

            // 2.1 加载陪伴运行（小窗模式）设置
            CompanionSettingsManager.Load();

            // 2.2 加载 Agent 助手（自定义 API）设置
            AgentSettingsManager.Load();

            // 3. 应用开机自动启动（同步注册表与设置）
            SettingsManager.SetAutoStart(SettingsManager.Current.AutoStart);

            // 4. 从 SiteFinderPage "软件版本"文字模块初始化版本号（版本检测的检测路径）
            //    实际解析（构造隐藏的 SiteFinderPage 实例）开销较大，推迟到首帧渲染后执行。

            // 5. 创建系统托盘图标（必须在主窗口显示前完成）
            SetupTrayIcon();

            // 6. 创建并显示主窗口
            _mainWindow = new MainWindow();
            _mainWindow.Show();

            // 6.x 主窗口首帧稳定后再淡出启动动画
            await Task.Delay(250);
            splash?.FadeOutAndClose();

            // 6.x 启动后的非关键初始化：推迟到首帧渲染（Loaded）之后执行，
            //      避免阻塞主窗口首次呈现，显著提升启动响应速度。
            //      注：Dispatcher.BeginInvoke 返回可 await 的 DispatcherOperation，
            //      此处为刻意"即发即弃"，用 _ = 丢弃返回值以消除 CS4014 警告。
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                try
                {
                    // 4. 解析软件版本号
                    SiteFinderPage.EnsureVersionInitialized();

                    // 4.1 记录/补齐版本历史
                    UpgradeTracker.Initialize();

                    // 6.1 注册全局热键 Ctrl+Alt+C，可从托盘一键切换陪伴运行小窗
                    var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow);
                    CompanionManager.RegisterGlobalHotkey(helper.Handle);

                    // 6.3 启动定时自动优化计划（N5）：按 SettingsManager 中的计划设置，
                    //     在运行期每分钟检查一次并在命中时间时执行一键优化。
                    SchedulerManager.Start();

                    // 7. 如果开启了自动检查更新，启动后自动检测
                    if (SettingsManager.Current.AutoCheckUpdate)
                        _ = UpdateManager.CheckAsync();

                    // 8. 用实际注册成功的全局热键更新主窗口导航提示
                    var hk = CompanionManager.HotkeyLabel;
                    if (!string.IsNullOrEmpty(hk) && _mainWindow != null)
                        _mainWindow.NavCompanion.ToolTip = $"陪伴运行小窗（{hk} 或 F11）";

                    // 9. 冒烟测试钩子：WINHELP_HOTKEY_DEBUG=1 → 写入热键注册 marker 文件
                    if (Environment.GetEnvironmentVariable("WINHELP_HOTKEY_DEBUG") == "1")
                    {
                        try
                        {
                            var marker = Path.Combine(Path.GetTempPath(), "winhelp_hotkey_debug.txt");
                            File.WriteAllText(marker, CompanionManager.HotkeyLabel ?? "registered");
                        }
                        catch { }
                    }

                    // 10. 冒烟测试钩子：WINHELP_COMPANION_AUTO=enter-exit → 自动进入陪伴模式数秒后退出
                    if (Environment.GetEnvironmentVariable("WINHELP_COMPANION_AUTO") == "enter-exit")
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
                        {
                            try
                            {
                                await Task.Delay(800);
                                CompanionManager.Enter();
                                await Task.Delay(4500);
                                CompanionManager.Exit();
                            }
                            catch { }
                        });
                    }

                }
                catch (Exception ex)
                {
                    LogCrash(ex, "App.OnStartup.Deferred");
                }
            });
            }
            catch (Exception ex)
            {
                // 启动期致命异常：记录到 crash.log 并提示用户，避免“闪退”无信息。
                LogCrash(ex, "App.OnStartup");
                try
                {
                    splash?.Close();
                    System.Windows.MessageBox.Show(
                        "程序启动时发生错误，详情已记录到日志文件。\n\n" + ex.Message,
                        "司南工具箱启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
                Shutdown();
            }
        }

        // ===== 全局异常捕获 =====

        private static string CrashLogDir
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINHELP");

        /// <summary>
        /// 安装三层异常捕获：
        /// - Dispatcher.UnhandledException：UI 线程未处理异常 → 标记已处理 + 弹提示，不让程序“闪退”。
        /// - AppDomain.CurrentDomain.UnhandledException：非 UI 线程（如定时器/后台线程）未处理异常 → 记录日志。
        /// - TaskScheduler.UnobservedTaskException：Fire-and-forget 任务异常 → 记录并标记为已观察，避免进程被终止。
        /// 任何异常都会写入 %LOCALAPPDATA%/WINHELP/crash.log（含时间、类型、消息、完整堆栈）。
        /// </summary>
        private void InstallGlobalExceptionHandlers()
        {
            Dispatcher.UnhandledException += (_, args) =>
            {
                // UI 线程异常：记录并“吞掉”，使程序继续运行而非直接关闭。
                LogCrash(args.Exception, "Dispatcher.UnhandledException");
                args.Handled = true;
                try
                {
                    System.Windows.MessageBox.Show(
                        "发生了一个界面错误，已记录到日志；程序将继续运行。\n如频繁出现，请反馈此问题。\n\n" + args.Exception.Message,
                        "司南工具箱运行提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { /* 弹窗失败也不能再抛出 */ }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var ex = args.ExceptionObject as Exception ?? new Exception("Unknown non-UI exception");
                LogCrash(ex, "AppDomain.CurrentDomain.UnhandledException" +
                    (args.IsTerminating ? " (terminating)" : ""));
                // 非 UI 线程致命异常无法恢复，但已记录；若尚未终止，弹出一个提示。
                if (!args.IsTerminating)
                {
                    try
                    {
                        Dispatcher.Invoke(() => System.Windows.MessageBox.Show(
                            "发生了一个后台错误，已记录到日志。\n\n" + ex.Message,
                            "司南工具箱运行提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    catch { }
                }
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                // Fire-and-forget 的 Task 异常：记录并标记为已观察，防止进程被终止。
                LogCrash(args.Exception, "TaskScheduler.UnobservedTaskException");
                args.SetObserved();
            };
        }

        /// <summary>把异常信息追加写入 crash.log（文件锁保护，避免多进程并发写入冲突）。</summary>
        internal static void LogCrash(Exception ex, string context)
        {
            try
            {
                Directory.CreateDirectory(CrashLogDir);
                var path = Path.Combine(CrashLogDir, "crash.log");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("========== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==========");
                sb.AppendLine("Context : " + context);
                sb.AppendLine("Type    : " + ex.GetType().FullName);
                sb.AppendLine("Message : " + ex.Message);
                sb.AppendLine("Stack   : " + ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine("Inner   : " + ex.InnerException.GetType().FullName + " | " + ex.InnerException.Message);
                    sb.AppendLine("InnerStk: " + ex.InnerException.StackTrace);
                }
                sb.AppendLine();
                // 用 FileShare.ReadWrite 让另一个实例也能写入，不互相阻塞。
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs);
                sw.Write(sb.ToString());
                sw.Flush();
            }
            catch { /* 日志写入失败不应引发二次异常 */ }
        }

        /// <summary>创建系统托盘图标</summary>
        private void SetupTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = UpdateManager.FullVersion,
                Visible = true
            };

            // 尝试从程序集提取图标
            try
            {
                var exePath = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
                _notifyIcon.Icon = Icon.ExtractAssociatedIcon(exePath);
            }
            catch
            {
                // 使用默认图标
                _notifyIcon.Icon = SystemIcons.Application;
            }

            // 双击托盘图标恢复窗口
            _notifyIcon.DoubleClick += (s, e) =>
            {
                if (CompanionManager.IsInCompanionMode)
                    CompanionManager.Exit();        // 陪伴模式下双击托盘 → 返回正常程序
                else
                    _mainWindow?.RestoreFromTray();
            };

            // 右键菜单
            var contextMenu = new ContextMenuStrip();

            var showItem = new ToolStripMenuItem("显示主窗口");
            showItem.Click += (s, e) =>
            {
                if (CompanionManager.IsInCompanionMode)
                    CompanionManager.Exit();
                else
                    _mainWindow?.RestoreFromTray();
            };
            contextMenu.Items.Add(showItem);

            var companionItem = new ToolStripMenuItem("陪伴运行（小窗）");
            companionItem.Click += (s, e) => CompanionManager.Toggle();
            contextMenu.Items.Add(companionItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出司南工具箱");
            exitItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        /// <summary>退出应用程序</summary>
        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;

            // 使用 ForceClose 跳过托盘隐藏逻辑
            _mainWindow?.ForceClose();
            _mainWindow = null;

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 注销全局热键
            CompanionManager.UnregisterGlobalHotkey();
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            base.OnExit(e);
        }
    }
}
