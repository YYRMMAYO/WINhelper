// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;

namespace WINHELP
{
    /// <summary>
    /// 导航项附加属性 — 控制"激活指示条"显示。
    /// 用附加属性而非 Style 切换，是因为指示条位于 ControlTemplate 内部，
    /// 只有 Trigger 才能改动模板内部元素的属性。
    /// </summary>
    public static class NavProps
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive", typeof(bool), typeof(NavProps),
                new PropertyMetadata(false));

        public static bool GetIsActive(DependencyObject obj) => (bool)obj.GetValue(IsActiveProperty);
        public static void SetIsActive(DependencyObject obj, bool value) => obj.SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml — 主窗口（侧边栏导航主页）
    /// 左侧 9 个导航项点击后在右侧内容区切换 UserControl，不再弹窗。
    /// </summary>
    public partial class MainWindow : Window, INavigationHost
    {
        private string? _downloadUrl;
        private bool _forceClose = false;

        // 玻璃模糊背景缓存：避免每次调节滑块都重新解码壁纸 + 整窗高斯模糊重渲
        private BitmapImage? _backdropBitmap = null;
        private string? _lastBackdropPath = null;
        private GlassMode _lastGlassMode = GlassMode.Translucent;

        // 实时系统状况监测
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _memCounter;
        private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

        // O11：缓存固定色画刷实例，避免 MonitorTick 每 1.5s new SolidColorBrush 造成 GC 抖动
        private static readonly SolidColorBrush _brushGreen  = new(Color.FromRgb(0x27, 0xAE, 0x60));
        private static readonly SolidColorBrush _brushOrange = new(Color.FromRgb(0xFF, 0x98, 0x00));
        private static readonly SolidColorBrush _brushRed    = new(Color.FromRgb(0xE7, 0x4C, 0x3C));
        private static readonly SolidColorBrush _brushGray   = new(Color.FromRgb(0x34, 0x49, 0x5E));

        // 每日贴士
        private const int TipCount = 12;
        private int _tipIndex = 0;

        private static string GetDailyTip(int index)
        {
            return index switch
            {
                0 => UiLanguage.L("按 Win+D 可快速显示桌面，再次按可恢复窗口。", "Press Win+D to show the desktop; press again to restore windows."),
                1 => UiLanguage.L("Ctrl+Shift+Esc 直接打开任务管理器，比 Ctrl+Alt+Del 更快。", "Ctrl+Shift+Esc opens Task Manager directly, faster than Ctrl+Alt+Del."),
                2 => UiLanguage.L("Win+E 一键打开文件资源管理器，方便查找文件。", "Win+E opens File Explorer to find files quickly."),
                3 => UiLanguage.L("Win+Shift+S 可进行区域截图，截图会自动保存到剪贴板。", "Win+Shift+S captures a region screenshot to the clipboard."),
                4 => UiLanguage.L("磁盘空间不足时，可用「系统清理」安全清理临时文件。", "Low on disk? Use System Cleaner to safely remove temp files."),
                5 => UiLanguage.L("禁用不必要的开机启动项，能明显加快开机速度。", "Disabling unneeded startup items noticeably speeds up boot."),
                6 => UiLanguage.L("网络异常时，先用「网络诊断」排查连通性与 DNS。", "Network issues? Run Network Diagnostics to check connectivity & DNS."),
                7 => UiLanguage.L("Win+V 可打开剪贴板历史，找回之前复制过的内容。", "Win+V opens clipboard history to recover previously copied items."),
                8 => UiLanguage.L("Alt+Tab 可在打开的窗口间快速切换。", "Alt+Tab switches quickly between open windows."),
                9 => UiLanguage.L("Win+I 快速进入 Windows 设置，方便调整系统选项。", "Win+I opens Windows Settings to adjust system options."),
                10 => UiLanguage.L("遇到电脑问题别慌，「故障排查向导」会一步步带你排查。", "Don't panic with PC issues—the Troubleshooting Wizard guides you step by step."),
                11 => UiLanguage.L("定期清理回收站和浏览器缓存，可释放不少磁盘空间。", "Clear the recycle bin and browser cache regularly to free disk space."),
                _ => ""
            };
        }

        // 当前显示的页面 key
        private string _currentKey = "home";

        // 页面工厂 / 标题 / 缓存
        private readonly Dictionary<string, Func<UserControl>> _factories = new();
        private readonly Dictionary<string, (string Zh, string En)> _titles = new();
        private readonly Dictionary<string, UserControl> _cache = new();
        private readonly Button[] _navButtons;

        public MainWindow()
        {
            InitializeComponent();
            ThemeManager.SetWindowIcon(this);
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            UiLanguage.Changed += () => Dispatcher.Invoke(Localize);

            Title = $"司南工具箱 v{UpdateManager.LocalVersion}";

            // 侧栏：主界面 + 快捷启动(故障排查/新手导览/装机助手) + 设置分组
            _navButtons = new[]
            {
                NavHome, NavTroubleshoot, NavNovice, NavSetup, NavSettings, NavTheme, NavCompanion,
            };

            InitPages();

            // 初始化今日概览英雄横幅
            InitHeroBanner();

            // 启动实时系统状况监测：推迟到首帧渲染之后执行，
            // 避免 PerformanceCounter 构造 + 首帧采样阻塞窗口首屏呈现，提升启动响应速度
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => InitMonitor());

            // 订阅更新事件
            UpdateManager.UpdateAvailable += info => Dispatcher.Invoke(() => ShowUpdateBar(info));

            // 初始化每日贴士（按日期轮换）
            _tipIndex = (int)(DateTime.Now.DayOfYear % TipCount);
            TxtDailyTip.Text = GetDailyTip(_tipIndex);

            // 默认显示首页：推迟到 Loaded，先让窗口框架与英雄横幅呈现，提升启动响应速度
            Localize();
            Loaded += (_, _) =>
            {
                SetActiveNav("home", NavHome);
                RunNavSmokeIfRequested();
            };
        }

        /// <summary>
        /// 冒烟测试钩子：WINHELP_NAV_SMOKE=1 → 自动遍历全部模块页（每页停留片刻），
        /// 任何页面 XAML 运行期异常都会被全局异常捕获写入 crash.log（供 CI/人工验证，正式运行不触发）。
        /// </summary>
        private void RunNavSmokeIfRequested()
        {
            if (Environment.GetEnvironmentVariable("WINHELP_NAV_SMOKE") != "1") return;
            string marker = Path.Combine(Path.GetTempPath(), "winhelp_navsmoke.txt");
            try { File.WriteAllText(marker, "start " + DateTime.Now.ToString("HH:mm:ss")); } catch { }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, async () =>
            {
                try
                {
                    var keys = ModuleRegistry.All.Select(m => m.Key).ToList();
                    foreach (var k in keys)
                    {
                        try { NavigateByKey(k); await Task.Delay(700); }
                        catch (Exception ex) { App.LogCrash(ex, "NavSmoke:" + k); }
                    }
                    try { File.WriteAllText(marker, "done " + DateTime.Now.ToString("HH:mm:ss")); } catch { }
                    Dispatcher.Invoke(() => Close());
                }
                catch { }
            });
        }

        private void ApplyTheme()
        {
            // 全局背景（共享单例 Brush，支持实时透明度）只应用于主窗口根网格（RootGrid）。
            // 内容容器 PageHost 保持透明，避免被页面再次叠加同一张壁纸造成"图片背景二次重叠"。
            RootGrid.Background = ThemeManager.BackgroundBrush;
            PageHost.Background = Brushes.Transparent;

            // 同步玻璃模糊背景层（Acrylic 模式可见）
            ApplyGlassBackdrop();

            // 星空主题时显示星点装饰层
            ApplyStarryLayer();

            ThemeManager.ApplyButtonTheme(BtnOptimize, ThemeManager.AccentColor);

            // 顶部标题栏 + 英雄横幅随主题色变化（需求 #7）
            ThemeHeroBanner();
        }

        /// <summary>
        /// 让顶部标题栏（CaptionBar）与"今日概览英雄横幅"的日期块随主题强调色实时变化。
        /// 之前这些是硬编码蓝色，换主题色后横幅颜色不变；改为引用 AccentColor 后即时同步。
        /// </summary>
        private void ThemeHeroBanner()
        {
            var accent = ThemeManager.AccentColor;
            var darker = ThemeManager.DarkerColor;

            if (HeroDateBlock != null)
                HeroDateBlock.Background = new SolidColorBrush(Color.FromArgb(0x1A, accent.R, accent.G, accent.B));
            if (TxtHeroDay != null)
                TxtHeroDay.Foreground = new SolidColorBrush(accent);

            if (CaptionBar != null)
            {
                CaptionBar.Background = new LinearGradientBrush(
                    Color.FromArgb(0xCC, accent.R, accent.G, accent.B),
                    Color.FromArgb(0x99, darker.R, darker.G, darker.B),
                    new Point(0, 0), new Point(0, 1));
            }
        }

        /// <summary>
        /// 星空主题装饰层：根据 IsStarryActive 切换 StarryLayer 可见性；
        /// 首次进入星空模式时构建 100 颗随机星点。星点位置由 Random 在窗口尺寸上生成，
        /// IsHitTestVisible=False 不影响鼠标交互。
        /// </summary>
        private void ApplyStarryLayer()
        {
            if (StarryLayer == null) return;
            if (ThemeManager.IsStarryActive)
            {
                if (StarryLayer.Children.Count == 0)
                {
                    BuildStarryStars();
                }
                StarryLayer.Visibility = Visibility.Visible;
            }
            else
            {
                StarryLayer.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildStarryStars()
        {
            StarryLayer.Children.Clear();
            double w = Math.Max(ActualWidth, 1100);
            double h = Math.Max(ActualHeight, 820);
            var rng = new Random(ThemeManager.ActivePresetKey == "aurora" ? 2024 : 7);
            for (int i = 0; i < 110; i++)
            {
                double x = rng.NextDouble() * w;
                double y = rng.NextDouble() * h;
                double size = rng.NextDouble() < 0.85 ? 1.4 + rng.NextDouble() * 1.6 : 2.5 + rng.NextDouble() * 2.0;
                double op = 0.4 + rng.NextDouble() * 0.6;
                var star = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(op * 255), 0xFF, 0xFF, 0xFF)),
                };
                Canvas.SetLeft(star, x);
                Canvas.SetTop(star, y);
                StarryLayer.Children.Add(star);
            }
        }

        /// <summary>
        /// 根据 GlassEffect 切换 BackdropImage 的可见性与图片来源。
        /// Translucent: 隐藏（RootGrid.Background 已展示清晰壁纸）。
        /// Acrylic: 显示并加载同一张图，承载 BlurEffect。
        /// </summary>
        private void ApplyGlassBackdrop()
        {
            if (BackdropImage == null) return;

            bool acrylic = ThemeManager.GlassEffect == GlassMode.Acrylic && ThemeManager.HasBackgroundImage;
            if (acrylic)
            {
                // 仅当图片路径或玻璃模式变化时才重新解码（昂贵）；
                // 透明度 / 强度等常规调节只更新 Opacity，避免整窗高斯模糊被反复重算。
                if (_backdropBitmap == null ||
                    _lastBackdropPath != ThemeManager.BackgroundImagePath ||
                    _lastGlassMode != ThemeManager.GlassEffect)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(ThemeManager.BackgroundImagePath);
                        bmp.EndInit();
                        bmp.Freeze();
                        _backdropBitmap = bmp;
                        _lastBackdropPath = ThemeManager.BackgroundImagePath;
                        _lastGlassMode = ThemeManager.GlassEffect;
                        BackdropImage.Source = bmp;
                        BackdropImage.InvalidateVisual(); // 仅换图时失效重渲
                    }
                    catch
                    {
                        BackdropImage.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                BackdropImage.Opacity = ThemeManager.BackgroundOpacity;
                BackdropImage.Visibility = Visibility.Visible;
            }
            else
            {
                BackdropImage.Visibility = Visibility.Collapsed;
                _lastGlassMode = ThemeManager.GlassEffect;
            }
        }

        /// <summary>注册所有页面的工厂与标题。
        /// 导航 key → 页面类 的映射集中在此；完整模块清单见项目根 MODULES.md。</summary>
        private void InitPages()
        {
            // 模块定义集中维护在 ModuleRegistry.cs（C# 实例），此处仅映射到内部字典，
            // 页面实例化统一走 ModuleRegistry.CreatePage（含导航宿主接线）。
            foreach (var m in ModuleRegistry.All)
            {
                _titles[m.Key] = (m.TitleZh, m.TitleEn);
                _factories[m.Key] = () => ModuleRegistry.CreatePage(m.Key, this);
            }
        }

        // ===== 导航切换 =====

        private void SetActiveNav(string key, Button? active)
        {
            foreach (var b in _navButtons)
            {
                b.Style = (Style)FindResource("NavItemStyle");
                b.FontWeight = FontWeights.Normal;
                NavProps.SetIsActive(b, false);
            }
            if (active != null)
            {
                active.Style = (Style)FindResource("NavItemActiveStyle");
                active.FontWeight = FontWeights.SemiBold;
                NavProps.SetIsActive(active, true);
            }
            ShowPage(key);
        }

        private void ShowPage(string key)
        {
            _currentKey = key;
            if (_titles.TryGetValue(key, out var t)) TxtPageTitle.Text = UiLanguage.L(t.Zh, t.En);
            MainScroll.ScrollToTop();
            if (!_cache.TryGetValue(key, out var page))
            {
                page = _factories[key]();
                _cache[key] = page;
            }
            PageHost.Content = page;
            HookGlobalMouseWheel();

            // 返回主界面时按最新收藏 / 使用频率重排卡片
            if (key == "home" && page is HomePage hp) hp.ApplySort();

            // 返回主界面时自动清空搜索框（需求 #3：搜索后跳转其它模块再回来需清空搜索内容）
            if (key == "home")
            {
                TxtSearch.Text = "";
            }
        }

        /// <summary>
        /// 把 PageHost 内所有 ScrollViewer 的滚轮事件统一转发到外层 MainScroll，
        /// 实现"全模块任意位置（无论是否对准滚动条）都能滚轮滚动"。
        /// 同时挂 PageHost 自身的 PreviewMouseWheel 兜底（无内层 ScrollViewer 时也能响应）。
        /// 切页时调用即可，已挂载的 ScrollViewer 会因切页被 GC 回收，无重复挂载风险。
        /// </summary>
        private void HookGlobalMouseWheel()
        {
            // 遍历新页面可视树中所有 ScrollViewer，把滚轮转发到外层 MainScroll
            foreach (var sv in FindDescendants<ScrollViewer>(PageHost))
            {
                // 先摘除可能残留的转发（防止 _cache 复用同一 UserControl 实例时叠加）
                sv.PreviewMouseWheel -= ForwardToMainScroll;
                sv.PreviewMouseWheel += ForwardToMainScroll;
            }
            // PageHost 自身兜底（子页面无 ScrollViewer、或者鼠标落在 StackPanel 子元素/按钮栏等空白区域）
            PageHost.PreviewMouseWheel -= ForwardToMainScroll;
            PageHost.PreviewMouseWheel += ForwardToMainScroll;
        }

        private void ForwardToMainScroll(object sender, MouseWheelEventArgs e)
        {
            // O5：内层 ScrollViewer 自身还能滚动且该方向未到边界时，交给内层处理（不转发、不 Handled），
            // 实现「内层优先本地滚动」；仅当内层到达边界、或命中非滚动区时，才转发到外层 MainScroll 整页滚动。
            if (sender is ScrollViewer sv && sv.ScrollableHeight > 0.5)
            {
                bool atTop = sv.VerticalOffset <= 0.5;
                bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.5;
                bool scrollUp = e.Delta > 0;
                if (scrollUp && !atTop) return;   // 内层可向上滚 → 放行给内层
                if (!scrollUp && !atBottom) return; // 内层可向下滚 → 放行给内层
            }

            // 到达边界或没有内层可滚：转发到外层 MainScroll，并标记 Handled 避免内层二次滚动
            MainScroll.ScrollToVerticalOffset(MainScroll.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        /// <summary>递归遍历可视树，找出所有指定类型的子元素</summary>
        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child == null) continue;
                if (child is T t) yield return t;
                foreach (var sub in FindDescendants<T>(child)) yield return sub;
            }
        }

        /// <summary>根据页面 key 找到对应导航按钮并切换（供 HomePage 等调用）</summary>
        private void NavigateByKey(string key)
        {
            // 侧栏精简后大部分模块无对应导航按钮，直接用 null 调用 SetActiveNav（仅切换页面，不高亮按钮）
            Button? btn = key switch
            {
                "home" => NavHome,
                "wizard" => NavTroubleshoot,
                "novice" => NavNovice,
                "setup" => NavSetup,
                "settings" => NavSettings,
                "theme" => NavTheme,
                "companion" => NavCompanion,
                _ => null
            };
            SetActiveNav(key, btn);
        }

        // ===== INavigationHost 实现（供 ModuleRegistry.CreatePage 注入导航 / 关闭 / 优化行为） =====
        void INavigationHost.Navigate(string key) => NavigateByKey(key);
        void INavigationHost.Optimize() => BtnOptimize_Click(BtnOptimize, new RoutedEventArgs());
        void INavigationHost.CloseToHome() => SetActiveNav("home", NavHome);
        void INavigationHost.OpenTutorial() => SetActiveNav("tutorial", null);
        void INavigationHost.OpenAgent() => SetActiveNav("agent", null);

        private void NavHome_Click(object sender, RoutedEventArgs e) => SetActiveNav("home", NavHome);
        private void NavTroubleshoot_Click(object sender, RoutedEventArgs e) => SetActiveNav("wizard", NavTroubleshoot);
        private void NavNovice_Click(object sender, RoutedEventArgs e) => SetActiveNav("novice", NavNovice);
        private void NavSetup_Click(object sender, RoutedEventArgs e) => SetActiveNav("setup", NavSetup);
        private void NavSettings_Click(object sender, RoutedEventArgs e) => SetActiveNav("settings", NavSettings);
        private void NavTheme_Click(object sender, RoutedEventArgs e) => SetActiveNav("theme", NavTheme);
        private void NavCompanion_Click(object sender, RoutedEventArgs e) => SetActiveNav("companion", NavCompanion);

        // ===== 多语言：语言切换时重新设置所有静态文本 =====
        private void Localize()
        {
            NavHome.Content = UiLanguage.L("主界面", "Home");
            NavTroubleshoot.Content = UiLanguage.L("故障排查", "Troubleshoot");
            NavNovice.Content = UiLanguage.L("新手导览", "Beginner Guide");
            NavSetup.Content = UiLanguage.L("装机助手", "Setup Assistant");
            NavSettings.Content = UiLanguage.L("设置", "Settings");
            NavTheme.Content = UiLanguage.L("主题", "Appearance");
            NavCompanion.Content = UiLanguage.L("陪伴运行", "Companion");

            TxtGroupQuick.Text = UiLanguage.L("快捷启动", "Quick Launch");
            TxtGroupSettings.Text = UiLanguage.L("设置", "Settings");

            TxtDailyTipTitle.Text = UiLanguage.L("每日使用贴士", "Daily Tip");
            BtnNextTip.Content = UiLanguage.L("换一个", "Next");
            BtnOptimize.Content = UiLanguage.L("一键优化", "One-Click Optimize");

            HeroLabelCpu.Text = UiLanguage.L("CPU", "CPU");
            HeroLabelMem.Text = UiLanguage.L("内存", "Memory");
            HeroLabelNet.Text = UiLanguage.L("网络", "Network");

            TxtHeroGreeting.Text = GetGreeting();
            SetHeroDate();

            if (_titles.TryGetValue(_currentKey, out var t)) TxtPageTitle.Text = UiLanguage.L(t.Zh, t.En);

            BtnDownload.Content = UiLanguage.L("立即下载", "Download");
            BtnDismiss.Content = UiLanguage.L("忽略", "Dismiss");
            if (OptResultPill.Visibility != Visibility.Visible)
                TxtOptResult.Text = UiLanguage.L("已优化", "Optimized");

            TxtSearch.ToolTip = UiLanguage.L("搜索首页功能模块", "Search home features");

            // 刷新动态状态文本
            MonitorTick(null, EventArgs.Empty);
            UpdateHeroExtra();
        }

        // ===== 自定义标题栏按钮 =====
        private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnMax_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized)
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ===== 搜索筛选 =====

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var kw = TxtSearch.Text.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(kw) && _currentKey != "home")
                SetActiveNav("home", NavHome);
            if (PageHost.Content is HomePage hp)
                hp.Filter(kw);
        }

        /// <summary>显示更新提示栏</summary>
        private void ShowUpdateBar(UpdateManager.UpdateInfo info)
        {
            _downloadUrl = info.DownloadUrl;
            UpdateTitle.Text = UiLanguage.L(
                $"发现新版本 v{info.RemoteVersion}（当前 {UpdateManager.FullVersion}）",
                $"New version v{info.RemoteVersion} found (current {UpdateManager.FullVersion})");
            UpdateNotes.Text = string.IsNullOrEmpty(info.ReleaseNotes)
                ? UiLanguage.L("点击「立即下载」获取最新版本", "Click Download to get the latest version")
                : info.ReleaseNotes;
            UpdateBar.Visibility = Visibility.Visible;
        }

        /// <summary>关闭/托盘逻辑</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_forceClose && SettingsManager.Current.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        public void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // ===== 每日贴士 =====

        private void BtnNextTip_Click(object sender, RoutedEventArgs e)
        {
            _tipIndex = (_tipIndex + 1) % TipCount;
            TxtDailyTip.Text = GetDailyTip(_tipIndex);
        }

        private void BtnCloseTip_Click(object sender, RoutedEventArgs e)
        {
            DailyTipCard.Visibility = Visibility.Collapsed;
        }

        // ===== 一键优化 =====

        private async void BtnOptimize_Click(object sender, RoutedEventArgs e)
        {
            BtnOptimize.IsEnabled = false;
            BtnOptimize.Content = UiLanguage.L("优化中…", "Optimizing…");
            OptResultPill.Visibility = Visibility.Collapsed;
            try
            {
                // N7：清理前按需创建系统还原点（权限不足时优雅降级，不阻塞）
                if (SettingsManager.Current.RestorePointEnabled)
                {
                    try { Cleaner.CreateSystemRestorePoint("司南工具箱 一键优化"); } catch { }
                }

                // O6 安全闸：大文件（>200MB）必须先经用户手动确认，避免误删重要文件
                const long LargeThreshold = 200L * 1024 * 1024;

                // 1) 临时目录中的大文件 —— 需手动确认
                var largeTemp = Cleaner.FindLargeTempFiles(LargeThreshold);
                bool allowLargeTemp = true;
                if (largeTemp.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine(UiLanguage.L(
                        $"临时目录中发现 {largeTemp.Count} 个大文件（单个超过 200 MB），清理后无法恢复：",
                        $"Found {largeTemp.Count} large file(s) (>200 MB) in temp folders. Once cleaned they cannot be recovered:"));
                    foreach (var (p, s) in largeTemp.Take(15))
                        sb.AppendLine("• " + System.IO.Path.GetFileName(p) + "  (" + FmtSize(s) + ")");
                    if (largeTemp.Count > 15) sb.AppendLine("• …");
                    sb.AppendLine();
                    sb.AppendLine(UiLanguage.L("是否一并清理这些大文件？", "Clean these large files as well?"));
                    var r = System.Windows.MessageBox.Show(sb.ToString(),
                        UiLanguage.L("大文件清理确认", "Large File Cleanup Confirmation"),
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    allowLargeTemp = r == MessageBoxResult.Yes;
                }

                // 2) 回收站 —— 总体积偏大也需确认
                var (rbSize, rbCount) = Cleaner.QueryRecycleBin();
                bool emptyRecycle = true;
                if (rbSize > LargeThreshold)
                {
                    var msg = UiLanguage.L(
                        $"回收站中包含 {rbCount} 个项目、约 {FmtSize(rbSize)}，清空后无法恢复。是否确认清空？",
                        $"Recycle Bin holds {rbCount} item(s), about {FmtSize(rbSize)}. Emptying cannot be undone. Confirm?");
                    var r = System.Windows.MessageBox.Show(msg,
                        UiLanguage.L("回收站清空确认", "Empty Recycle Bin Confirmation"),
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    emptyRecycle = r == MessageBoxResult.Yes;
                }

                // 3) 执行一键优化（受保护的大文件按用户选择跳过）
                long freed = await System.Threading.Tasks.Task.Run(() =>
                    Cleaner.OneClickOptimize(
                        protectLargeTempBytes: allowLargeTemp ? 0 : LargeThreshold,
                        emptyRecycleBin: emptyRecycle));

                if (SettingsManager.Current.PrivacyCleanEnabled)
                    freed += await System.Threading.Tasks.Task.Run(() => Cleaner.CleanPrivacyTraces());

                // N15：累计优化统计
                SettingsManager.Current.OptimizeCount++;
                SettingsManager.Current.LastOptimize = DateTime.Now;
                SettingsManager.Current.CleanedBytes += freed;
                SettingsManager.Save();

                // 刷新状态
                MonitorTick(null, EventArgs.Empty);
                UpdateHeroExtra();

                // 结果文案补充安全提示
                var note = (!allowLargeTemp ? UiLanguage.L("（已保留大文件）", " (large files kept)") : "")
                         + (!emptyRecycle ? UiLanguage.L("（已保留回收站）", " (recycle bin kept)") : "");
                TxtOptResult.Text = UiLanguage.L($"已清理并释放 {FmtSize(freed)}", $"Cleaned and freed {FmtSize(freed)}") + note;
                OptResultPill.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                // 一键优化异常不应导致程序崩溃：记录并提示用户。
                TxtOptResult.Text = UiLanguage.L("优化出错：", "Optimize error: ") + ex.Message;
                OptResultPill.Visibility = Visibility.Visible;
            }
            finally
            {
                BtnOptimize.Content = UiLanguage.L("一键优化", "One-Click Optimize");
                BtnOptimize.IsEnabled = true;
            }
        }

        // 搜索框回车：仅做应用内筛选（不再跳转网页搜索）
        private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 回车时确保在首页并执行筛选
                if (_currentKey != "home")
                    SetActiveNav("home", NavHome);
                if (PageHost.Content is HomePage hp)
                    hp.Filter(TxtSearch.Text.Trim().ToLowerInvariant());
                e.Handled = true;
            }
        }

        private static string FmtSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        // ===== 今日概览英雄横幅 =====

        private void InitHeroBanner()
        {
            SetHeroDate();
            TxtHeroGreeting.Text = GetGreeting();
            // 摘要文字在 MonitorTick 首次触发时更新
        }

        /// <summary>按当前语言设置英雄横幅日期（日 / 月）</summary>
        private void SetHeroDate()
        {
            var now = DateTime.Now;
            TxtHeroDay.Text = now.Day.ToString();
            string[] monthEn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            TxtHeroMonth.Text = UiLanguage.Current == Lang.En
                ? monthEn[now.Month - 1]
                : $"{now.Month}月";
        }

        /// <summary>根据当前时段返回问候语（随语言切换）</summary>
        private static string GetGreeting()
        {
            int hour = DateTime.Now.Hour;
            return hour switch
            {
                >= 5 and < 12 => UiLanguage.L("早上好，欢迎回来", "Good morning, welcome back"),
                >= 12 and < 18 => UiLanguage.L("下午好，欢迎回来", "Good afternoon, welcome back"),
                >= 18 and < 23 => UiLanguage.L("晚上好，欢迎回来", "Good evening, welcome back"),
                _ => UiLanguage.L("夜深了，注意休息", "Late night—time to rest")
            };
        }

        /// <summary>根据负载百分比返回对应颜色（绿/橙/红）</summary>
        private static Color LoadColor(float pct)
        {
            if (pct >= 85f) return Color.FromRgb(0xE7, 0x4C, 0x3C); // 红
            if (pct >= 60f) return Color.FromRgb(0xFF, 0x98, 0x00); // 橙
            return Color.FromRgb(0x27, 0xAE, 0x60);                 // 绿
        }

        // ===== 实时系统状况监测 =====

        private void InitMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch { _cpuCounter = null; }

            try
            {
                _memCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                _memCounter.NextValue();
            }
            catch { _memCounter = null; }

            if (_cpuCounter == null && _memCounter == null)
            {
                TxtCpu.Text = "CPU N/A";
                TxtMem.Text = "内存 N/A";
            }

            _monitorTimer.Tick += MonitorTick;
            _monitorTimer.Start();
            MonitorTick(null, EventArgs.Empty);
        }

        private int _heroTick = 0;

        private void MonitorTick(object? sender, EventArgs e)
        {
            float cpuPct = -1, memPct = -1;

            try
            {
                if (_cpuCounter != null)
                {
                    cpuPct = _cpuCounter.NextValue();
                    TxtCpu.Text = $"{UiLanguage.L("CPU", "CPU")} {cpuPct:F0}%";
                    TxtCpu.Foreground = cpuPct >= 0 ? LoadBrush(cpuPct) : _brushGray;
                }
                if (_memCounter != null)
                {
                    memPct = _memCounter.NextValue();
                    TxtMem.Text = $"{UiLanguage.L("内存", "Memory")} {memPct:F0}%";
                    TxtMem.Foreground = memPct >= 0 ? LoadBrush(memPct) : _brushGray;
                }
            }
            catch { }

            bool online = false;
            try { online = NetworkInterface.GetIsNetworkAvailable(); } catch { }
            NetDot.Fill = online ? _brushGreen : _brushRed;
            TxtNet.Text = online ? UiLanguage.L("网络 正常", "Network OK") : UiLanguage.L("网络 离线", "Network offline");

            // 同步英雄横幅的状态点（复用同一缓存画刷实例，多元素共享无妨）
            if (cpuPct >= 0) HeroDotCpu.Fill = LoadBrush(cpuPct);
            if (memPct >= 0) HeroDotMem.Fill = LoadBrush(memPct);
            HeroDotNet.Fill = online ? _brushGreen : _brushRed;

            // 摘要文字
            string sysPart = (cpuPct >= 85 || memPct >= 85)
                ? UiLanguage.L("系统负载较高", "High system load")
                : (cpuPct >= 60 || memPct >= 60)
                    ? UiLanguage.L("系统负载中等", "Moderate load")
                    : UiLanguage.L("系统运行正常", "System running normally");
            string netPart = online ? UiLanguage.L("网络在线", "Network online") : UiLanguage.L("网络离线", "Network offline");
            TxtHeroSummary.Text = $"{sysPart} · {netPart}";

            // O8：每 ~10 次刷新更新一次扩展信息（上次优化 + 可清理量），避免频繁查询回收站
            if (_heroTick++ % 10 == 0) UpdateHeroExtra();
        }

        /// <summary>按负载返回对应缓存画刷（绿/橙/红），O11</summary>
        private static SolidColorBrush LoadBrush(float pct)
            => pct >= 85f ? _brushRed : pct >= 60f ? _brushOrange : _brushGreen;

        /// <summary>O8 英雄横幅扩展信息：上次优化时间 + 当前可清理量</summary>
        private void UpdateHeroExtra()
        {
            try
            {
                long pending = Cleaner.SumMatching(Cleaner.TempDirs(), "*", SearchOption.TopDirectoryOnly).size
                             + Cleaner.QueryRecycleBin().size;
                var last = SettingsManager.Current.LastOptimize;
                string lastStr = (last == default) ? UiLanguage.L("从未", "Never") : last.ToString("yyyy-MM-dd HH:mm");
                TxtHeroExtra.Text = $"{UiLanguage.L("上次优化", "Last optimize")}：{lastStr} · " +
                                    $"{UiLanguage.L("可清理", "Reclaimable")}：{FmtSize(pending)}";
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _monitorTimer.Stop();
            _cpuCounter?.Dispose();
            _memCounter?.Dispose();
            base.OnClosed(e);
        }

        // ===== 下载/忽略 =====

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_downloadUrl))
                UpdateManager.OpenDownloadUrl(_downloadUrl);
        }

        /// <summary>
        /// v5.2.0：从 GitHub 直接下载最新 tag 的安装包并安装。
        /// 使用与「检查更新」相同的 tags 版本解析逻辑，下载后强制 SHA-256 校验，
        /// 校验不通过（或发布流程未回填哈希）一律拒绝启动安装。
        /// </summary>
        private async void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            btn.IsEnabled = false;
            BtnDownload.IsEnabled = false;
            try
            {
                UpdateNotes.Text = UiLanguage.L(
                    "正在从 GitHub 获取最新版本…", "Fetching the latest version from GitHub…");

                var prog = new Progress<(long Read, long Total)>(p =>
                {
                    double pct = p.Total > 0 ? p.Read * 100.0 / p.Total : 0;
                    UpdateNotes.Text = UiLanguage.L(
                        string.Format("正在下载安装包… {0:F0}%", pct),
                        string.Format("Downloading installer… {0:F0}%", pct));
                });

                string? path = await UpdateManager.DownloadLatestAsync(prog);
                if (path == null)
                {
                    UpdateNotes.Text = UiLanguage.L(
                        "下载失败或 SHA-256 校验未通过（可能发布流程尚未完成）。可点击「下载页面」手动获取。",
                        "Download failed or SHA-256 verification failed (the release may not be complete). Use the download page instead.");
                    return;
                }

                UpdateNotes.Text = UiLanguage.L(
                    "下载完成，正在准备安装…", "Download complete, preparing to install…");
                var r = MessageBox.Show(
                    UiLanguage.L(
                        "最新版本安装包已下载并通过完整性校验。\n是否立即运行安装程序？（安装程序会请求管理员权限）",
                        "The latest installer has been downloaded and verified.\nRun it now? (It will request administrator permission)"),
                    UiLanguage.L("安装更新", "Install Update"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
                if (r == MessageBoxResult.Yes && UpdateManager.LaunchInstaller(path))
                {
                    UpdateNotes.Text = UiLanguage.L(
                        "安装程序已启动。安装完成后软件将自动升级到最新版本。",
                        "The installer has started. The app will upgrade once installation finishes.");
                }
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "MainWindow.BtnUpdateNow");
                UpdateNotes.Text = UiLanguage.L(
                    "下载更新时发生错误，请稍后重试。", "An error occurred while downloading the update. Retry later.");
            }
            finally
            {
                btn.IsEnabled = true;
                BtnDownload.IsEnabled = true;
            }
        }

        private void BtnDismiss_Click(object sender, RoutedEventArgs e)
        {
            UpdateBar.Visibility = Visibility.Collapsed;
        }

        // F11：切换陪伴运行；Ctrl+K：全局命令面板
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                CompanionManager.Toggle();
                e.Handled = true;
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.K)
            {
                OpenCommandPalette();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        /// <summary>唤起全局命令面板（Ctrl+K）：跨所有模块 + 动作搜索直达。</summary>
        private void OpenCommandPalette()
        {
            var dlg = new SearchWindow(BuildCommandItems());
            dlg.Owner = this;
            dlg.ShowDialog();
        }

        /// <summary>构造命令面板的数据源：全部模块（跳转）+ 一组安全动作（直接执行）。</summary>
        private List<CommandItem> BuildCommandItems()
        {
            var list = new List<CommandItem>();
            // 模块项直接取自 ModuleRegistry（图标 / 标题已集中维护），与导航、首页卡片同源
            foreach (var m in ModuleRegistry.All)
            {
                list.Add(new CommandItem
                {
                    Key = m.Key,
                    Icon = m.Icon,
                    Group = UiLanguage.L("模块", "Module"),
                    TitleZh = m.TitleZh, TitleEn = m.TitleEn,
                    Execute = () => NavigateByKey(m.Key),
                });
            }
            var actG = UiLanguage.L("动作", "Action");
            list.Add(new CommandItem
            {
                Key = "act:optimize", Icon = "✨", Group = actG,
                TitleZh = "一键优化", TitleEn = "One-Click Optimize",
                SubZh = "清理临时文件并清空回收站", SubEn = "Clean temp & empty recycle bin",
                Execute = () => BtnOptimize_Click(BtnOptimize, new RoutedEventArgs()),
            });
            list.Add(new CommandItem
            {
                Key = "act:companion", Icon = "🐾", Group = actG,
                TitleZh = "切换陪伴运行", TitleEn = "Toggle Companion",
                SubZh = "显示 / 隐藏陪伴小窗", SubEn = "Show / hide companion window",
                Execute = () => CompanionManager.Toggle(),
            });
            list.Add(new CommandItem
            {
                Key = "act:followsys", Icon = "🌗", Group = actG,
                TitleZh = "跟随系统主题", TitleEn = "Follow System Theme",
                SubZh = "自动随系统浅色 / 深色切换", SubEn = "Auto match system light / dark",
                Execute = () => ThemeManager.SetFollowSystem(!ThemeManager.FollowSystem),
            });
            list.Add(new CommandItem
            {
                Key = "act:privacy", Icon = "🧼", Group = actG,
                TitleZh = "清理隐私痕迹", TitleEn = "Clean Privacy Traces",
                SubZh = "清理浏览器等隐私痕迹", SubEn = "Clean browser privacy traces",
                Execute = () => System.Threading.Tasks.Task.Run(() => { try { Cleaner.CleanPrivacyTraces(); } catch { } }),
            });
            return list;
        }
    }
}
