using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>启动项管理页（导航 key="startup"）：管理开机自启程序。
/// v4.9.0 增强：扫描面扩展（RunOnce / Wow6432Node / Policies）+ 原生 StartupApproved 禁用机制
/// （与任务管理器双向同步，保留旧 WINHELP_Disabled 读取兼容与手动迁移）+ 发布者/数字签名列 + 计划任务只读展示。
/// 由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。</summary>
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
        // v4.9.0：旧版禁用标记（来自 WINHELP_Disabled 子键）
        public bool LegacyDisabled;
        // v4.9.0：只读项（计划任务 / 非提权 HKLM）
        public bool ReadOnly;
        public string ReadOnlyNote = "";
        // v4.9.0：发布者 / 数字签名
        public string Publisher = "";
        public string SignedState = "";
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

    // v4.9.0：原生 StartupApproved 路径（与任务管理器共用）
    private const string ApprovedRun =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32 =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string RunPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public StartupPage()
    {
        InitializeComponent();
        ApplyTheme();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        UiLanguage.Changed += () => Dispatcher.Invoke(Render);
        LoadAll();
        Render();
        // 计划任务扫描较慢，异步执行，完成后重绘
        _ = LoadScheduledTasksAsync();
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

    // ===== 扫描面（v4.9.0 扩展）=====

    private void LoadEntries()
    {
        // 1) 注册表 Run（当前用户 / 所有用户）
        TryLoadRegistry(Registry.CurrentUser, RunPath, "当前用户");
        TryLoadRegistry(Registry.LocalMachine, RunPath, "所有用户（需管理员权限）");

        // 2) RunOnce（一次性自启，通常用于安装程序）
        TryLoadRegistry(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "当前用户 RunOnce");
        TryLoadRegistry(Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "所有用户 RunOnce（需管理员权限）");

        // 3) Wow6432Node Run（32 位程序在 64 位系统中的注册位置）
        TryLoadRegistry(Registry.LocalMachine,
            @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", "32 位程序（Wow6432Node）");

        // 4) 组策略 Run（Policies\Explorer\Run）
        TryLoadRegistry(Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "组策略 Run（所有用户）");
        TryLoadRegistry(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "组策略 Run（当前用户）");

        // 启动文件夹
        var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        TryLoadStartupFolder(startupDir, "当前用户启动文件夹");

        var commonStartupDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        TryLoadStartupFolder(commonStartupDir, "公用启动文件夹");
    }

    private void TryLoadRegistry(RegistryKey baseKey, string runPath, string source)
    {
        bool isHklm = baseKey == Registry.LocalMachine;
        bool adminOnly = isHklm && !CommandRunner.IsElevated; // 非提权 HKLM 只读

        try
        {
            using var key = baseKey.OpenSubKey(runPath, false);
            if (key != null)
                foreach (var name in key.GetValueNames())
                    _entries.Add(BuildRegistryEntry(baseKey, runPath, source, name,
                        key.GetValue(name)?.ToString() ?? "", adminOnly));

            // 旧版禁用子键（兼容：老用户已禁用项必须继续显示为禁用，否则会静默复活）
            using var dis = baseKey.OpenSubKey(runPath + "\\WINHELP_Disabled", false);
            if (dis != null)
                foreach (var name in dis.GetValueNames())
                    _entries.Add(BuildRegistryEntry(baseKey, runPath, source, name,
                        dis.GetValue(name)?.ToString() ?? "", adminOnly, legacyDisabled: true));
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("读取启动项出错（部分项需管理员权限）：", "Error reading startup items (some need admin): ") + ex.Message;
        }
    }

    private Entry BuildRegistryEntry(RegistryKey baseKey, string runPath, string source,
        string name, string command, bool readOnly, bool legacyDisabled = false)
    {
        // 原生 StartupApproved 状态（与任务管理器一致）：0x03=禁用 / 0x02=启用 / 无记录=启用
        bool? approved = ReadApproved(baseKey, name);
        // 语义：legacyDisabled=true → 禁用；approved==true → 禁用；其余 → 启用
        bool enabled = !legacyDisabled && approved != true;

        return new Entry
        {
            Name = name,
            Command = command,
            Source = source,
            Base = baseKey,
            RunPath = runPath,
            Enabled = enabled,
            LegacyDisabled = legacyDisabled,
            ReadOnly = readOnly,
            ReadOnlyNote = readOnly ? UiLanguage.L("系统级启动项：需管理员权限，重启本程序后即可修改",
                "System-level item: restart as admin to modify") : ""
        };
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
                    Enabled = !file.EndsWith(".lnk.disabled", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch { }
    }

    // ===== 原生 StartupApproved（v4.9.0）=====

    /// <summary>读取原生禁用状态：true=原生禁用，false=原生启用，null=无记录。</summary>
    private static bool? ReadApproved(RegistryKey baseKey, string name)
    {
        try
        {
            foreach (var path in new[] { ApprovedRun, ApprovedRun32 })
            {
                using var k = baseKey.OpenSubKey(path);
                if (k?.GetValue(name) is byte[] b && b.Length >= 1)
                {
                    if (b[0] == 0x03) return true;
                    if (b[0] == 0x02) return false;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>写入原生禁用状态（12 字节二进制，与任务管理器格式一致）。</summary>
    private static void WriteApproved(RegistryKey baseKey, string name, bool disabled)
    {
        var bytes = new byte[12];
        bytes[0] = disabled ? (byte)0x03 : (byte)0x02;
        long ft = DateTime.UtcNow.ToFileTimeUtc();
        for (int i = 0; i < 8; i++) bytes[4 + i] = (byte)(ft >> (8 * i));
        using var k = baseKey.CreateSubKey(ApprovedRun);
        k?.SetValue(name, bytes, RegistryValueKind.Binary);
    }

    // ===== 计划任务（只读展示，v4.9.0）=====
    // 任务名是用户数据，无法字面量白名单化，因此只读展示、不提供开关。

    private async Task LoadScheduledTasksAsync()
    {
        try
        {
            CommandRunner.RegisterAllowed(new[] { "schtasks /query /xml" });
            var r = await CommandRunner.RunAsync("schtasks /query /xml", timeoutSec: 40);
            if (!r.Success || string.IsNullOrWhiteSpace(r.Output)) return;

            // 输出为多个 <Task ...>...</Task> 拼接，逐个解析
            foreach (Match m in Regex.Matches(r.Output, @"<Task\b[\s\S]*?</Task>"))
            {
                try
                {
                    var doc = System.Xml.Linq.XDocument.Parse(m.Value);
                    var root = doc.Root;
                    if (root == null) continue;
                    bool logonOrBoot = false, enabled = false;
                    foreach (var trig in root.Descendants("Triggers").SelectMany(t => t.Elements()))
                    {
                        var tname = trig.Name.LocalName;
                        if (tname is "LogonTrigger" or "BootTrigger")
                        {
                            logonOrBoot = true;
                            var en = trig.Element("Enabled");
                            if (en == null || en.Value == "true") enabled = true;
                        }
                    }
                    if (!logonOrBoot || !enabled) continue;
                    var taskName = (string?)root.Attribute("URI") ?? "";
                    var exec = root.Descendants("Command").FirstOrDefault()?.Value ?? "";
                    var args = root.Descendants("Arguments").FirstOrDefault()?.Value ?? "";
                    if (string.IsNullOrWhiteSpace(taskName)) continue;
                    Dispatcher.Invoke(() =>
                    {
                        _entries.Add(new Entry
                        {
                            Name = taskName,
                            Command = exec + " " + args,
                            Source = "计划任务（只读）",
                            Enabled = false,
                            ReadOnly = true,
                            ReadOnlyNote = UiLanguage.L("登录/启动时触发，只读展示", "Logon/boot triggered, read-only")
                        });
                        Render();
                    });
                }
                catch { /* 单个任务解析失败跳过 */ }
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

        // v4.9.0：发布者 + 数字签名（失败静默降级为未签名）
        if (path != null && File.Exists(path))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                e.Publisher = vi.CompanyName ?? "";
                if (string.IsNullOrWhiteSpace(e.Publisher)) e.Publisher = vi.FileDescription ?? "";
            }
            catch { }
            try
            {
                using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader
                    .LoadCertificateFromFile(path);
                var subject = cert.Subject ?? "";
                var cn = Regex.Match(subject, @"CN=([^,]+)").Groups[1].Value;
                e.SignedState = string.IsNullOrWhiteSpace(cn) ? UiLanguage.L("已签名", "Signed") : cn;
            }
            catch { e.SignedState = UiLanguage.L("未签名", "Unsigned"); }
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

    // ===== 启用 / 禁用（v4.9.0：优先原生 StartupApproved）=====

    private void Toggle(Entry e)
    {
        try
        {
            if (e.ReadOnly)
            {
                TxtStatus.Text = UiLanguage.L("该项只读：", "Read-only item: ") + e.ReadOnlyNote;
                return;
            }

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
                // 旧版禁用项：先迁移到原生机制（恢复 Run 值 + 清除旧键），再按原生机制切换
                if (e.LegacyDisabled)
                {
                    MigrateLegacyToNative(e, enableFirst: true);
                    e.LegacyDisabled = false;
                }

                if (e.Enabled)
                {
                    // 禁用：写入原生 StartupApproved=0x03（与任务管理器一致，Run 值保留）
                    WriteApproved(e.Base!, e.Name, disabled: true);
                    e.Enabled = false;
                    TxtStatus.Text = UiLanguage.L($"已禁用：{e.Name}", $"Disabled: {e.Name}");
                }
                else
                {
                    WriteApproved(e.Base!, e.Name, disabled: false);
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

    /// <summary>把旧版 WINHELP_Disabled 禁用项迁移到原生 StartupApproved 机制（手动触发，不批量自动迁移）。</summary>
    private void MigrateLegacyToNative(Entry e, bool enableFirst)
    {
        // 从旧禁用子键读出值，写回 Run
        using var dis = e.Base?.OpenSubKey(e.RunPath + "\\WINHELP_Disabled", true);
        if (dis == null) return;
        var val = dis.GetValue(e.Name);
        if (val == null) return;
        using var run = e.Base?.CreateSubKey(e.RunPath);
        run?.SetValue(e.Name, val);
        dis.DeleteValue(e.Name, false);
        // 写入原生状态（启用=0x02；由调用方随后决定是否改为禁用）
        WriteApproved(e.Base!, e.Name, disabled: !enableFirst);
    }

    /// <summary>手动迁移单个旧版禁用项到原生机制（保留禁用状态）。</summary>
    private void MigrateOne(Entry e)
    {
        try
        {
            MigrateLegacyToNative(e, enableFirst: false);
            e.LegacyDisabled = false;
            e.Enabled = false;
            TxtStatus.Text = UiLanguage.L($"已迁移到系统机制（保持禁用）：{e.Name}",
                $"Migrated to native mechanism (kept disabled): {e.Name}");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("迁移失败：", "Migration failed: ") + ex.Message;
        }
        Render();
    }

    // ===== 渲染 =====

    private void Render()
    {
        TxtTitle.Text = UiLanguage.L("启动项管理", "Startup Manager");
        TxtSubtitle.Text = UiLanguage.L("禁用不必要的开机自启项，加快开机速度（与任务管理器同步）",
            "Disable unneeded startup items to speed up boot (synced with Task Manager)");
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
            Text = UiLanguage.L("本次开机信息", "Boot Information"),
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

            // 影响评估徽章 + 开机耗时估算
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

            // v4.9.0：旧版禁用徽标（橙色）
            if (e.LegacyDisabled)
            {
                var legacy = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E67E22")),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                legacy.Child = new TextBlock
                {
                    Text = UiLanguage.L("旧版禁用", "Legacy off"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                badgeRow.Children.Add(legacy);
            }
            // v4.9.0：只读徽标（计划任务 / 非提权 HKLM）
            if (e.ReadOnly)
            {
                var ro = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6")),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                ro.Child = new TextBlock
                {
                    Text = UiLanguage.L("只读", "Read-only"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                badgeRow.Children.Add(ro);
            }

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

            // v4.9.0：发布者 / 签名
            if (!string.IsNullOrEmpty(e.Publisher))
                sp.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("发布者：", "Publisher: ") + e.Publisher,
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 3, 0, 0)
                });
            if (!string.IsNullOrEmpty(e.SignedState))
            {
                var signedColor = e.SignedState == UiLanguage.L("未签名", "Unsigned")
                    ? "#E67E22" : "#27AE60";
                sp.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("签名：", "Signature: ") + e.SignedState,
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(signedColor)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            sp.Children.Add(new TextBlock
            {
                Text = UiLanguage.L("来源：", "Source: ") + Glossary.Hint(e.Source),
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
            if (e.ReadOnly && !string.IsNullOrEmpty(e.ReadOnlyNote))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = e.ReadOnlyNote,
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6")),
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            grid.Children.Add(sp);

            // 右侧操作列：启用/禁用按钮 + 可选迁移按钮
            var actions = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            if (!e.ReadOnly)
            {
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
                actions.Children.Add(btn);
            }
            else
            {
                var roBtn = new Button
                {
                    Content = UiLanguage.L("只读", "Read-only"),
                    Height = 32,
                    Width = 72,
                    FontSize = 13,
                    IsEnabled = false,
                    BorderThickness = new Thickness(0)
                };
                actions.Children.Add(roBtn);
            }

            // v4.9.0：旧版禁用项提供「迁移到系统机制」按钮（手动，不自动批量迁移）
            if (e.LegacyDisabled && !e.ReadOnly)
            {
                var mig = new Button
                {
                    Content = UiLanguage.L("迁移到系统机制", "Migrate to native"),
                    Height = 28,
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 11,
                    Padding = new Thickness(8, 0, 8, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E67E22")),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var captured2 = e;
                mig.Click += (s, ev) => MigrateOne(captured2);
                actions.Children.Add(mig);
            }
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            row.Child = grid;
            ListPanel.Children.Add(row);
        }
        TxtStatus.Text = UiLanguage.L(
            $"共 {_entries.Count} 个启动项，当前已启用 {enabledCount} 个，预计开机自启占用约 {totalSec:F1}s",
            $"{_entries.Count} startup items, {enabledCount} enabled, est. auto-start cost ≈ {totalSec:F1}s");
    }
}
