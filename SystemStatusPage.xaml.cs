using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;

namespace WINHELP
{
    /// <summary>
    /// SystemStatusPage.xaml 交互逻辑 — 设备检测与优化（导航 key="system"：硬件识别 + 完整性检测 + 优化建议）
    /// 支持 中/EN 语言切换：切换后重新渲染所有文本，避免中文缺字/乱码。
    /// 由 MainWindow._factories 懒加载；依赖 HardwareInfo / HealthScoreService 与 ThemeManager 玻璃画刷。
    /// </summary>
    public partial class SystemStatusPage : UserControl
    {
        private bool _isChecking = false;
        public Action<string>? OnNavigate;
        private static string L(string zh, string en) => UiLanguage.L(zh, en);

        // 检测结果标记（供优化建议生成使用）
        private double _diskFreeGB = -1;
        private bool _netOk = true, _apiOk = true, _settingsOk = true, _iconOk = true, _cfgOk = true;

        // 缓存最近一次硬件信息（语言切换重渲硬件列表用）
        private List<HardwareInfo.Item>? _lastHwItems;
        // v5.4.0：硬件加载防重入标志（语言切换/手动刷新并发时只跑一次）
        private bool _hwLoading;

        // ===== 进程榜 / 温度 / 诊断 状态 =====
        private List<ProcItem>? _procItems;
        private double? _cpuTempC;
        // 受保护进程（禁止结束）
        private static readonly HashSet<string> _procBlockList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "winlogon", "csrss", "lsass", "services", "svchost",
            "System", "Idle", "dwm", "司南工具箱"
        };

        /// <summary>进程榜条目（内存/CPU 指标在采样后填充）</summary>
        private sealed class ProcItem
        {
            public int Pid { get; set; }
            public string Name { get; set; } = "";
            public long MemoryBytes { get; set; }
            public double? CpuPct { get; set; }
            public string MemoryText => FmtBytes(MemoryBytes);
            public string CpuText => CpuPct.HasValue ? $"{CpuPct.Value:F1}%" : "—";
        }

        /// <summary>字节可读化（与 SystemCleanerPage 的 Fmt 风格一致）</summary>
        private static string FmtBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        // ===== 诊断发现条目 =====
        private sealed class DiagFinding
        {
            public string SevKey { get; set; } = "info"; // normal/info/warn/critical
            public string Text { get; set; } = "";
            public string Suggestion { get; set; } = "";
        }

        // ===== 健康度圆环仪表（动画）=====
        public static readonly DependencyProperty GaugeValueProperty =
            DependencyProperty.Register("GaugeValue", typeof(double), typeof(SystemStatusPage),
                new PropertyMetadata(0.0, (d, _) => ((SystemStatusPage)d).UpdateGauge()));

        public double GaugeValue
        {
            get => (double)GetValue(GaugeValueProperty);
            set => SetValue(GaugeValueProperty, value);
        }

        // 硬件分组顺序与名称
        private static readonly (string Key, string Zh, string En)[] _hwGroups = new[]
        {
            ("cpu", "处理器", "Processor"),
            ("gpu", "显卡", "Graphics"),
            ("mem", "内存", "Memory"),
            ("mb", "主板与固件", "Motherboard & Firmware"),
            ("store", "存储", "Storage"),
            ("sys", "系统与运行时", "OS & Runtime"),
            ("etc", "其他", "Other"),
        };

        // 检测项分组顺序与名称
        private static readonly (string Key, string Zh, string En)[] _checkGroups = new[]
        {
            ("integ", "系统完整性", "Integrity"),
            ("net", "网络与更新", "Network & Update"),
            ("run", "运行与硬件", "Runtime & Hardware"),
        };

        public SystemStatusPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            Loaded += (_, _) =>
            {
                UiLanguage.Changed += OnLanguageChanged;
                _ = LoadTemperatureAsync();
            };
            Unloaded += (_, _) => UiLanguage.Changed -= OnLanguageChanged;

