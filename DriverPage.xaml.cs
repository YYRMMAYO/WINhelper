// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// 驱动管理页（导航 key="driver"，v5.6.0 新增）：
    /// - 驱动健康检测：Win32_PnPEntity 中 ConfigManagerErrorCode≠0 的问题设备，配白话原因与修复建议；
    /// - 关键设备驱动信息：显卡 / 声卡 / 网卡型号 + 驱动版本 + 发布日期；
    /// - 驱动备份入口：跳转「系统急救」的 dism 一键备份；
    /// - 驱动官网直达：按本机显卡 / 主板品牌自动匹配官方驱动下载页。
    /// 面向新手：所有检测放后台线程，结果用浅色 CodePanel 展示，附通俗解释（Glossary 风格）。
    /// </summary>
    public partial class DriverPage : UserControl
    {
        /// <summary>导航请求（由 MainWindow 注入，用于跳转「系统急救」等模块）</summary>
        public Action<string>? OnNavigate;

        private bool _busy = false;

        public DriverPage()
        {
            InitializeComponent();
            ThemeManager.ApplyButtonTheme(BtnCheck, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnInfo, Color.FromRgb(0x27, 0xAE, 0x60));
            ThemeManager.ApplyButtonTheme(BtnBackup, Color.FromRgb(0xE6, 0x7E, 0x22));
            ThemeManager.ApplyButtonTheme(BtnGpu, Color.FromRgb(0x34, 0x49, 0x5E));
            ThemeManager.ApplyButtonTheme(BtnCpu, Color.FromRgb(0x34, 0x49, 0x5E));
        }

        private static string L(string zh, string en) => UiLanguage.L(zh, en);

        // ===== 驱动健康检测 =====

        private async void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SetButton(BtnCheck, false);
            TxtCheckOut.Text = "";
            TxtCheckHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            TxtCheckHint.Text = L("正在检测…", "Scanning…");
            try
            {
                var result = await Task.Run(ScanProblemDevices);
                TxtCheckOut.Text = result;
                if (result.Contains(L("未发现", "No problem"), StringComparison.OrdinalIgnoreCase))
                {
                    TxtCheckHint.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
                    TxtCheckHint.Text = L("检测完成，未发现异常。", "Done — no issues found.");
                }
                else
                {
                    TxtCheckHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                    TxtCheckHint.Text = L("发现驱动异常，请按下方建议处理。", "Driver issues found — follow the advice below.");
                }
            }
            catch (Exception ex)
            {
                TxtCheckOut.Text = L("检测失败：", "Scan failed: ") + ex.Message;
                TxtCheckHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                TxtCheckHint.Text = L("检测失败。", "Scan failed.");
            }
            finally
            {
                _busy = false;
                SetButton(BtnCheck, true);
            }
        }

        /// <summary>扫描所有有问题的设备（ConfigManagerErrorCode ≠ 0），输出白话原因 + 修复建议。</summary>
        private static string ScanProblemDevices()
        {
            var rows = new List<(string name, string pnpClass, int code, string status)>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, ConfigManagerErrorCode, PNPClass, Status FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using var _mo = mo;
                    int code = 0;
                    try { code = Convert.ToInt32(mo["ConfigManagerErrorCode"]); } catch { continue; }
                    if (code == 0) continue;
                    var name = Convert.ToString(mo["Name"]) ?? L("未知设备", "Unknown device");
                    var cls = Convert.ToString(mo["PNPClass"]) ?? "";
                    var status = Convert.ToString(mo["Status"]) ?? "";
                    rows.Add((name, cls, code, status));
                }
            }
            catch { /* WMI 不可用时输出空 */ }

            if (rows.Count == 0)
                return L("✓ 未发现问题驱动设备。", "✓ No problem devices found.");

            var sb = new StringBuilder();
            sb.AppendLine(L("发现 " + rows.Count + " 个问题设备：", $"Found {rows.Count} problem device(s):"));
            sb.AppendLine();
            foreach (var (name, cls, code, status) in rows)
            {
                sb.AppendLine("• " + name);
                if (!string.IsNullOrEmpty(cls))
                    sb.AppendLine("  类型：" + cls);
                sb.AppendLine("  错误代码 " + code + "：" + DescribeError(code));
                sb.AppendLine();
            }
            sb.AppendLine(L("通用处理步骤：", "General steps:"));
            sb.AppendLine(L("1. 右键「此电脑」→ 管理 → 设备管理器，找到带黄色感叹号的设备；", "1. Right-click 'This PC' → Manage → Device Manager, find the device with a yellow mark;"));
            sb.AppendLine(L("2. 右键 → 更新驱动程序 → 自动搜索；", "2. Right-click → Update driver → search automatically;"));
            sb.AppendLine(L("3. 若无效，到设备厂商官网下载对应驱动安装（或先用手机热点联网下载）。", "3. If that fails, download the matching driver from the vendor's site (use a phone hotspot if needed)."));
            return sb.ToString();
        }

        /// <summary>把设备管理器错误代码翻译成白话（中文/英文随语言）。</summary>
        private static string DescribeError(int code) => code switch
        {
            1 => L("未正确配置（请到设备管理器卸载后重新扫描硬件）", "not configured properly (uninstall in Device Manager, then rescan hardware)"),
            10 => L("设备无法启动（尝试更新驱动或重启电脑）", "device cannot start (try updating the driver or rebooting)"),
            18 => L("需要重新安装驱动（更新驱动程序）", "reinstall the driver (Update driver)"),
            21 => L("Windows 正在删除此设备（稍后重启电脑）", "Windows is removing the device (reboot later)"),
            22 => L("设备已被禁用（右键 → 启用设备）", "device is disabled (right-click → Enable device)"),
            24 => L("驱动缺失或未安装（更新驱动程序）", "driver missing or not installed (Update driver)"),
            28 => L("未安装驱动程序（右键 → 更新驱动程序）", "driver not installed (right-click → Update driver)"),
            31 => L("驱动未正确加载（卸载设备后重启，再让系统自动安装）", "driver failed to load (uninstall the device, reboot, let Windows reinstall)"),
            32 => L("此设备的驱动服务无法启动（重装驱动）", "the driver service cannot start (reinstall the driver)"),
            33 => L("Windows 无法确定此设备的资源（尝试重装驱动）", "Windows cannot determine the device's resources (reinstall the driver)"),
            34 => L("需要修改设备设置（查看设备说明文档）", "device settings must be changed (check the device manual)"),
            35 => L("设备的固件未提供正确的配置信息（重装驱动或联系厂商）", "firmware did not provide correct configuration (reinstall driver or contact vendor)"),
            36 => L("设备请求中断，但无法启用（重装驱动）", "device requested an interrupt it cannot use (reinstall the driver)"),
            37 => L("Windows 无法初始化此设备的驱动（重装驱动）", "Windows cannot initialize the driver (reinstall it)"),
            39 => L("Windows 无法加载此设备的驱动，可能已损坏（重装驱动）", "Windows cannot load the driver — it may be corrupted (reinstall it)"),
            40 => L("驱动信息缺失或注册表损坏（重装驱动）", "driver information missing or registry corrupted (reinstall the driver)"),
            41 => L("驱动加载成功但立即失败，可能已损坏（更新 / 重装驱动）", "driver loaded but failed immediately — it may be corrupted (update / reinstall)"),
            43 => L("Windows 已停止此设备，可能硬件故障或驱动不兼容（先重装驱动，仍报错则可能硬件损坏）", "Windows stopped the device — possible hardware failure or driver incompatibility (reinstall the driver first; if it persists, the hardware may be broken)"),
            45 => L("设备未连接到电脑（检查连接线 / 重新插拔）", "device is not connected (check cables / re-plug it)"),
            46 => L("Windows 无法访问此设备（重启电脑）", "Windows cannot access the device (reboot)"),
            47 => L("设备无法使用，等待移除（拔下后重新插入）", "device cannot be used until removed (unplug and re-plug)"),
            48 => L("设备的软件已停止（重启电脑）", "the device's software was stopped (reboot)"),
            52 => L("无法验证驱动数字签名（到官网下载对应签名驱动）", "the driver's digital signature cannot be verified (download the signed driver from the vendor)"),
            _ => L("驱动异常（尝试更新 / 重装驱动，或到官网下载）", "driver problem (try updating / reinstalling, or download from the vendor site)"),
        };

        // ===== 关键设备驱动信息 =====

        private async void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SetButton(BtnInfo, false);
            TxtInfoOut.Text = "";
            TxtInfoHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            TxtInfoHint.Text = L("正在读取…", "Reading…");
            try
            {
                TxtInfoOut.Text = await Task.Run(ReadKeyDevices);
                TxtInfoHint.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
                TxtInfoHint.Text = L("读取完成。", "Done.");
            }
            catch (Exception ex)
            {
                TxtInfoOut.Text = L("读取失败：", "Read failed: ") + ex.Message;
                TxtInfoHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                TxtInfoHint.Text = L("读取失败。", "Read failed.");
            }
            finally
            {
                _busy = false;
                SetButton(BtnInfo, true);
            }
        }

        /// <summary>读取显卡 / 声卡 / 网卡的型号与驱动版本、发布日期。</summary>
        private static string ReadKeyDevices()
        {
            var sb = new StringBuilder();
            ReadCategory(sb, L("显卡 (GPU)", "Graphics (GPU)"),
                "SELECT Name, DriverVersion, DriverDate, Manufacturer FROM Win32_VideoController");
            ReadCategory(sb, L("声卡 (Audio)", "Audio"),
                "SELECT Name, DriverVersion, DriverDate, Manufacturer FROM Win32_SoundDevice");
            ReadCategory(sb, L("网卡 (Network)", "Network"),
                "SELECT Name, DriverVersion, DriverDate, Manufacturer FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True");
            if (sb.Length == 0)
                sb.AppendLine(L("未能读取到设备信息（WMI 不可用）。", "Could not read device info (WMI unavailable)."));
            return sb.ToString();
        }

        private static void ReadCategory(StringBuilder sb, string title, string query)
        {
            var items = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject mo in searcher.Get())
                {
                    using var _mo = mo;
                    var name = Trim(Convert.ToString(mo["Name"]));
                    if (string.IsNullOrEmpty(name)) continue;
                    var ver = Trim(Convert.ToString(mo["DriverVersion"]));
                    var date = FormatDriverDate(Convert.ToString(mo["DriverDate"]));
                    var mfr = Trim(Convert.ToString(mo["Manufacturer"]));
                    var line = name;
                    if (!string.IsNullOrEmpty(ver)) line += "  |  驱动 " + ver;
                    if (!string.IsNullOrEmpty(date)) line += "  (" + date + ")";
                    if (!string.IsNullOrEmpty(mfr)) line += "  [" + mfr + "]";
                    items.Add(line);
                }
            }
            catch { }
            if (items.Count == 0) return;
            sb.AppendLine("■ " + title);
            foreach (var it in items) sb.AppendLine("  " + it);
            sb.AppendLine();
        }

        /// <summary>WMI DriverDate（形如 20200813000000.000000-000）→ yyyy-MM-dd；解析失败返回空串。</summary>
        private static string FormatDriverDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8) return "";
            return DateTime.TryParseExact(raw[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt) ? dt.ToString("yyyy-MM-dd") : "";
        }

        private static string Trim(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

        // ===== 备份与更新 =====

        private void BtnBackup_Click(object sender, RoutedEventArgs e) => OnNavigate?.Invoke("rescue");

        /// <summary>按本机显卡品牌打开对应官方驱动下载页。</summary>
        private async void BtnGpu_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://www.nvidia.cn/drivers/";
            try
            {
                string? vendor = await Task.Run(() =>
                {
                    using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    using var results = s.Get();
                    foreach (ManagementObject mo in results)
                    {
                        using var _mo = mo;
                        var n = Convert.ToString(mo["Name"]) ?? "";
                        if (n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "nvidia";
                        if (n.Contains("AMD", StringComparison.OrdinalIgnoreCase) || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return "amd";
                        if (n.Contains("Intel", StringComparison.OrdinalIgnoreCase) || n.Contains("Iris", StringComparison.OrdinalIgnoreCase)) return "intel";
                    }
                    return null;
                });
                url = vendor switch
                {
                    "amd" => "https://www.amd.com/zh-cn/support/download/drivers.html",
                    "intel" => "https://www.intel.cn/content/www/cn/zh/download-center/home.html",
                    _ => "https://www.nvidia.cn/drivers/",
                };
            }
            catch { }
            SafeUrl.Open(url);
        }

        /// <summary>按本机主板 / 整机品牌打开对应官方驱动下载页（驱动大多数来自主板厂商）。</summary>
        private async void BtnCpu_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://www.asus.com.cn/support/";
            try
            {
                string? brand = await Task.Run(() =>
                {
                    string mfr = "";
                    using (var s = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem"))
                    using (var results1 = s.Get())
                    {
                        foreach (ManagementObject mo in results1)
                        {
                            using var _mo = mo;
                            mfr = Convert.ToString(mo["Manufacturer"]) ?? "";
                            break;
                        }
                    }
                    string board = "";
                    using (var s2 = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_BaseBoard"))
                    using (var results2 = s2.Get())
                    {
                        foreach (ManagementObject mo in results2)
                        {
                            using var _mo = mo;
                            board = Convert.ToString(mo["Manufacturer"]) ?? "";
                            break;
                        }
                    }
                    string tag = mfr + " " + board;
                    if (tag.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase) || tag.Contains("华硕", StringComparison.OrdinalIgnoreCase)) return "asus";
                    if (tag.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || tag.Contains("MSI", StringComparison.OrdinalIgnoreCase)) return "msi";
                    if (tag.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase) || tag.Contains("技嘉", StringComparison.OrdinalIgnoreCase)) return "gigabyte";
                    if (tag.Contains("ASRock", StringComparison.OrdinalIgnoreCase) || tag.Contains("华擎", StringComparison.OrdinalIgnoreCase)) return "asrock";
                    if (tag.Contains("Colorful", StringComparison.OrdinalIgnoreCase) || tag.Contains("七彩虹", StringComparison.OrdinalIgnoreCase)) return "colorful";
                    if (tag.Contains("MAXSUN", StringComparison.OrdinalIgnoreCase) || tag.Contains("铭瑄", StringComparison.OrdinalIgnoreCase)) return "maxsun";
                    if (tag.Contains("LENOVO", StringComparison.OrdinalIgnoreCase) || tag.Contains("联想", StringComparison.OrdinalIgnoreCase)) return "lenovo";
                    if (tag.Contains("Dell", StringComparison.OrdinalIgnoreCase) || tag.Contains("戴尔", StringComparison.OrdinalIgnoreCase)) return "dell";
                    if (tag.Contains("HP", StringComparison.OrdinalIgnoreCase) || tag.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)) return "hp";
                    return null;
                });
                url = brand switch
                {
                    "msi" => "https://www.msi.cn/support",
                    "gigabyte" => "https://www.gigabyte.cn/Support",
                    "asrock" => "https://www.asrock.com/support/index.cn.asp",
                    "colorful" => "https://www.colorful.cn/product.aspx",
                    "maxsun" => "https://www.maxsun.com.cn",
                    "lenovo" => "https://newsupport.lenovo.com.cn/",
                    "dell" => "https://www.dell.com/support/home/zh-cn",
                    "hp" => "https://support.hp.com/cn-zh",
                    _ => "https://www.asus.com.cn/support/",
                };
            }
            catch { }
            SafeUrl.Open(url);
        }

        // ===== 辅助 =====

        private void SetButton(Button btn, bool enabled)
        {
            btn.IsEnabled = enabled;
            btn.Opacity = enabled ? 1.0 : 0.6;
        }
    }
}
