using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// RescuePage.xaml 交互逻辑 — 系统急救（导航 key="rescue"，v4.9.0 新增）。
    /// 蓝屏分析 / 电池健康 / 端口占用 / 驱动备份。所有命令为编译期字面量，
    /// 经 RescueCatalog.EnsureRegistered() 注册进 CommandRunner 白名单。
    /// 由 MainWindow 工厂懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。
    /// </summary>
    public partial class RescuePage : UserControl
    {
        private bool _busy;

        public RescuePage()
        {
            InitializeComponent();
            RescueCatalog.EnsureRegistered();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            Loaded += (_, __) => InitView();
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
            ThemeManager.ApplyButtonTheme(BtnBsod, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnBattery, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnPorts, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnDrivers, ThemeManager.AccentColor);
        }

        private void InitView()
        {
            // 管理员状态徽标
            bool admin = CommandRunner.IsElevated;
            AdminBadge.Background = new SolidColorBrush(admin
                ? Color.FromRgb(0x27, 0xAE, 0x60) : Color.FromRgb(0xE6, 0x7E, 0x22));
            AdminBadgeText.Text = UiLanguage.L(admin ? "管理员运行" : "标准权限",
                                               admin ? "Admin" : "Standard");
            // v5.4.0：电池检测（WMI 查询可能耗时）移到后台，避免页面加载卡顿
            _ = Task.Run(() =>
            {
                bool hasBattery = false;
                try
                {
                    using var pm = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerMeter");
                    if (pm != null) hasBattery = true;
                }
                catch { }
                if (!hasBattery)
                    try
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher(
                            "SELECT * FROM Win32_Battery");
                        hasBattery = searcher.Get().Count > 0;
                    }
                    catch { }
                Dispatcher.Invoke(() =>
                {
                    if (!hasBattery && BatteryCard != null)
                        BatteryCard.Visibility = Visibility.Collapsed;
                });
            });
            // 蓝屏转储文件列表
            ListMinidumps();
        }

        private void ListMinidumps()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "Minidump");
                if (!Directory.Exists(dir))
                {
                    TxtMinidumps.Text = UiLanguage.L("未发现 Minidump 目录，可能从未蓝屏过。",
                        "No Minidump directory found - no BSOD recorded.");
                    return;
                }
                var files = Directory.GetFiles(dir, "*.dmp")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(10)
                    .ToList();
                if (files.Count == 0)
                {
                    TxtMinidumps.Text = UiLanguage.L("Minidump 目录为空，无蓝屏记录。",
                        "Minidump folder is empty.");
                    return;
                }
                TxtMinidumps.Text = UiLanguage.L("最近蓝屏转储：", "Recent minidumps:") + "\n"
                    + string.Join("\n", files.Select(f =>
                        $"  {f.LastWriteTime:yyyy-MM-dd HH:mm}  {f.Length / 1024} KB  {f.Name}"));
            }
            catch (Exception ex)
            {
                TxtMinidumps.Text = UiLanguage.L("读取转储失败：", "Read minidumps failed: ") + ex.Message;
            }
        }

        private async void BtnBsod_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(BtnBsod, TxtBsodOut, TxtBsodHint,
                RescueCatalog.Find("wer_events")!, ParseBsodOutput);
        }

        private async void BtnBattery_Click(object sender, RoutedEventArgs e)
        {
            var cmd = RescueCatalog.Find("battery")!;
            var hint = TxtBatteryHint;
            if (_busy) return;
            _busy = true;
            SetButton(BtnBattery, false, "…");
            hint.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            hint.Text = UiLanguage.L("正在生成电池报告…", "Generating battery report…");
            try
            {
                var r = await CommandRunner.RunAsync(cmd.Command, cmd.RequireAdmin,
                    timeoutSec: cmd.TimeoutSec);
                var report = Path.Combine(Path.GetTempPath(), "sinan_battery_report.html");
                if (File.Exists(report))
                {
                    try { Process.Start(new ProcessStartInfo(report) { UseShellExecute = true }); }
                    catch { }
                    hint.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
                    hint.Text = UiLanguage.L("报告已生成并打开（默认浏览器）。", "Report generated and opened.");
                }
                else
                {
                    hint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                    hint.Text = r.Success
                        ? UiLanguage.L("报告未生成（设备可能不支持）。", "Report not generated (device may not support).")
                        : (r.Error ?? "exit=" + r.ExitCode);
                }
            }
            finally
            {
                _busy = false;
                SetButton(BtnBattery, true, UiLanguage.L("生成报告", "Generate"));
            }
        }

        private async void BtnPorts_Click(object sender, RoutedEventArgs e)
        {
            var filter = TxtPortFilter.Text?.Trim() ?? "";
            await RunAsync(BtnPorts, TxtPortsOut, TxtPortsHint,
                RescueCatalog.Find("ports")!, output => ParsePorts(output, filter));
        }

        private async void BtnDrivers_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(BtnDrivers, TxtDriversOut, TxtDriversHint,
                RescueCatalog.Find("driver_backup")!, output =>
                {
                    var lines = output.Split('\n')
                        .Where(l => l.Contains("正在导出", StringComparison.OrdinalIgnoreCase)
                                 || l.Contains("导出", StringComparison.OrdinalIgnoreCase)
                                 || l.Contains("已导出", StringComparison.OrdinalIgnoreCase)
                                 || l.Contains("成功", StringComparison.OrdinalIgnoreCase)
                                 || l.Contains("error", StringComparison.OrdinalIgnoreCase)
                                 || l.Contains("failed", StringComparison.OrdinalIgnoreCase))
                        .Take(40);
                    var s = string.Join("\n", lines);
                    return string.IsNullOrWhiteSpace(s) ? output : s;
                });
        }

        /// <summary>统一执行白名单命令并展示输出。</summary>
        private async Task RunAsync(Button btn, TextBox outBox, TextBlock hint,
            RescueCatalog.RescueCommand cmd, Func<string, string>? transform = null)
        {
            if (_busy) return;
            _busy = true;
            SetButton(btn, false, "…");
            outBox.Text = "";
            hint.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            hint.Text = UiLanguage.L("正在执行…", "Running…");
            try
            {
                var r = await CommandRunner.RunAsync(cmd.Command, cmd.RequireAdmin,
                    timeoutSec: cmd.TimeoutSec);
                var raw = r.Output;
                outBox.Text = transform != null ? transform(raw) : raw;
                if (r.Success)
                {
                    hint.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
                    hint.Text = UiLanguage.L("执行完成。", "Done.");
                }
                else
                {
                    hint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                    hint.Text = r.Error ?? UiLanguage.L("执行失败（退出码 ", "Failed (exit ") + r.ExitCode + ")";
                }
            }
            catch (Exception ex)
            {
                hint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                hint.Text = ex.Message;
            }
            finally
            {
                _busy = false;
                SetButton(btn, true, btn.Content is string s ? s.Replace("…", "") : cmd.LabelZh);
                // 恢复按钮文案
                if (btn == BtnBsod) SetButton(BtnBsod, true, UiLanguage.L("开始分析", "Analyze"));
                else if (btn == BtnPorts) SetButton(BtnPorts, true, UiLanguage.L("扫描", "Scan"));
                else if (btn == BtnDrivers) SetButton(BtnDrivers, true, UiLanguage.L("开始备份", "Backup"));
            }
        }

        private static void SetButton(Button b, bool enabled, string text)
        {
            b.IsEnabled = enabled;
            b.Content = text;
        }

        /// <summary>从崩溃事件文本中提取 BugCheck 码与疑似驱动（输出精简）。</summary>
        private static string ParseBsodOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return UiLanguage.L("（无输出）", "(no output)");
            var lines = output.Split('\n').Select(l => l.Trim()).ToList();

            var sb = new System.Text.StringBuilder();
            var bugcheck = new Regex(@"Bugcheck\s+(\S+)", RegexOptions.IgnoreCase);
            var driver = new Regex(@"The\s+value\s+is\s+(0x[0-9a-fA-F]+)\s*;\s*(\S+\.sys)?", RegexOptions.IgnoreCase);
            bool inKv = false;
            foreach (var line in lines)
            {
                if (line.Contains("BugCheck", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Event ID 41", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Event ID 1001", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("WER", StringComparison.OrdinalIgnoreCase))
                {
                    inKv = true;
                    sb.AppendLine(line);
                    continue;
                }
                if (inKv && line.StartsWith("详细信息", StringComparison.OrdinalIgnoreCase)) continue;
                if (inKv && string.IsNullOrEmpty(line)) { inKv = false; continue; }
                if (inKv) sb.AppendLine(line);
            }

            if (sb.Length == 0) return output.Length > 1500 ? output[..1500] : output;

            // 常见蓝屏错误码释义（扩充至 26 条，普通/专业模式均显示，帮用户看懂错误码）
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["0x0000007B"] = "INACCESSIBLE_BOOT_DEVICE：磁盘控制器/启动驱动问题",
                ["0x0000007E"] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED：驱动或系统服务异常",
                ["0x00000050"] = "PAGE_FAULT_IN_NONPAGED_AREA：内存或驱动问题",
                ["0x000000D1"] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL：驱动内存访问错误",
                ["0x0000001A"] = "MEMORY_MANAGEMENT：内存管理错误（可测内存）",
                ["0x0000003B"] = "SYSTEM_SERVICE_EXCEPTION：系统服务异常（驱动）",
                ["0x000000EF"] = "CRITICAL_PROCESS_DIED：关键进程崩溃",
                ["0x00000124"] = "WHEA_UNCORRECTABLE_ERROR：硬件错误（CPU/内存/主板）",
                ["0x0000000A"] = "IRQL_NOT_LESS_OR_EQUAL：驱动与系统内核冲突（常见于驱动问题）",
                ["0x0000007A"] = "KERNEL_DATA_INPAGE_ERROR：内存或硬盘读取失败",
                ["0x000000C2"] = "BAD_POOL_CALLER：内存池调用错误（多为驱动问题）",
                ["0x000000C4"] = "DRIVER_VERIFIER_DETECTED_VIOLATION：驱动验证器检测到违规",
                ["0x00000116"] = "VIDEO_TDR_FAILURE：显卡驱动停止响应（建议更新显卡驱动）",
                ["0x00000133"] = "DPC_WATCHDOG_VIOLATION：驱动响应超时（常见于磁盘/存储驱动）",
                ["0x000000EA"] = "THREAD_STUCK_IN_DEVICE_DRIVER：显卡驱动卡死",
                ["0x00000139"] = "KERNEL_SECURITY_CHECK_FAILURE：内核安全检测失败（驱动或系统文件损坏）",
                ["0x0000009F"] = "DRIVER_POWER_STATE_FAILURE：驱动电源状态错误（电源管理问题）",
                ["0x00000077"] = "KERNEL_STACK_INPAGE_ERROR：内核页面读取错误（硬盘或内存问题）",
                ["0x000000F4"] = "CRITICAL_OBJECT_TERMINATION：关键系统进程终止（硬件或驱动）",
                ["0x0000001E"] = "KMODE_EXCEPTION_NOT_HANDLED：内核模式异常（驱动或硬件）",
                ["0x0000002E"] = "DATA_BUS_ERROR：数据总线错误（内存或硬件）",
                ["0x0000003F"] = "NO_MORE_SYSTEM_PTES：系统内存不足",
                ["0x0000008E"] = "KERNEL_MODE_EXCEPTION_NOT_HANDLED：内核异常（内存或驱动）",
                ["0x00000101"] = "CLOCK_WATCHDOG_TIMEOUT：CPU 核心无响应（超频或 CPU 故障）",
                ["0x00000112"] = "MACHINE_CHECK_EXCEPTION：硬件机器检查错误（CPU 或内存）",
                ["0x0000013E"] = "KERNEL_MODE_HEAP_CORRUPTION：内核内存堆损坏"
            };
            foreach (var kv in hints)
                if (output.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("▶ " + kv.Key + " " + kv.Value);
            return sb.ToString();
        }

        /// <summary>解析 netstat -ano 输出，映射进程名，可选按端口过滤（中文列名，通俗展示）。</summary>
        private static string ParsePorts(string output, string filter)
        {
            if (string.IsNullOrWhiteSpace(output)) return UiLanguage.L("（无输出）", "(no output)");
            var rows = new List<string>();
            var pidCache = new Dictionary<int, string>();
            string? ProcName(int pid)
            {
                if (pid <= 0) return "-";
                if (pidCache.TryGetValue(pid, out var n)) return n;
                try
                {
                    using var p = Process.GetProcessById(pid);   // v5.4.0：进程对象释放
                    n = p.ProcessName;
                }
                catch { n = "?"; }
                pidCache[pid] = n;
                return n;
            }
            // 普通模式把连接状态翻译成中文
            static string StateZh(string s) => s.ToUpperInvariant() switch
            {
                "LISTENING" => "监听中",
                "ESTABLISHED" => "已连接",
                "TIME_WAIT" => "等待关闭",
                "CLOSE_WAIT" => "关闭等待",
                "SYN_SENT" => "连接中",
                "FIN_WAIT" => "结束等待",
                "SYN_RECEIVED" => "接收连接",
                _ => s
            };

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("活动连接", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Active Connections", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("协议", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Proto", StringComparison.OrdinalIgnoreCase))
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                var proto = parts[0];
                var local = parts[1];
                var foreign = parts[2];
                var state = parts[3];
                if (!int.TryParse(parts[4], out var pid)) continue;
                if (!string.IsNullOrEmpty(filter) && !local.Contains(":" + filter + " ", StringComparison.OrdinalIgnoreCase)
                    && !local.EndsWith(":" + filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                rows.Add($"{proto,-5} {local,-28} {foreign,-24} {StateZh(state),-12} {ProcName(pid)}  (PID {pid})");
            }

            if (rows.Count == 0)
                return UiLanguage.L("（无匹配结果）", "(no match)");
            var header = string.Format("{0,-6} {1,-28} {2,-24} {3,-12} {4}", "协议", "本地地址", "远程地址", "状态", "进程");
            return header + "\n" + string.Join("\n", rows.Take(400));
        }
    }
}
