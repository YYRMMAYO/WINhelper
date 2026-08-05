// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
    /// 主窗口（v5.3.0 全新壳）：
    /// 浅色标题栏 + 数据驱动左侧导航 + 宽松内容区。
    /// 侧栏导航由 NavSpec 静态配置生成（不再硬编码 7 个按钮与 7 个事件处理器），
    /// 全部模块入口统一走 ModuleRegistry 单一事实源；首页卡片承载其余模块。
    /// </summary>
    public partial class MainWindow : Window, INavigationHost
    {
        private string? _downloadUrl;
        private bool _forceClose = false;
        private string _currentKey = "home";

        // 玻璃模糊背景缓存：避免每次调节滑块都重新解码壁纸 + 整窗高斯模糊重渲
        private BitmapImage? _backdropBitmap = null;
        private string? _lastBackdropPath = null;
        private GlassMode _lastGlassMode = GlassMode.Translucent;

        // 页面工厂 / 标题 / 缓存
        private readonly Dictionary<string, Func<UserControl>> _factories = new();
        private readonly Dictionary<string, (string Zh, string En)> _titles = new();
        private readonly Dictionary<string, UserControl> _cache = new();

        // 动态导航按钮与分组标题（BuildNav 生成）
        private readonly List<Button> _navButtons = new();
        private readonly List<(TextBlock tb, string zh, string en)> _navHeaders = new();
        private Button NavHome = null!;
        /// <summary>陪伴运行导航按钮（App.xaml.cs 需要设置 ToolTip 显示热键）</summary>
        public Button NavCompanion { get; private set; } = null!;

        /// <summary>
        /// 侧栏导航结构：分组标题（空串表示无分组标题）+ 模块 key 列表。
        /// 只放最常用入口，其余模块由首页卡片 / Ctrl+K 命令面板承载。
        /// </summary>
        private static readonly (string GroupZh, string GroupEn, string[] Keys)[] NavSpec =
        {
            ("", "", new[] { "home" }),
            ("系统工具", "System Tools", new[] { "clean", "startup", "system", "net", "issue", "rescue" }),
            ("设置", "Settings", new[] { "theme", "settings", "companion" }),
        };

        // 优化结果提示自动隐藏计时器
        private readonly DispatcherTimer _resultTimer =
            new() { Interval = TimeSpan.FromSeconds(8) };

        public MainWindow()
        {
            InitializeComponent();
            ThemeManager.SetWindowIcon(this);
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            UiLanguage.Changed += () => Dispatcher.Invoke(Localize);

            Title = $"司南工具箱 v{UpdateManager.LocalVersion}";
            TxtVersion.Text = "v" + UpdateManager.LocalVersion;
            TxtNavFooter.Text = $"v{UpdateManager.LocalVersion} · GPL v2 · 免费开源";

            InitPages();
            BuildNav();

            _resultTimer.Tick += (_, _) =>
            {
                _resultTimer.Stop();
                TxtOptResult.Visibility = Visibility.Collapsed;
            };

            UpdateManager.UpdateAvailable += info => Dispatcher.Invoke(() => ShowUpdateBar(info));

            Localize();
            // 默认显示首页：推迟到 Loaded，先让窗口框架呈现，提升启动响应速度
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
            Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
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
            // 全局背景（共享单例 Brush）只应用于主窗口根网格（RootGrid）。
            // 内容容器 PageHost 保持透明，避免被页面再次叠加同一张壁纸造成"图片背景二次重叠"。
            RootGrid.Background = ThemeManager.BackgroundBrush;
            PageHost.Background = Brushes.Transparent;

            // 同步玻璃模糊背景层（Acrylic 模式可见）
            ApplyGlassBackdrop();

            // 星空主题时显示星点装饰层
            ApplyStarryLayer();

            ThemeManager.ApplyButtonTheme(BtnOptimize, ThemeManager.AccentColor);
        }

        /// <summary>
        /// 星空主题装饰层：根据 IsStarryActive 切换 StarryLayer 可见性；
        /// 首次进入星空模式时构建 110 颗随机星点。IsHitTestVisible=False 不影响鼠标交互。
        /// </summary>
        private void ApplyStarryLayer()
        {
            if (StarryLayer == null) return;
            if (ThemeManager.IsStarryActive)
            {
                if (StarryLayer.Children.Count == 0) BuildStarryStars();
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
                        BackdropImage.InvalidateVisual();
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

        /// <summary>注册所有页面的工厂与标题（模块定义统一来自 ModuleRegistry）。</summary>
        private void InitPages()
        {
            foreach (var m in ModuleRegistry.All)
            {
                _titles[m.Key] = (m.TitleZh, m.TitleEn);
                _factories[m.Key] = () => ModuleRegistry.CreatePage(m.Key, this);
            }
        }

        /// <summary>依据 NavSpec 动态生成左侧导航（按钮 + 分组标题），Tag 即模块 key。</summary>
        private void BuildNav()
        {
            foreach (var (gZh, gEn, keys) in NavSpec)
            {
                if (!string.IsNullOrEmpty(gZh))
                {
                    var h = new TextBlock
                    {
                        Style = (Style)FindResource("NavGroupHeader"),
                        Text = UiLanguage.L(gZh, gEn),
                    };
                    _navHeaders.Add((h, gZh, gEn));
                    NavPanel.Children.Add(h);
                }
                foreach (var k in keys)
                {
                    var m = ModuleRegistry.Find(k);
                    if (m == null) continue;
                    var b = new Button
                    {
                        Tag = k,
                        Style = (Style)FindResource("NavItemStyle"),
                        Content = UiLanguage.L(m.TitleZh, m.TitleEn),
                    };
                    b.Click += NavBtn_Click;
                    _navButtons.Add(b);
                    NavPanel.Children.Add(b);
                    if (k == "home") NavHome = b;
                    if (k == "companion") NavCompanion = b;
                }
            }
        }

        private void NavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string key)
                SetActiveNav(key, b);
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

            // 返回主界面时重排卡片（收藏/使用频率）并刷新首页统计
            if (key == "home" && page is HomePage hp)
            {
                hp.ApplySort();
                hp.RefreshStats();
                TxtSearch.Text = "";
            }
        }

        /// <summary>
        /// 把 PageHost 内所有 ScrollViewer 的滚轮事件统一转发到外层 MainScroll，
        /// 实现"全模块任意位置（无论是否对准滚动条）都能滚轮滚动"。
        /// </summary>
        private void HookGlobalMouseWheel()
        {
            foreach (var sv in FindDescendants<ScrollViewer>(PageHost))
            {
                sv.PreviewMouseWheel -= ForwardToMainScroll;
                sv.PreviewMouseWheel += ForwardToMainScroll;
            }
            PageHost.PreviewMouseWheel -= ForwardToMainScroll;
            PageHost.PreviewMouseWheel += ForwardToMainScroll;
        }

        private void ForwardToMainScroll(object sender, MouseWheelEventArgs e)
        {
            // 内层 ScrollViewer 自身还能滚动且该方向未到边界时，交给内层处理；
            // 仅当内层到达边界、或命中非滚动区时，才转发到外层 MainScroll 整页滚动。
            if (sender is ScrollViewer sv && sv.ScrollableHeight > 0.5)
            {
                bool atTop = sv.VerticalOffset <= 0.5;
                bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.5;
                bool scrollUp = e.Delta > 0;
                if (scrollUp && !atTop) return;
                if (!scrollUp && !atBottom) return;
            }
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
            Button? btn = _navButtons.FirstOrDefault(b => (b.Tag as string) == key);
            SetActiveNav(key, btn);
        }

        // ===== INavigationHost 实现（供 ModuleRegistry.CreatePage 注入导航 / 关闭 / 优化行为） =====
        void INavigationHost.Navigate(string key) => NavigateByKey(key);
        void INavigationHost.Optimize() => BtnOptimize_Click(BtnOptimize, new RoutedEventArgs());
        void INavigationHost.CloseToHome() => SetActiveNav("home", NavHome);
        void INavigationHost.OpenTutorial() => SetActiveNav("tutorial", null);
        void INavigationHost.OpenAgent() => SetActiveNav("agent", null);

        // ===== 多语言 =====

        private void Localize()
        {
            TxtBrand.Text = UiLanguage.L("司南工具箱", "Sinan Toolbox");
            TxtNavFooter.Text = $"v{UpdateManager.LocalVersion} · GPL v2 · " +
                UiLanguage.L("免费开源", "free & open source");
            foreach (var b in _navButtons)
            {
                if (b.Tag is string k && ModuleRegistry.Find(k) is ModuleDefinition m)
                    b.Content = UiLanguage.L(m.TitleZh, m.TitleEn);
            }
            foreach (var (tb, zh, en) in _navHeaders)
                tb.Text = UiLanguage.L(zh, en);

            if (_titles.TryGetValue(_currentKey, out var t)) TxtPageTitle.Text = UiLanguage.L(t.Zh, t.En);
            BtnOptimize.Content = UiLanguage.L("一键优化", "One-Click Optimize");
            TxtSearch.ToolTip = UiLanguage.L("搜索首页功能模块", "Search home features");
            BtnDownload.Content = UiLanguage.L("下载页面", "Download Page");
            BtnUpdateNow.Content = UiLanguage.L("下载并安装", "Download & Install");
            BtnDismiss.Content = UiLanguage.L("忽略", "Dismiss");
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
            // v5.4.0：仅在首次输入时跳回首页（后续击键只做筛选），
            // 修复"每敲一个字符就整页切换"的卡顿与打断感
            if (PageHost.Content is not HomePage hp)
            {
                if (!string.IsNullOrEmpty(kw)) SetActiveNav("home", NavHome);
                return;
            }
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
                ? UiLanguage.L("点击「下载并安装」即可在线升级", "Click Download & Install to upgrade")
                : info.ReleaseNotes;
            UpdateBar.Visibility = Visibility.Visible;
        }

        /// <summary>关闭/托盘逻辑</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // v5.4.0：窗口关闭时停止结果提示自动隐藏计时器（防句柄残留）
            _resultTimer.Stop();
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

        // ===== 一键优化 =====

        private async void BtnOptimize_Click(object sender, RoutedEventArgs e)
        {
            BtnOptimize.IsEnabled = false;
            BtnOptimize.Content = UiLanguage.L("优化中…", "Optimizing…");
            try
            {
                // 清理前按需创建系统还原点（权限不足时优雅降级，不阻塞）
                if (SettingsManager.Current.RestorePointEnabled)
                {
                    try { Cleaner.CreateSystemRestorePoint("司南工具箱 一键优化"); } catch { }
                }

                // 安全闸：大文件（>200MB）必须先经用户手动确认，避免误删重要文件
                const long LargeThreshold = 200L * 1024 * 1024;

                var largeTemp = Cleaner.FindLargeTempFiles(LargeThreshold);
                bool allowLargeTemp = true;
                if (largeTemp.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine(UiLanguage.L(
                        $"临时目录中发现 {largeTemp.Count} 个大文件（单个超过 200 MB），清理后无法恢复：",
                        $"Found {largeTemp.Count} large file(s) (>200 MB) in temp folders. Once cleaned they cannot be recovered:"));
                    foreach (var (p, s) in largeTemp.Take(15))
                        sb.AppendLine("• " + Path.GetFileName(p) + "  (" + FmtSize(s) + ")");
                    if (largeTemp.Count > 15) sb.AppendLine("• …");
                    sb.AppendLine();
                    sb.AppendLine(UiLanguage.L("是否一并清理这些大文件？", "Clean these large files as well?"));
                    var r = System.Windows.MessageBox.Show(sb.ToString(),
                        UiLanguage.L("大文件清理确认", "Large File Cleanup Confirmation"),
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    allowLargeTemp = r == MessageBoxResult.Yes;
                }

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

                long freed = await Task.Run(() =>
                    Cleaner.OneClickOptimize(
                        protectLargeTempBytes: allowLargeTemp ? 0 : LargeThreshold,
                        emptyRecycleBin: emptyRecycle));

                if (SettingsManager.Current.PrivacyCleanEnabled)
                    freed += await Task.Run(() => Cleaner.CleanPrivacyTraces());

                SettingsManager.Current.OptimizeCount++;
                SettingsManager.Current.LastOptimize = DateTime.Now;
                SettingsManager.Current.CleanedBytes += freed;
                SettingsManager.Save();

                var note = (!allowLargeTemp ? UiLanguage.L("（已保留大文件）", " (large files kept)") : "")
                         + (!emptyRecycle ? UiLanguage.L("（已保留回收站）", " (recycle bin kept)") : "");
                ShowOptimizeResult(UiLanguage.L($"已清理并释放 {FmtSize(freed)}", $"Cleaned and freed {FmtSize(freed)}") + note);

                // 首页可见时刷新统计（v5.4.0：force 跳过 60s 缓存，优化后立即显示新数据）
                if (PageHost.Content is HomePage hp) hp.RefreshStats(true);
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "MainWindow.BtnOptimize");
                ShowOptimizeResult(UiLanguage.L("优化出错：", "Optimize error: ") + ex.Message);
            }
            finally
            {
                BtnOptimize.Content = UiLanguage.L("一键优化", "One-Click Optimize");
                BtnOptimize.IsEnabled = true;
            }
        }

        private void ShowOptimizeResult(string text)
        {
            TxtOptResult.Text = text;
            TxtOptResult.Visibility = Visibility.Visible;
            _resultTimer.Stop();
            _resultTimer.Start();
        }

        // 搜索框回车：仅做应用内筛选（不再跳转网页搜索）
        private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_currentKey != "home")
                    SetActiveNav("home", NavHome);
                if (PageHost.Content is HomePage hp)
                    hp.Filter(TxtSearch.Text.Trim().ToLowerInvariant());
                e.Handled = true;
            }
        }

        internal static string FmtSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        // ===== 下载/忽略 =====

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_downloadUrl))
            {
                UpdateManager.OpenDownloadUrl(_downloadUrl);
            }
            else
            {
                // v5.4.0：更新栏未出现时点击给出明确反馈（不再静默无反应）
                System.Windows.MessageBox.Show(
                    UiLanguage.L("暂无可用下载地址。请点击「检查更新」后重试。", "No download link yet. Run 'Check for Updates' first."),
                    UiLanguage.L("提示", "Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 从 GitHub 直接下载最新 tag 的安装包并安装（下载后强制 SHA-256 校验，失败拒绝安装）。
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

        /// <summary>命令面板数据源：全部模块（跳转）+ 一组安全动作（直接执行）。图标取标题首字（无 emoji）。</summary>
        private List<CommandItem> BuildCommandItems()
        {
            var list = new List<CommandItem>();
            foreach (var m in ModuleRegistry.All)
            {
                list.Add(new CommandItem
                {
                    Key = m.Key,
                    Icon = FirstChar(m.TitleZh),
                    Group = UiLanguage.L("模块", "Module"),
                    TitleZh = m.TitleZh, TitleEn = m.TitleEn,
                    Execute = () => NavigateByKey(m.Key),
                });
            }
            var actG = UiLanguage.L("动作", "Action");
            list.Add(new CommandItem
            {
                Key = "act:optimize", Icon = "优", Group = actG,
                TitleZh = "一键优化", TitleEn = "One-Click Optimize",
                SubZh = "清理临时文件并清空回收站", SubEn = "Clean temp & empty recycle bin",
                Execute = () => BtnOptimize_Click(BtnOptimize, new RoutedEventArgs()),
            });
            list.Add(new CommandItem
            {
                Key = "act:companion", Icon = "伴", Group = actG,
                TitleZh = "切换陪伴运行", TitleEn = "Toggle Companion",
                SubZh = "显示 / 隐藏陪伴小窗", SubEn = "Show / hide companion window",
                Execute = () => CompanionManager.Toggle(),
            });
            list.Add(new CommandItem
            {
                Key = "act:followsys", Icon = "主", Group = actG,
                TitleZh = "跟随系统主题", TitleEn = "Follow System Theme",
                SubZh = "自动随系统浅色 / 深色切换", SubEn = "Auto match system light / dark",
                Execute = () => ThemeManager.SetFollowSystem(!ThemeManager.FollowSystem),
            });
            list.Add(new CommandItem
            {
                Key = "act:privacy", Icon = "隐", Group = actG,
                TitleZh = "清理隐私痕迹", TitleEn = "Clean Privacy Traces",
                SubZh = "清理浏览器等隐私痕迹", SubEn = "Clean browser privacy traces",
                Execute = () => Task.Run(() => { try { Cleaner.CleanPrivacyTraces(); } catch { } }),
            });
            return list;
        }

        private static string FirstChar(string s)
            => string.IsNullOrEmpty(s) ? "" : s[..1];
    }
}
