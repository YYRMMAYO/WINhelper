using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace WINHELP
{
    /// <summary>
    /// 系统硬件/设备信息收集 — 通过 WMI 枚举 CPU、显卡、内存、主板、BIOS、操作系统等。
    /// 所有查询均做异常隔离：单项失败不影响其他项展示（该项标记为「不可用」）。
    /// 专门用于「系统状况监测 / 设备型号显示」模块。
    /// 文本随 UiLanguage 切换（中文 / 英文），避免中文缺字/乱码。
    /// </summary>
    public static class HardwareInfo
    {
        /// <summary>单条信息：标签 + 值</summary>
        public sealed class Item
        {
            public string Label { get; init; } = "";
            public string Value { get; init; } = "";
            public bool IsGpu { get; init; }
        }

        /// <summary>异步收集全部系统信息（WMI 较慢，放后台线程执行避免界面卡顿）</summary>
        public static Task<List<Item>> CollectAsync()
            => Task.Run(Collect);

        private static string L(string zh, string en) => UiLanguage.L(zh, en);

        private static List<Item> Collect()
        {
            var list = new List<Item>();

            // ===== 处理器（CPU）=====
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var name = Safe(mo, "Name");
                    var cores = Safe(mo, "NumberOfCores");
                    var logical = Safe(mo, "NumberOfLogicalProcessors");
                    var mhz = Safe(mo, "MaxClockSpeed");
                    var detail = "";
                    if (!string.IsNullOrEmpty(cores) && !string.IsNullOrEmpty(logical))
                        detail = string.Format(L("（{0} 核 / {1} 线程）", " ({0} cores / {1} threads)"), cores, logical);
                    else if (!string.IsNullOrEmpty(logical))
                        detail = string.Format(L("（{0} 线程）", " ({0} threads)"), logical);
                    if (!string.IsNullOrEmpty(mhz))
                        detail += " @ " + FormatMHz(mhz);
                    list.Add(new Item { Label = L("处理器 (CPU)", "Processor (CPU)"), Value = Trim(name) + detail });
                    break; // 多路 CPU 仅显示第一颗
                }
            }
            catch
            {
                list.Add(new Item { Label = L("处理器 (CPU)", "Processor (CPU)"), Value = L("无法读取（WMI 不可用）", "Unreadable (WMI unavailable)") });
            }

            // ===== 显卡（GPU）— 可有多块 =====
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterRAM, DriverVersion, Status, VideoProcessor FROM Win32_VideoController");
                bool any = false;
                foreach (ManagementObject mo in searcher.Get())
                {
                    any = true;
                    var name = Trim(Safe(mo, "Name"));
                    var ram = Safe(mo, "AdapterRAM");
                    var driver = Safe(mo, "DriverVersion");
                    var status = Safe(mo, "Status");
                    var vproc = Safe(mo, "VideoProcessor");

                    var sub = new List<string>();
                    if (!string.IsNullOrEmpty(ram) && long.TryParse(ram, out var bytes) && bytes > 0)
                        sub.Add(string.Format(L("显存 {0}", "VRAM {0}"), FormatBytes(bytes)));
                    if (!string.IsNullOrEmpty(driver))
                        sub.Add(string.Format(L("驱动 {0}", "Driver {0}"), driver));
                    if (!string.IsNullOrEmpty(status) && status != "OK")
                        sub.Add(string.Format(L("状态 {0}", "Status {0}"), status));

                    var sep = L("，", ", ");
                    var val = name;
                    if (sub.Count > 0)
                        val += "  ·  " + string.Join(sep, sub);

                    list.Add(new Item { Label = L("显卡 (GPU)", "Graphics (GPU)"), Value = val, IsGpu = true });
                }
                if (!any)
                    list.Add(new Item { Label = L("显卡 (GPU)", "Graphics (GPU)"), Value = L("未检测到显示适配器", "No display adapter detected"), IsGpu = true });
            }
            catch
            {
                list.Add(new Item { Label = L("显卡 (GPU)", "Graphics (GPU)"), Value = L("无法读取（WMI 不可用）", "Unreadable (WMI unavailable)"), IsGpu = true });
            }

            // ===== 内存（物理）=====
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var mem = Safe(mo, "TotalPhysicalMemory");
                    if (!string.IsNullOrEmpty(mem) && ulong.TryParse(mem, out var bytes))
                        list.Add(new Item { Label = L("安装内存 (RAM)", "Installed RAM"), Value = FormatBytes((long)bytes) });
                    break;
                }
            }
            catch
            {
                list.Add(new Item { Label = L("安装内存 (RAM)", "Installed RAM"), Value = L("无法读取", "Unreadable") });
            }

            // ===== 主板 / 整机型号 =====
            try
            {
                using var cs = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in cs.Get())
                {
                    var manu = Trim(Safe(mo, "Manufacturer"));
                    var model = Trim(Safe(mo, "Model"));
                    var txt = manu;
                    if (!string.IsNullOrEmpty(model) && !model.Equals("System Product Name", System.StringComparison.OrdinalIgnoreCase)
                        && !model.Equals("System Model", System.StringComparison.OrdinalIgnoreCase))
                        txt += (txt.Length > 0 ? " " : "") + model;
                    if (!string.IsNullOrEmpty(txt))
                        list.Add(new Item { Label = L("整机 / 主板", "System / Motherboard"), Value = txt });
                    break;
                }
            }
            catch { /* 非关键 */ }

            try
            {
                using var bb = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                foreach (ManagementObject mo in bb.Get())
                {
                    var manu = Trim(Safe(mo, "Manufacturer"));
                    var prod = Trim(Safe(mo, "Product"));
                    var txt = manu + (manu.Length > 0 ? " " : "") + prod;
                    if (!string.IsNullOrEmpty(txt) && txt != " ")
                        list.Add(new Item { Label = L("主板型号", "Motherboard model"), Value = txt });
                    break;
                }
            }
            catch { /* 非关键 */ }

            // ===== BIOS =====
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, Manufacturer FROM Win32_Bios");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var ver = Trim(Safe(mo, "SMBIOSBIOSVersion"));
                    var manu = Trim(Safe(mo, "Manufacturer"));
                    if (!string.IsNullOrEmpty(ver) || !string.IsNullOrEmpty(manu))
                        list.Add(new Item { Label = L("BIOS", "BIOS"), Value = (manu + " " + ver).Trim() });
                    break;
                }
            }
            catch { /* 非关键 */ }

            // ===== 操作系统 =====
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, OSArchitecture, BuildNumber FROM Win32_OperatingSystem");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var caption = Trim(Safe(mo, "Caption"));
                    var ver = Safe(mo, "Version");
                    var arch = Safe(mo, "OSArchitecture");
                    var build = Safe(mo, "BuildNumber");
                    var txt = caption;
                    var sub = new List<string>();
                    if (!string.IsNullOrEmpty(ver)) sub.Add("v" + ver);
                    if (!string.IsNullOrEmpty(build)) sub.Add(string.Format(L("Build {0}", "Build {0}"), build));
                    if (!string.IsNullOrEmpty(arch)) sub.Add(arch);
                    if (sub.Count > 0) txt += string.Format(L("（{0}）", " ({0})"), string.Join(L("，", ", "), sub));
                    list.Add(new Item { Label = L("操作系统", "Operating System"), Value = txt });
                    break;
                }
            }
            catch
            {
                list.Add(new Item { Label = L("操作系统", "Operating System"), Value = System.Environment.OSVersion.VersionString });
            }

            // ===== .NET 运行时 =====
            try
            {
                var rtVer = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
                list.Add(new Item { Label = L(".NET 运行时", ".NET runtime"), Value = rtVer });
            }
            catch { /* 非关键 */ }

            // ===== 系统盘 =====
            try
            {
                var drive = System.IO.Path.GetPathRoot(System.Environment.SystemDirectory) ?? "C:\\";
                var di = new System.IO.DriveInfo(drive);
                if (di.IsReady)
                {
                    var total = FormatBytes((long)di.TotalSize);
                    var free = FormatBytes((long)di.AvailableFreeSpace);
                    list.Add(new Item { Label = L("系统盘", "System drive"),
                        Value = string.Format(L("{0} · 共 {1} · 可用 {2}", "{0} · Total {1} · Free {2}"),
                            drive.TrimEnd('\\'), total, free) });
                }
            }
            catch { /* 非关键 */ }

            return list;
        }

        // ===== 工具方法 =====

        private static string Safe(ManagementObject mo, string prop)
        {
            try
            {
                var v = mo[prop];
                return Sanitize(v?.ToString());
            }
            catch { return ""; }
        }

        private static string Trim(string s) => Sanitize(s);

        /// <summary>
        /// 清理 WMI 文本：
        /// 1) Unicode 归一化（FormC），修正因组合字符/编码差异导致的“乱码/显示异常”；
        /// 2) 丢弃 NUL、退格、DEL 等不可见控制字符（部分乱码/文字被截断的常见来源）；
        /// 3) 丢弃 U+FFFD 替换字符（已损坏的字符）；
        /// 4) 制表符与各类特殊空格归一为普通空格并压缩多余空白。
        /// </summary>
        private static string Sanitize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // 归一化合合字符，减少跨语言/跨编码显示异常
            string normalized;
            try { normalized = s.Normalize(NormalizationForm.FormC); }
            catch { normalized = s; }

            var sb = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                if (c == '\t' || c == '\n' || c == '\r') { sb.Append(' '); continue; }
                if (c == '\uFFFD') continue;                 // 已损坏的替换字符
                if (char.IsControl(c)) continue;            // 丢弃不可见控制字符
                sb.Append(c);
            }
            var cleaned = sb.ToString()
                .Replace('\u00A0', ' ')  // 不间断空格
                .Replace('\u3000', ' '); // 全角空格
            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned.Trim();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suf = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double d = bytes;
            while (d >= 1024 && i < suf.Length - 1) { d /= 1024; i++; }
            // d < 10 显示 2 位小数，d < 100 显示 1 位小数，更大显示 0 位
            string num = d < 10 ? d.ToString("F2")
                       : d < 100 ? d.ToString("F1")
                       : d.ToString("F0");
            return i == 0 ? $"{bytes} B" : $"{num} {suf[i]}";
        }

        private static string FormatMHz(string mhz)
        {
            if (double.TryParse(mhz, out var v) && v > 0)
                return v >= 1000 ? $"{v / 1000.0:F(2)} GHz" : $"{v:F(0)} MHz";
            return mhz + " MHz";
        }
    }
}