            LocalizeUI();
            _ = LoadHardwareAsync();
        }

        private void OnLanguageChanged()
        {
            Dispatcher.Invoke(() =>
            {
                LocalizeUI();
                // 硬件信息文本按采集时语言生成本地化，语言切换必须重扫以更新文案；
                // 加防重入：若正在加载则跳过本次（避免并发 WMI 枚举）
                if (_hwLoading) return;
                _ = LoadHardwareAsync();
            });
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
            ThemeManager.ApplyButtonTheme(BtnStart, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnRefreshHw, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnProcRefresh, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnKillProc, Color.FromRgb(0xE7, 0x4C, 0x3C),
                hoverColor: Color.FromRgb(0xC0, 0x39, 0x2B));
            ThemeManager.ApplyButtonTheme(BtnTempRefresh, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnDiag, ThemeManager.AccentColor);
        }

        /// <summary>按当前语言刷新所有静态文本</summary>
        private void LocalizeUI()
        {
            TxtTitle.Text = L("设备检测与优化", "Device Check & Optimize");
            TxtSubtitle.Text = L("— 设备信息 · 完整性诊断 · 优化建议", "— Device info · Diagnostics · Tips");
            TxtHwTitle.Text = L("设备信息", "Device Info");
            TxtCheckTitle.Text = L("检测项目", "Checks");
            TxtOptTitle.Text = L("优化建议", "Optimization Tips");
            BtnRefreshHw.Content = L("刷新", "Refresh");
            TxtProcTitle.Text = L("进程榜", "Process List");
            BtnProcRefresh.Content = L("刷新", "Refresh");
            BtnKillProc.Content = L("结束选中进程", "End Selected");
            TxtProcStatus.Text = L("点击「刷新」加载进程", "Click Refresh to load processes");
            TxtTempTitle.Text = L("温度 / 传感器", "Temperature / Sensors");
            BtnTempRefresh.Content = L("刷新", "Refresh");
            TxtTempHint.Text = L("通过系统接口读取，部分设备不支持", "Read via system API; may be unsupported on some devices");
            TxtDiagTitle.Text = L("智能诊断", "Smart Diagnosis");
            BtnDiag.Content = L("开始诊断", "Diagnose");
            if (!_isChecking)
                BtnStart.Content = L("开始检测", "Start check");
        }

        /// <summary>刷新系统信息按钮</summary>
        private void Button_RefreshHw_Click(object sender, RoutedEventArgs e)
            => _ = LoadHardwareAsync();

        // ===== 设备信息（分组渲染）=====

        private async Task LoadHardwareAsync()
        {
            if (_hwLoading) return;          // v5.4.0：防重入
            _hwLoading = true;
            try
            {
                BtnRefreshHw.IsEnabled = false;
                TxtHwStatus.Text = L("正在读取设备信息…", "Reading device info…");
                HardwareList.Children.Clear();
                HardwareList.Children.Add(CreateGroupHeader(L("…", "…")));
                if (HardwareList.Children[0] is TextBlock firstHdr)
                    firstHdr.Text = L("正在枚举硬件…", "Enumerating hardware…");

                var items = await HardwareInfo.CollectAsync();
                _lastHwItems = items;
                RenderHardwareList();
            }
            catch (Exception ex)
            {
                HardwareList.Children.Clear();
                HardwareList.Children.Add(CreateInfoRow("!", L("读取失败", "Read failed"), ex.Message, false));
                TxtHwStatus.Text = L("读取失败", "Read failed");
            }
            finally
            {
                _hwLoading = false;
                BtnRefreshHw.IsEnabled = true;
            }
        }

        /// <summary>从缓存渲染硬件信息列表（普通模式在标签后追加术语解释，专业模式原文）</summary>
        private void RenderHardwareList()
        {
            if (HardwareList == null || _lastHwItems == null) return;
            HardwareList.Children.Clear();
            int gpuCount = 0;
            var grouped = new Dictionary<string, List<HardwareInfo.Item>>();
            foreach (var it in _lastHwItems)
            {
                if (it.IsGpu) gpuCount++;
                var k = HwGroupKey(it.Label);
                if (!grouped.TryGetValue(k, out var lst)) { lst = new List<HardwareInfo.Item>(); grouped[k] = lst; }
                lst.Add(it);
            }

            foreach (var g in _hwGroups)
            {
                if (!grouped.TryGetValue(g.Key, out var lst) || lst.Count == 0) continue;
                HardwareList.Children.Add(CreateGroupHeader(HwGroupName(g.Key)));
                foreach (var it in lst)
                {
                    var icon = it.IsGpu ? "◆" : "▪";
                    HardwareList.Children.Add(CreateInfoRow(icon, it.Label, it.Value, it.IsGpu));
                }
            }

            TxtHwStatus.Text = gpuCount > 0
                ? string.Format(L("共 {0} 项 · 检测到 {1} 块显卡", "{0} items · {1} GPU(s) detected"), _lastHwItems.Count, gpuCount)
                : string.Format(L("共 {0} 项", "{0} items"), _lastHwItems.Count);
        }

        private static string HwGroupKey(string label)
        {
            if (label.Contains("CPU") || label.Contains("处理器")) return "cpu";
            if (label.Contains("GPU") || label.Contains("显卡") || label.Contains("Graphics")) return "gpu";
            if (label.Contains("RAM") || label.Contains("内存") || label.Contains("Memory")) return "mem";
            if (label.Contains("主板") || label.Contains("整机") || label.Contains("BIOS") || label.Contains("Motherboard")) return "mb";
            if (label.Contains("系统盘") || label.Contains("Drive") || label.Contains("Storage")) return "store";
            if (label.Contains("操作系统") || label.Contains(".NET") || label.Contains("Operating")) return "sys";
            return "etc";
        }

        private static string HwGroupName(string key)
        {
            foreach (var g in _hwGroups) if (g.Key == key) return L(g.Zh, g.En);
            return key;
        }

        private static string GroupName(string key)
        {
            foreach (var g in _checkGroups) if (g.Key == key) return L(g.Zh, g.En);
            return key;
        }

        private TextBlock CreateGroupHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Style = (Style)FindResource("GroupHeader")
            };
        }

        /// <summary>创建一行设备信息（图标 + 标签 + 值）</summary>
        private Border CreateInfoRow(string icon, string label, string value, bool isGpu)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(isGpu
                    ? Color.FromRgb(0xFD, 0xF1, 0xF3)
                    : Color.FromRgb(0xF8, 0xF9, 0xFA)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 7),
                BorderThickness = new Thickness(isGpu ? 1 : 0, 0, 0, 0),
                BorderBrush = isGpu ? new SolidColorBrush(Color.FromRgb(0xE2, 0x9B, 0xAE)) : null
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ic = new TextBlock
            {
                Text = icon,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 10, 0)
            };
            Grid.SetColumn(ic, 0);
            grid.Children.Add(ic);

            var lb = new TextBlock
            {
                // 在标签后追加术语通俗解释（如"处理器 (CPU)（中央处理器…）"）
                Text = Glossary.Hint(label),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(isGpu ? Color.FromRgb(0xC0, 0x39, 0x2B) : Color.FromRgb(0x7F, 0x8C, 0x8D)),
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(lb, 1);
            grid.Children.Add(lb);

            var val = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? "—" : value,
                FontSize = 12.5,
                // 显式 CJK 字体回退，避免个别系统字体缺失导致方块（不变更字号/颜色/布局）
                FontFamily = new FontFamily("Microsoft YaHei, SimSun, Segoe UI, PingFang SC"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17
            };
            Grid.SetColumn(val, 2);
            grid.Children.Add(val);

            row.Child = grid;
            return row;
        }

        // ===== 健康度圆环 =====

        private void UpdateGauge()
        {
            // 防御性检查：XAML 可视化树尚未就绪时（如 GaugeValue 在 Loaded 前被赋值），
            // GaugeArc / TxtScore / TxtGrade 可能仍为 null，直接跳过避免 NullReferenceException。
            if (GaugeArc == null || TxtScore == null || TxtGrade == null) return;

            double r = GaugeArc.Width / 2;
            double c = 2 * Math.PI * r;
            double pct = Math.Max(0, Math.Min(100, GaugeValue)) / 100.0;
            GaugeArc.StrokeDashArray = new DoubleCollection { pct * c, c };
            TxtScore.Text = ((int)Math.Round(GaugeValue)).ToString();
            var (col, grade) = ScoreColor(GaugeValue);
            GaugeArc.Stroke = new SolidColorBrush(col);
            TxtScore.Foreground = new SolidColorBrush(col);
            TxtGrade.Text = grade;
        }

        private void AnimateGauge(double score)
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = score,
                Duration = TimeSpan.FromMilliseconds(900),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(GaugeValueProperty, anim);
        }

        private static (Color c, string g) ScoreColor(double s)
        {
            if (s >= 90) return (Color.FromRgb(0x27, 0xAE, 0x60), L("优秀", "Excellent"));
            if (s >= 75) return (Color.FromRgb(0x2E, 0x86, 0xC1), L("良好", "Good"));
            if (s >= 60) return (Color.FromRgb(0xE6, 0x7E, 0x22), L("一般", "Fair"));
            return (Color.FromRgb(0xE7, 0x4C, 0x3C), L("需关注", "Attention"));
        }

        // ===== 检测项定义 =====

        private class CheckItem
        {
            public string Key { get; set; } = "";
            public string Group { get; set; } = "integ";
            public string Icon { get; set; } = "";
            public string Title { get; set; } = "";
            public string Desc { get; set; } = "";
            public Func<Task<CheckResult>> Run { get; set; } = null!;
        }

        private class CheckResult
        {
            public bool Pass { get; set; }
            public string Detail { get; set; } = "";
        }

        private async void Button_Click_Start(object sender, RoutedEventArgs e)
        {
            if (_isChecking) return;
            _isChecking = true;
            try
            {
            BtnStart.IsEnabled = false;
            BtnStart.Content = L("检测中...", "Checking...");
            TxtSummary.Text = L("准备中…", "Preparing…");
            CheckList.Children.Clear();
            OptPanel.Children.Clear();
            TxtOptStatus.Text = "";

            var checks = BuildChecks();
            int passCount = 0;

            // 先按分组渲染所有"检测中"行
            var rowRefs = new List<(CheckItem item, TextBlock icon, TextBlock title)>();
            string? lastGroup = null;
            foreach (var item in checks)
            {
                if (item.Group != lastGroup)
                {
                    CheckList.Children.Add(CreateGroupHeader(GroupName(item.Group)));
                    lastGroup = item.Group;
                }
                var (border, icon, title) = CreateCheckRow(item, "·",
                    item.Title + " — " + L("检测中...", "checking..."), "#7F8C8D");
                CheckList.Children.Add(border);
                rowRefs.Add((item, icon, title));
            }

            // 依次执行并更新
            for (int i = 0; i < checks.Length; i++)
            {
                var (item, icon, title) = rowRefs[i];
                TxtSummary.Text = string.Format(L("正在检测 {0}/{1}...", "Checking {0}/{1}..."), i + 1, checks.Length);

                CheckResult result;
                try { result = await item.Run(); }
                catch (Exception ex) { result = new CheckResult { Pass = false, Detail = ex.Message }; }

                if (result.Pass) passCount++;
                var color = (Color)ColorConverter.ConvertFromString(result.Pass ? "#27AE60" : "#E74C3C");
                icon.Text = result.Pass ? "OK" : "X";
                icon.Foreground = new SolidColorBrush(color);
                title.Text = $"{item.Title} — {result.Detail}";
                title.Foreground = new SolidColorBrush(color);

                await Task.Delay(60);
            }

            // 综合健康分（New A）：综合 CPU / 内存 / 系统盘剩余 / 开机启动项
            var health = HealthScoreService.Compute();
            AnimateGauge(health.Score);

            TxtSummary.Text = string.Format(L("检测完成：{0}/{1} 项通过 · 健康分 {2}", "Done: {0}/{1} passed · Health {2}"), passCount, checks.Length, health.Score);

            BuildSuggestions(passCount, checks.Length, health);
            }
            catch (Exception ex)
            {
                TxtSummary.Text = L("检测出错：", "Check error: ") + ex.Message;
            }
            finally
            {
                _isChecking = false;
                BtnStart.IsEnabled = true;
                BtnStart.Content = L("重新检测", "Re-check");
            }
        }

        private CheckItem[] BuildChecks()
        {
            return new[]
            {
                new CheckItem
                {
                    Key = "CFG", Group = "integ", Icon = "CFG",
                    Title = L("配置目录读写", "Config dir R/W"), Desc = L("检查配置目录是否可读写", "Check config dir read/write"),
                    Run = async () =>
                    {
                        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
                        Directory.CreateDirectory(dir);
                        var testFile = Path.Combine(dir, ".selftest");
                        await File.WriteAllTextAsync(testFile, "ok");
                        var content = await File.ReadAllTextAsync(testFile);
                        File.Delete(testFile);
                        var ok = content == "ok";
                        _cfgOk = ok;
                        return ok
                            ? new CheckResult { Pass = true, Detail = L("配置目录可正常读写", "Config dir R/W OK") }
                            : new CheckResult { Pass = false, Detail = L("读写内容不匹配", "R/W mismatch") };
                    }
                },
                new CheckItem
                {
                    Key = "THM", Group = "integ", Icon = "THM",
                    Title = L("主题配置加载", "Theme load"), Desc = L("检查主题配置是否能正常加载", "Check theme config loads"),
                    Run = async () =>
                    {
                        await Task.Yield();
                        var accent = ThemeManager.AccentColor;
                        return new CheckResult { Pass = true, Detail = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}" };
                    }
                },
                new CheckItem
                {
                    Key = "SET", Group = "integ", Icon = "SET",
                    Title = L("应用设置加载", "Settings load"), Desc = L("检查设置是否正常加载", "Check settings load"),
                    Run = async () =>
                    {
                        await Task.Yield();
                        var s = SettingsManager.Current;
                        var ok = s != null;
                        _settingsOk = ok;
                        var autoStart = s?.AutoStart;
                        var autoUpdate = s?.AutoCheckUpdate;
                        return ok
                            ? new CheckResult { Pass = true, Detail = L($"开机自启：{autoStart} · 自动更新：{autoUpdate}", $"AutoStart:{autoStart} AutoUpdate:{autoUpdate}") }
                            : new CheckResult { Pass = false, Detail = L("设置为空", "Settings empty") };
                    }
                },
                new CheckItem
                {
                    Key = "ICO", Group = "integ", Icon = "ICO",
                    Title = L("图标资源加载", "Icon load"), Desc = L("检查应用图标资源是否可用", "Check icon resource"),
                    Run = async () =>
                    {
                        await Task.Yield();
                        try
                        {
                            var icon = new System.Windows.Media.Imaging.BitmapImage(
                                new Uri("pack://application:,,,/AppIcon.ico"));
                            var ok = icon != null && icon.Width >= 0;
                            _iconOk = ok;
                            return ok
                                ? new CheckResult { Pass = true, Detail = L("图标资源加载正常", "Icon OK") }
                                : new CheckResult { Pass = false, Detail = L("图标加载异常", "Icon load error") };
                        }
                        catch (Exception ex)
                        {
                            _iconOk = false;
                            return new CheckResult { Pass = false, Detail = ex.Message };
                        }
                    }
                },
                new CheckItem
                {
                    Key = "NET", Group = "net", Icon = "NET",
                    Title = L("网络连通性", "Network"), Desc = L("检查网络是否正常", "Check network"),
                    Run = async () =>
                    {
                        try
                        {
                            using var cts = HttpClientProvider.Timeout(5); // 保持原 5s 超时语义
                            var resp = await HttpClientProvider.Shared.GetAsync("https://www.baidu.com", cts.Token);
                            var ok = resp.IsSuccessStatusCode;
                            _netOk = ok;
                            return ok
                                ? new CheckResult { Pass = true, Detail = L("网络连接正常", "Network OK") }
                                : new CheckResult { Pass = false, Detail = $"HTTP {resp.StatusCode}" };
                        }
                        catch
                        {
                            _netOk = false;
                            return new CheckResult { Pass = false, Detail = L("无法连接网络", "No network") };
                        }
                    }
                },
                new CheckItem
                {
                    Key = "API", Group = "net", Icon = "API",
                    Title = L("GitHub API 可达性", "GitHub API"), Desc = L("检查更新服务是否可用", "Check update service"),
                    Run = async () =>
                    {
                        try
                        {
                            using var cts = HttpClientProvider.Timeout(8); // 保持原 8s 超时语义
                            var resp = await HttpClientProvider.Shared.GetAsync(
                                "https://api.github.com/repos/YYRMMAYO/WINhelper/tags", cts.Token);
                            var ok = resp.IsSuccessStatusCode;
                            _apiOk = ok;
                            return ok
                                ? new CheckResult { Pass = true, Detail = L("GitHub API 可达，更新功能正常", "GitHub API reachable") }
                                : new CheckResult { Pass = false, Detail = $"HTTP {resp.StatusCode}" };
                        }
                        catch
                        {
                            _apiOk = false;
                            return new CheckResult { Pass = false, Detail = L("GitHub API 不可达（不影响其他功能）", "GitHub API unreachable (harmless)") };
                        }
                    }
                },
                new CheckItem
                {
                    Key = "DSK", Group = "run", Icon = "DSK",
                    Title = L("磁盘空间", "Disk space"), Desc = L("检查系统盘剩余空间", "Check system drive free space"),
                    Run = async () =>
                    {
                        await Task.Yield();
                        try
                        {
                            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                            var freeGB = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                            _diskFreeGB = freeGB;
                            return freeGB > 1
                                ? new CheckResult { Pass = true, Detail = string.Format(L("系统盘剩余 {0:F1} GB", "Free {0:F1} GB"), freeGB) }
                                : new CheckResult { Pass = false, Detail = string.Format(L("系统盘空间不足 ({0:F1} GB)", "Low disk ({0:F1} GB)"), freeGB) };
                        }
                        catch (Exception ex)
                        {
                            _diskFreeGB = -1;
                            return new CheckResult { Pass = false, Detail = ex.Message };
                        }
                    }
                },
                new CheckItem
                {
                    Key = "ENV", Group = "run", Icon = "ENV",
                    Title = L("系统环境", "System env"), Desc = L("检查操作系统和运行时环境", "Check OS & runtime"),
                    Run = async () =>
                    {
                        await Task.Yield();
                        var os = RuntimeInformation.OSDescription;
                        return new CheckResult { Pass = true, Detail = os.Length > 60 ? os[..60] + "..." : os };
                    }
                },
                new CheckItem
                {
                    Key = "CLP", Group = "run", Icon = "CLP",
                    Title = L("剪贴板访问", "Clipboard"), Desc = L("检查剪贴板是否可访问", "Check clipboard"),
                    Run = async () =>
                    {
                        await Task.Yield();
                        try
                        {
                            if (Clipboard.ContainsText())
                            {
                                var text = Clipboard.GetText();
                                return new CheckResult { Pass = true, Detail = string.Format(L("剪贴板可访问（内容长度 {0}）", "Clipboard OK (len {0})"), text.Length) };
                            }
                            return new CheckResult { Pass = true, Detail = L("剪贴板可访问（当前为空）", "Clipboard OK (empty)") };
                        }
                        catch (Exception ex)
                        {
                            return new CheckResult { Pass = false, Detail = ex.Message };
                        }
                    }
                },
                new CheckItem
                {
                    Key = "PRC", Group = "run", Icon = "PRC",
                    Title = L("进程启动能力", "Process launch"), Desc = L("检查能否启动外部进程", "Check external process launch"),
                    Run = async () =>
                    {
                        await Task.Yield();
                        try
                        {
                            var psi = new ProcessStartInfo("cmd.exe", "/c echo ok")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true
                            };
                            using var p = Process.Start(psi);
                            if (p != null)
                            {
                                // v5.4.0：异步等待 + 校验输出，超时/无输出按失败处理（修复"超时仍报正常"误报）
                                var wait = p.WaitForExitAsync();
                                var done = await Task.WhenAny(wait, Task.Delay(3000));
                                if (done != wait)
                                {
                                    try { p.Kill(); } catch { }
                                    return new CheckResult { Pass = false, Detail = L("进程启动超时", "Process launch timed out") };
                                }
                                string outp;
                                try { outp = p.StandardOutput.ReadToEnd(); } catch { outp = ""; }
                                if (!outp.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase))
                                    return new CheckResult { Pass = false, Detail = L("进程启动输出异常", "Bad process output") };
                                return new CheckResult { Pass = true, Detail = L("进程启动功能正常", "Process launch OK") };
                            }
                            return new CheckResult { Pass = false, Detail = L("进程启动返回 null", "Process null") };
                        }
                        catch (Exception ex)
                        {
                            return new CheckResult { Pass = false, Detail = ex.Message };
                        }
                    }
                },
            };
        }

        /// <summary>创建检测行 UI（返回行容器与可更新的状态/标题文本块）</summary>
        private (Border border, TextBlock statusIcon, TextBlock titleText) CreateCheckRow(CheckItem item, string statusIcon, string statusText, string colorHex)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new TextBlock
            {
                Text = item.Icon,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var status = new TextBlock
            {
                Text = statusIcon,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                Text = statusText,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color)
            };
            textPanel.Children.Add(title);

            var desc = new TextBlock
            {
                Text = item.Desc,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            textPanel.Children.Add(desc);

            Grid.SetColumn(textPanel, 2);
            grid.Children.Add(textPanel);

            border.Child = grid;
            return (border, status, title);
        }

        // ===== 优化建议 =====

        private sealed class Suggestion
        {
            public string Icon = "";
            public string Title = "";
            public string Desc = "";
            public string? ActionKey;
            public string ActionLabel = "";
        }

        private void BuildSuggestions(int passCount, int total, HealthScoreService.HealthResult health)
        {
            OptPanel.Children.Clear();
            var list = new List<Suggestion>();
            bool diskSuggested = false;

            // 综合健康分概览（New A）：分数 + 一句话结论
            list.Add(new Suggestion
            {
                Icon = "",
                Title = L("综合健康分 ", "Health score ") + health.Score,
                Desc = health.Summary,
                ActionKey = null,
                ActionLabel = ""
            });
            // 健康分引擎给出的可执行建议
            foreach (var sug in health.Suggestions)
            {
                list.Add(new Suggestion
                {
                    Icon = "",
                    Title = sug,
                    Desc = "",
                    ActionKey = null,
                    ActionLabel = ""
                });
            }

            if (_diskFreeGB >= 0 && _diskFreeGB < 10)
            {
                list.Add(new Suggestion
                {
                    Icon = "",
                    Title = L("系统盘空间紧张", "Low system disk space"),
                    Desc = string.Format(L("系统盘仅剩 {0:F1} GB，建议清理临时文件与回收站释放空间。", "Only {0:F1} GB left on system drive. Clean temp & recycle bin."), _diskFreeGB),
                    ActionKey = "clean",
                    ActionLabel = L("前往清理", "Go clean")
                });
                diskSuggested = true;
            }
            if (!_netOk)
            {
                list.Add(new Suggestion
                {
                    Icon = "",
                    Title = L("网络连接异常", "Network issue"),
                    Desc = L("未能连接到网络，部分在线功能（更新、教程）将不可用。", "No network. Online features (updates, tutorials) won't work."),
                    ActionKey = "net",
                    ActionLabel = L("网络诊断", "Diagnose")
                });
            }
            if (!_settingsOk)
            {
                list.Add(new Suggestion
                {
                    Icon = "",
                    Title = L("设置加载异常", "Settings load issue"),
                    Desc = L("应用设置未能正常读取，可在设置页重置或检查配置目录权限。", "Settings failed to load. Reset in Settings or check config permissions."),
                    ActionKey = "settings",
                    ActionLabel = L("打开设置", "Open settings")
                });
            }
            if (!_iconOk)
            {
                list.Add(new Suggestion
                {
                    Icon = "",
                    Title = L("图标资源缺失", "Icon resource missing"),
                    Desc = L("应用图标资源未能加载，可能影响界面显示，建议重新安装或修复。", "App icon resource missing; UI may look off. Reinstall/repair suggested."),
                    ActionKey = null,
                    ActionLabel = ""
                });
            }
            if (!diskSuggested && _diskFreeGB >= 0)
            {
                list.Add(new Suggestion
                {
                    Icon = "",
                    Title = L("定期清理更流畅", "Clean regularly"),
                    Desc = L("保持系统盘整洁可提升运行速度，建议偶尔运行一次系统清理。", "Keep the system drive tidy for better performance."),
                    ActionKey = "clean",
                    ActionLabel = L("前往清理", "Go clean")
                });
            }

            if (list.Count == 0)
            {
                list.Add(new Suggestion
                {
                    Icon = "",
                    Title = L("系统状态良好", "System is healthy"),
                    Desc = L("各项检测均通过，暂无需要优化的项，保持即可。", "All checks passed. Nothing needs fixing right now."),
                    ActionKey = null,
                    ActionLabel = ""
                });
            }

            TxtOptStatus.Text = string.Format(L("共 {0} 条建议", "{0} suggestion(s)"), list.Count);
            foreach (var s in list) OptPanel.Children.Add(CreateSuggestionRow(s));
        }

        private Border CreateSuggestionRow(Suggestion s)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 0, 9)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (s.ActionKey != null)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = s.Icon, FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = s.Title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
            });
            sp.Children.Add(new TextBlock
            {
                Text = s.Desc, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
                Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            if (s.ActionKey != null)
            {
                var btn = new Button
                {
                    Content = s.ActionLabel, Height = 32,
                    Padding = new Thickness(14, 0, 14, 0),
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                var key = s.ActionKey;
                btn.Click += (_, _) => OnNavigate?.Invoke(key);
                ThemeManager.ApplyButtonTheme(btn, ThemeManager.AccentColor);
                Grid.SetColumn(btn, 2);
                grid.Children.Add(btn);
            }

            row.Child = grid;
            return row;
        }

        // ===== N3 进程榜 =====

        private async void BtnProcRefresh_Click(object sender, RoutedEventArgs e)
            => await LoadProcessesAsync();

        private async Task LoadProcessesAsync()
        {
            try
            {
                BtnProcRefresh.IsEnabled = false;
                TxtProcStatus.Text = L("正在枚举进程…", "Enumerating processes…");
                ProcList.ItemsSource = null;

                var procs = Process.GetProcesses();
                var snap = new List<(Process p, string name, int pid, long ws, TimeSpan tt, DateTime t0)>();
                foreach (var p in procs)
                {
                    try
                    {
                        snap.Add((p, p.ProcessName, p.Id, p.WorkingSet64, p.TotalProcessorTime, DateTime.UtcNow));
                    }
                    catch
                    {
                        try { p.Dispose(); } catch { }
                    }
                }

                // 两次采样 TotalProcessorTime 估算 CPU 占用（按需刷新，非高频定时器）
                await Task.Delay(600);

                var list = new List<ProcItem>();
                foreach (var s in snap)
                {
                    double? cpu = null;
                    try
                    {
                        var tt2 = s.p.TotalProcessorTime;
                        var dtMs = (DateTime.UtcNow - s.t0).TotalMilliseconds;
                        if (dtMs > 0)
                            cpu = Math.Min(100.0, tt2.Subtract(s.tt).TotalMilliseconds / dtMs / Environment.ProcessorCount);
                    }
                    catch { }
                    try { s.p.Dispose(); } catch { }
                    if (string.IsNullOrEmpty(s.name)) continue;
                    list.Add(new ProcItem { Pid = s.pid, Name = s.name, MemoryBytes = s.ws, CpuPct = cpu });
                }

                list.Sort((a, b) => b.MemoryBytes.CompareTo(a.MemoryBytes));
                _procItems = list.Take(40).ToList();
                ProcList.ItemsSource = _procItems;
                TxtProcStatus.Text = string.Format(
                    L("共 {0} 个进程，显示内存占用前 40", "{0} processes, top 40 by memory"), procs.Length);
            }
            catch (Exception ex)
            {
                TxtProcStatus.Text = L("读取进程失败：", "Failed to read processes: ") + ex.Message;
            }
            finally
            {
                BtnProcRefresh.IsEnabled = true;
            }
        }

        private async void BtnKillProc_Click(object sender, RoutedEventArgs e)
        {
            if (ProcList.SelectedItem is not ProcItem item)
            {
                MessageBox.Show(L("请先选择要结束的进程。", "Please select a process first."),
                    L("提示", "Tip"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var self = Process.GetCurrentProcess().ProcessName;
            if (_procBlockList.Contains(item.Name) ||
                string.Equals(item.Name, self, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    L($"进程 “{item.Name}” 受系统保护，无法结束。", $"Process \"{item.Name}\" is protected and cannot be ended."),
                    L("操作被拒绝", "Blocked"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                L($"确定结束进程 “{item.Name}”？", $"End process \"{item.Name}\"?"),
                L("确认结束进程", "Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using var p = Process.GetProcessById(item.Pid);
                p.Kill();
                await LoadProcessesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    L($"无法结束进程：{ex.Message}", $"Cannot end process: {ex.Message}"),
                    L("错误", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== N14 温度 / 传感器 =====

        private async void BtnTempRefresh_Click(object sender, RoutedEventArgs e)
            => await LoadTemperatureAsync();

        private async Task LoadTemperatureAsync()
        {
            await Task.Run(() =>
            {
                double? temp = ReadCpuTemperature();
                _cpuTempC = temp;
                Dispatcher.Invoke(() =>
                {
                    if (temp.HasValue)
                        TxtTemp.Text = $"{temp.Value:F1} °C";
                    else
                        TxtTemp.Text = L("传感器不可用 / 不支持", "Sensor unavailable / unsupported");
                });
            });
        }

        /// <summary>通过 WMI 读取 CPU 温度；多数设备无数据，返回 null 表示不支持。绝不伪造数值。</summary>
        private static double? ReadCpuTemperature()
        {
            // 1) MSAcpi_ThermalZoneTemperature：CurrentTemperature 单位 = 十分之一开尔文
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var raw = obj["CurrentTemperature"];
                    if (raw != null)
                    {
                        double tenthsK = Convert.ToDouble(raw);
                        return tenthsK / 10.0 - 273.15;
                    }
                }
            }
            catch { }

            // 2) Win32_TemperatureProbe（若平台提供）
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Temperature FROM Win32_TemperatureProbe");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var raw = obj["Temperature"];
                    if (raw != null)
                    {
                        double v = Convert.ToDouble(raw);
                        if (v > 0) return v / 10.0 - 273.15;
                    }
                }
            }
            catch { }

            return null;
        }

        // ===== N4 智能诊断（规则启发式，纯本地）=====

        private async void BtnDiag_Click(object sender, RoutedEventArgs e)
            => await RunDiagnosisAsync();

        private async Task RunDiagnosisAsync()
        {
            BtnDiag.IsEnabled = false;
            DiagPanel.Children.Clear();
            DiagPanel.Children.Add(CreateDiagRow(
                new DiagFinding { SevKey = "info", Text = L("正在分析…", "Analyzing…") }));
            try
            {
                if (_procItems == null) await LoadProcessesAsync();

                var findings = new List<DiagFinding>();

                // 1) 内存占用
                double? memPct = ReadMemoryUsedPct();
                if (memPct.HasValue)
                {
                    if (memPct.Value > 95)
                        findings.Add(new DiagFinding { SevKey = "critical",
                            Text = L($"内存占用严重（{memPct.Value:F0}%），系统可能卡顿", $"Memory critically high ({memPct.Value:F0}%); system may stutter"),
                            Suggestion = L("建议关闭大型程序或重启以释放内存", "Close heavy apps or restart to free memory") });
                    else if (memPct.Value > 85)
                        findings.Add(new DiagFinding { SevKey = "warn",
                            Text = L($"内存占用偏高（{memPct.Value:F0}%）", $"Memory usage high ({memPct.Value:F0}%)"),
                            Suggestion = L("建议关闭不常用的后台程序", "Close unused background programs") });
                    else if (memPct.Value > 70)
                        findings.Add(new DiagFinding { SevKey = "info",
                            Text = L($"内存占用中等（{memPct.Value:F0}%）", $"Memory usage moderate ({memPct.Value:F0}%)"),
                            Suggestion = L("当前占用正常，可继续观察", "Normal for now; keep an eye on it") });
                    else
                        findings.Add(new DiagFinding { SevKey = "normal",
                            Text = L($"内存占用正常（{memPct.Value:F0}%）", $"Memory usage normal ({memPct.Value:F0}%)") });
                }
                else
                    findings.Add(new DiagFinding { SevKey = "info",
                        Text = L("内存数据不可用", "Memory data unavailable") });

                // 2) CPU 温度
                if (_cpuTempC.HasValue)
                {
                    if (_cpuTempC.Value > 90)
                        findings.Add(new DiagFinding { SevKey = "critical",
                            Text = L($"CPU 温度过高（{_cpuTempC.Value:F0}°C）", $"CPU too hot ({_cpuTempC.Value:F0}°C)"),
                            Suggestion = L("检查散热/风扇，避免高负载", "Check cooling/fan; avoid heavy load") });
                    else if (_cpuTempC.Value > 80)
                        findings.Add(new DiagFinding { SevKey = "warn",
                            Text = L($"CPU 温度偏高（{_cpuTempC.Value:F0}°C）", $"CPU warm ({_cpuTempC.Value:F0}°C)"),
                            Suggestion = L("注意通风，减少持续高负载", "Improve ventilation; reduce sustained load") });
                    else
                        findings.Add(new DiagFinding { SevKey = "normal",
                            Text = L($"CPU 温度正常（{_cpuTempC.Value:F0}°C）", $"CPU temp normal ({_cpuTempC.Value:F0}°C)") });
                }
                else
                    findings.Add(new DiagFinding { SevKey = "info",
                        Text = L("温度数据不可用（设备不支持）", "Temperature unavailable (unsupported)") });

                // 3) 进程：内存 / CPU 大户
                if (_procItems != null)
                {
                    var topMem = _procItems.OrderByDescending(x => x.MemoryBytes).FirstOrDefault();
                    if (topMem != null && topMem.MemoryBytes > 2L * 1024 * 1024 * 1024)
                        findings.Add(new DiagFinding { SevKey = "info",
                            Text = L($"“{topMem.Name}” 进程占用 {FmtBytes(topMem.MemoryBytes)} 内存",
                                     $"\"{topMem.Name}\" uses {FmtBytes(topMem.MemoryBytes)} RAM"),
                            Suggestion = L("若无需使用可在上方结束该进程", "End it above if not needed") });

                    var topCpu = _procItems.Where(x => x.CpuPct.HasValue)
                        .OrderByDescending(x => x.CpuPct).FirstOrDefault();
                    if (topCpu != null && topCpu.CpuPct is double cpuVal && cpuVal > 30)
                        findings.Add(new DiagFinding { SevKey = "info",
                            Text = L($"“{topCpu.Name}” 进程 CPU 占用 {cpuVal:F0}%",
                                     $"\"{topCpu.Name}\" uses {cpuVal:F0}% CPU"),
                            Suggestion = L("高 CPU 占用可能拖慢系统", "High CPU may slow the system") });
                }

                // 4) 系统盘空间
                double? freeGB = null;
                try
                {
                    var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                    var drive = new DriveInfo(root);
                    freeGB = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                    if (freeGB < 10)
                        findings.Add(new DiagFinding { SevKey = "warn",
                            Text = L($"系统盘空间不足（仅剩 {freeGB:F1} GB）", $"Low system disk space ({freeGB:F1} GB left)"),
                            Suggestion = L("建议运行系统清理释放空间", "Run system cleanup to free space") });
                    else
                        findings.Add(new DiagFinding { SevKey = "normal",
                            Text = L($"系统盘空间充足（剩余 {freeGB:F1} GB）", $"System disk healthy ({freeGB:F1} GB free)") });
                }
                catch { }

                DiagPanel.Children.Clear();
                if (findings.Count == 0)
                    findings.Add(new DiagFinding { SevKey = "normal", Text = L("未发现明显问题", "No obvious issues") });
                foreach (var f in findings) DiagPanel.Children.Add(CreateDiagRow(f));

                // N4 增强：已配置 AI 密钥时，尝试获取人话解读（失败/超时静默降级，不阻塞规则结论）
                if (!string.IsNullOrWhiteSpace(AgentSettingsManager.Current?.ApiKey))
                {
                    try
                    {
                        var aiText = await AiClient.AskAsync(
                            BuildDiagnosisPrompt(memPct, _cpuTempC, _procItems, freeGB),
                            "你是电脑优化助手，用简体中文给出通俗、可操作的解读，不超过180字。只说结论与建议，不寒暄。");
                        if (!string.IsNullOrWhiteSpace(aiText))
                        {
                            DiagPanel.Children.Insert(0, CreateDiagRow(new DiagFinding
                            {
                                SevKey = "info",
                                Text = "🤖 AI 解读：" + aiText.Replace("\r", " ").Replace("\n", " ").Trim()
                            }));
                        }
                    }
                    catch { /* 降级：保留规则结论 */ }
                }
            }
            catch (Exception ex)
            {
                DiagPanel.Children.Clear();
                DiagPanel.Children.Add(CreateDiagRow(new DiagFinding
                {
                    SevKey = "critical",
                    Text = L("诊断出错：", "Diagnosis error: ") + ex.Message
                }));
            }
            finally
            {
                BtnDiag.IsEnabled = true;
            }
        }

        /// <summary>N4 增强：根据当前检测数据构造给 AI 的语境提示。</summary>
        private static string BuildDiagnosisPrompt(double? memPct, double? cpuTempC, List<ProcItem>? procs, double? freeGB)
        {
            var mem = memPct.HasValue ? memPct.Value.ToString("F0") + "%" : "未知";
            var temp = cpuTempC.HasValue ? cpuTempC.Value.ToString("F0") + "°C" : "不支持/未知";
            var disk = freeGB.HasValue ? freeGB.Value.ToString("F1") + " GB" : "未知";
            var proc = "";
            if (procs != null)
            {
                var top = procs.OrderByDescending(x => x.MemoryBytes).FirstOrDefault();
                if (top != null) proc = $"；内存占用最高进程：{top.Name}（{FmtBytes(top.MemoryBytes)}）";
            }
            return $"请根据以下电脑状态，用简体中文给出通俗、可操作的解读（不超过180字），重点说明是否需要优化以及该怎么做：\n- 内存占用：{mem}\n- CPU 温度：{temp}\n- 系统盘剩余：{disk}{proc}";
        }

        /// <summary>读取物理内存使用率（%）。失败返回 null。</summary>
        private static double? ReadMemoryUsedPct()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var total = Convert.ToDouble(obj["TotalVisibleMemorySize"]); // KB
                    var free = Convert.ToDouble(obj["FreePhysicalMemory"]);       // KB
                    if (total > 0) return (total - free) / total * 100.0;
                }
            }
            catch { }
            return null;
        }

        // ===== 诊断卡片渲染 =====

        private static (Color c, string label) DiagSeverity(string key) => key switch
        {
            "normal" => (Color.FromRgb(0x27, 0xAE, 0x60), L("正常", "Normal")),
            "info" => (Color.FromRgb(0x2E, 0x86, 0xC1), L("提示", "Info")),
            "warn" => (Color.FromRgb(0xE6, 0x7E, 0x22), L("警告", "Warning")),
            "critical" => (Color.FromRgb(0xE7, 0x4C, 0x3C), L("严重", "Critical")),
            _ => (Color.FromRgb(0x95, 0xA5, 0xA6), key)
        };

        private Border CreateDiagRow(DiagFinding f)
        {
            var (color, label) = DiagSeverity(f.SevKey);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0)
            };
            badge.Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = f.Text,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrEmpty(f.Suggestion))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = f.Suggestion,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            border.Child = grid;
            return border;
        }
    }
}
