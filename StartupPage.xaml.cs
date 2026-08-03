using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>启动项管理页（导航 key="startup"）：管理开机自启程序。由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。</summary>
public partial class StartupPage : UserControl
{
    private enum Impact { High, Medium, Low }

    /// <summary>数据模型：Entry。</summary>
    private sealed class Entry
    {
        public string Name = "";
        public string Command = "";
        public string Source = "";
        public RegistryKey? Base;
        public string RunPath = "";
        public string FilePath = "";
        public bool Enabled;
        public bool IsStartupFolder;
        // N6 影响评估（只读，不影响禁用逻辑）
        public Impact ImpactLevel = Impact.Low;
        public double BootSeconds;
    }

    /// <summary>数据模型：BootInfo。</summary>
    private sealed class BootInfo
    {
        public DateTime? LastBoot;
        public bool HasPreciseDuration;
        public string PreciseDuration = "";
    }

    private readonly List<Entry> _entries = new();
    private BootInfo? _bootInfo;

    public StartupPage()
    {
        InitializeComponent();
        ApplyTheme();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        UiLanguage.Changed += () => Dispatcher.Invoke(Render);
        LoadAll();
        Render();
    }

    private void LoadAll()
    {
        _entries.Clear();
        LoadEntries();
        foreach (var e in _entries) Assess(e);
        LoadBootInfo();
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnRefresh, ThemeManager.AccentColor);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadAll();
        Render();
        TxtStatus.Text = UiLanguage.L($"已加载 {_entries.Count} 个启动项", $"Loaded {_entries.Count} startup items");
    }

    private void LoadEntries()
    {
        const string run = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        TryLoadRegistry(Registry.CurrentUser, run, "当前用户");
        TryLoadRegistry(Registry.LocalMachine, run, "所有用户（需管理员权限）");

        // 启动文件夹
        var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        TryLoadStartupFolder(startupDir, "当前用户启动文件夹");

        var commonStartupDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        TryLoadStartupFolder(commonStartupDir, "公用启动文件夹");
    }

    private void TryLoadRegistry(RegistryKey baseKey, string runPath, string source)
    {
        try
        {
            using var key = baseKey.OpenSubKey(runPath, false);
            if (key != null)
                foreach (var name in key.GetValueNames())
                    _entries.Add(new Entry
                    {
                        Name = name,
                        Command = key.GetValue(name)?.ToString() ?? "",
                        Source = source,
                        Base = baseKey,
                        RunPath = runPath,
                        Enabled = true
                    });

            using var dis = baseKey.OpenSubKey(runPath + "\\WINHELP_Disabled", false);
            if (dis != null)
                foreach (var name in dis.GetValueNames())
                    _entries.Add(new Entry
                    {
                        Name = name,
                        Command = dis.GetValue(name)?.ToString() ?? "",
                        Source = source,
                        Base = baseKey,
                        RunPath = runPath,
                        Enabled = false
                    });
        }
            catch (Exception ex)
            {
                TxtStatus.Text = UiLanguage.L("读取启动项出错（部分项需管理员权限）：", "Error reading startup items (some need admin): ") + ex.Message;
            }
    }

    private void TryLoadStartupFolder(string dir, string source)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.lnk"))
            {
                _entries.Add(new Entry
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Source = source,
                    FilePath = file,
                    IsStartupFolder = true,
                    Enabled = true
                });
            }
        }
        catch { }
    }

    // ===== N6 影响评估（基于可执行文件大小 + 已知重型关键词，纯评估，不改变禁用行为）=====
    private static readonly string[] HighKeywords =
    {
        "adobe", "spotify", "discord", "steam", "onedrive", "cloud", "antivirus",
        "avast", "avg", "kaspersky", "norton", "mcafee", "bonjour", "update",
        "agent", "helper", "teams", "slack", "zoom", "dropbox", "wechat", "qq", "baidu"
    };

    private static readonly string[] MediumKeywords =
    {
        "skype", "itunes", "apple", "google", "nvidia", "amd", "realtek",
        "epic", "origin", "uplay", "obs", "chrome", "edge", "firefox", "java"
    };

    private void Assess(Entry e)
    {
        var path = ExtractExePath(e.Command);
        long size = 0;
        if (path != null)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists) size = fi.Length;
            }
            catch { }
        }

        var lower = (e.Name + " " + e.Command).ToLowerInvariant();
        bool highKw = HighKeywords.Any(lower.Contains);
        bool medKw = MediumKeywords.Any(lower.Contains);

        const long huge = 100L * 1024 * 1024;   // >100MB → 高
        const long big = 20L * 1024 * 1024;      // >20MB  → 中

        if (highKw || size > huge)
        {
            e.ImpactLevel = Impact.High;
            e.BootSeconds = 3.0;
        }
        else if (medKw || size > big)
        {
            e.ImpactLevel = Impact.Medium;
            e.BootSeconds = 1.5;
        }
        else
        {
            e.ImpactLevel = Impact.Low;
            e.BootSeconds = 0.3;
        }
    }

    private static string? ExtractExePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var c = command.Trim();
        string token;
        if (c.StartsWith("\""))
        {
            int end = c.IndexOf('"', 1);
            token = end > 1 ? c.Substring(1, end - 1) : c.Substring(1);
        }
        else
        {
            int sp = c.IndexOf(' ');
            token = sp > 0 ? c.Substring(0, sp) : c;
        }
        if (string.IsNullOrWhiteSpace(token)) return null;
        try { token = Environment.ExpandEnvironmentVariables(token); } catch { }
        return token;
    }

    // ===== N6 开机耗时（WMI 读取，仅展示）=====
    private void LoadBootInfo()
    {
        _bootInfo = new BootInfo();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                var raw = mo["LastBootUpTime"]?.ToString();
                if (!string.IsNullOrEmpty(raw))
                {
                    try { _bootInfo.LastBoot = ManagementDateTimeConverter.ToDateTime(raw); }
                    catch { if (DateTime.TryParse(raw, out var dt)) _bootInfo.LastBoot = dt; }
                }
                break;
            }
        }
        catch { /* WMI 读取失败则留空，Render 中给出友好提示 */ }

        // 可选：尝试从性能日志读取精确开机耗时；
        // 该 ETW 日志通常不可经 Win32_NTLogEvent 查询，故默认标记为"需性能日志(可选)"。
        _bootInfo.HasPreciseDuration = false;
        try
        {
            using var s2 = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NTLogEvent WHERE LogFile='Microsoft-Windows-Diagnostics-Performance/Operational' AND EventCode=100");
            foreach (ManagementObject mo in s2.Get())
            {
                var msg = mo["Message"]?.ToString() ?? "";
                if (msg.IndexOf("启动", StringComparison.Ordinal) >= 0)
                {
                    _bootInfo.HasPreciseDuration = true;
                    _bootInfo.PreciseDuration = UiLanguage.L("（详见性能日志）", "(see performance log)");
                }
                break;
            }
        }
        catch { /* 无权限或未开启：保持可选标记 */ }
    }

    private void Toggle(Entry e)
    {
        try
        {
            if (e.IsStartupFolder)
            {
                // 启动文件夹：重命名或移动来实现禁用/启用
                if (e.Enabled)
                {
                    var disabledPath = e.FilePath + ".disabled";
                    if (File.Exists(e.FilePath) && !File.Exists(disabledPath))
                    {
                        File.Move(e.FilePath, disabledPath);
                        e.FilePath = disabledPath;
                        e.Command = disabledPath;
                        e.Enabled = false;
                        TxtStatus.Text = UiLanguage.L($"已禁用：{e.Name}", $"Disabled: {e.Name}");
                    }
                }
                else
                {
                    var enabledPath = e.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? e.FilePath[..^9]
                        : e.FilePath;
                    if (File.Exists(e.FilePath) && !File.Exists(enabledPath))
                    {
                        File.Move(e.FilePath, enabledPath);
                        e.FilePath = enabledPath;
                        e.Command = enabledPath;
                        e.Enabled = true;
                        TxtStatus.Text = UiLanguage.L($"已启用：{e.Name}", $"Enabled: {e.Name}");
                    }
                }
            }
            else
            {
                if (e.Enabled)
                {
                    using var run = e.Base?.OpenSubKey(e.RunPath, true);
                    if (run == null) return;
                    var val = run.GetValue(e.Name);
                    if (val == null) return;
                    using var dis = e.Base?.CreateSubKey(e.RunPath + "\\WINHELP_Disabled");
                    dis?.SetValue(e.Name, val);
                    run.DeleteValue(e.Name, false);
                    e.Enabled = false;
                    TxtStatus.Text = UiLanguage.L($"已禁用：{e.Name}", $"Disabled: {e.Name}");
                }
                else
                {
                    using var dis = e.Base?.OpenSubKey(e.RunPath + "\\WINHELP_Disabled", true);
                    if (dis == null) return;
                    var val = dis.GetValue(e.Name);
                    if (val == null) return;
                    using var run = e.Base?.CreateSubKey(e.RunPath);
                    run?.SetValue(e.Name, val);
                    dis.DeleteValue(e.Name, false);
                    e.Enabled = true;
                    TxtStatus.Text = UiLanguage.L($"已启用：{e.Name}", $"Enabled: {e.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("修改失败（HKLM 或系统项需要管理员权限）：", "Failed (HKLM or system items need admin): ") + ex.Message;
        }
        Render();
    }

    private void Render()
    {
        TxtTitle.Text = UiLanguage.L("启动项管理", "Startup Manager");
        TxtSubtitle.Text = UiLanguage.L("禁用不必要的开机自启项，加快开机速度",
            "Disable unneeded startup items to speed up boot");
        BtnRefresh.Content = UiLanguage.L("刷新", "Refresh");

        ListPanel.Children.Clear();

        // 开机信息卡片（始终显示，只读）
        var bootCard = new Border
        {
            Background = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDF0F3")),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var bootSp = new StackPanel();
        bootSp.Children.Add(new TextBlock
        {
            Text = "⏱️ " + UiLanguage.L("本次开机信息", "Boot Information"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
        });
        string bootLine;
        if (_bootInfo?.LastBoot != null)
        {
            var up = DateTime.Now - _bootInfo.LastBoot.Value;
            var upStr = $"{up.Days}{UiLanguage.L("天", "d")} {up.Hours}{UiLanguage.L("时", "h")} {up.Minutes}{UiLanguage.L("分", "m")}";
            bootLine = UiLanguage.L($"本次开机于 {_bootInfo.LastBoot.Value:g}，已运行 {upStr}",
                                    $"Booted at {_bootInfo.LastBoot.Value:g}, uptime {upStr}");
        }
        else
        {
            bootLine = UiLanguage.L("无法读取开机时间（需 WMI 权限）", "Unable to read boot time (WMI required)");
        }
        bootSp.Children.Add(new TextBlock
        {
            Text = bootLine,
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        var durText = _bootInfo is { HasPreciseDuration: true }
            ? _bootInfo.PreciseDuration
            : UiLanguage.L("需性能日志(可选)", "Requires performance log (optional)");
        bootSp.Children.Add(new TextBlock
        {
            Text = UiLanguage.L("精确开机耗时：", "Precise boot duration: ") + durText,
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6")),
            Margin = new Thickness(0, 2, 0, 0)
        });
        bootCard.Child = bootSp;
        ListPanel.Children.Add(bootCard);

        if (_entries.Count == 0)
        {
            TxtStatus.Text = UiLanguage.L("未发现启动项", "No startup items found");
            return;
        }

        // 分组：已启用在前
        var ordered = _entries.OrderByDescending(x => x.Enabled).ToList();
        int enabledCount = _entries.Count(x => x.Enabled);
        double totalSec = _entries.Where(x => x.Enabled).Sum(x => x.BootSeconds);

        foreach (var e in ordered)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDF0F3")),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = e.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
            });

            // 影响评估徽章 + 开机耗时估算（颜色：高=橙红 / 中=黄 / 低=绿）
            var badgeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var (levelText, levelColor) = e.ImpactLevel switch
            {
                Impact.High => (UiLanguage.L("高", "High"), (Color)ColorConverter.ConvertFromString("#E74C3C")),
                Impact.Medium => (UiLanguage.L("中", "Med"), (Color)ColorConverter.ConvertFromString("#F1C40F")),
                _ => (UiLanguage.L("低", "Low"), (Color)ColorConverter.ConvertFromString("#27AE60"))
            };
            var badge = new Border
            {
                Background = new SolidColorBrush(levelColor),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = levelText,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            badgeRow.Children.Add(badge);
            badgeRow.Children.Add(new TextBlock
            {
                Text = UiLanguage.L($"  影响评估 · 预计开机占用 ≈ {e.BootSeconds:F1}s",
                                    $"  impact · est. boot cost ≈ {e.BootSeconds:F1}s"),
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            sp.Children.Add(badgeRow);

            sp.Children.Add(new TextBlock
            {
                Text = $"来源：{e.Source}",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6")),
                Margin = new Thickness(0, 3, 0, 0)
            });
            var cmdText = (e.Command.Length > 70 ? e.Command[..70] + "…" : e.Command);
            sp.Children.Add(new TextBlock
            {
                Text = cmdText,
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BDC3C7")),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (!e.Enabled)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("已禁用，不会随开机启动", "Disabled, will not start at boot"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            else if (e.ImpactLevel == Impact.High)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("⚠️ 建议禁用：此项较可能影响开机速度", "⚠️ Suggest disabling: this item likely slows boot"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E67E22")),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            grid.Children.Add(sp);

            var btn = new Button
            {
                Content = e.Enabled ? UiLanguage.L("禁用", "Disable") : UiLanguage.L("启用", "Enable"),
                Height = 32,
                Width = 72,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = e.Enabled ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27AE60")),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F2F3F5")),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var captured = e;
            btn.Click += (s, ev) => Toggle(captured);
            Grid.SetColumn(btn, 1);
            grid.Children.Add(btn);

            row.Child = grid;
            ListPanel.Children.Add(row);
        }
        TxtStatus.Text = UiLanguage.L(
            $"共 {_entries.Count} 个启动项，当前已启用 {enabledCount} 个，预计开机自启占用约 {totalSec:F1}s",
            $"{_entries.Count} startup items, {enabledCount} enabled, est. auto-start cost ≈ {totalSec:F1}s");
    }
}
