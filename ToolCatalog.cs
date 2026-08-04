using System.Collections.Generic;

namespace WINHELP
{
    /// <summary>
    /// 专业工具目录（ProToolPage 数据源，仿 SiteCatalog 风格）。
    /// 全部为绿色免安装专业工具的官方下载地址（P3 阶段会联网逐条核验真实性）。
    /// 点击经 SafeUrl.Open 打开（编译期常量 URL，仅校验 http/https scheme）。
    /// </summary>
    public sealed record ProTool(
        string Name,
        string DescZh,
        string DescEn,
        string Url,
        string CategoryZh,
        string CategoryEn);

    /// <summary>专业工具目录（静态只读列表）。</summary>
    public static class ToolCatalog
    {
        /// <summary>分类：硬件检测 / 系统分析 / 故障诊断 / 磁盘工具 / 内存测试</summary>
        public static readonly IReadOnlyList<ProTool> All = new ProTool[]
        {
            // —— 硬件检测 ——
            new("HWiNFO", "硬件信息与传感器监控", "Hardware info & sensors",
                "https://www.hwinfo.com/", "硬件检测", "Hardware"),
            new("CrystalDiskInfo", "硬盘健康状态 (SMART)", "Disk health (SMART)",
                "https://crystalmark.info/en/software/crystaldiskinfo/", "硬件检测", "Hardware"),
            new("CPU-Z", "CPU/主板/内存详细信息", "CPU / board / memory details",
                "https://www.cpuid.com/softwares/cpu-z.html", "硬件检测", "Hardware"),
            new("GPU-Z", "显卡信息与传感器", "GPU info & sensors",
                "https://www.techpowerup.com/gpuz/", "硬件检测", "Hardware"),
            // —— 系统分析 ——
            new("Autoruns", "开机自启全量分析 (Sysinternals)", "Autostart analysis (Sysinternals)",
                "https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns", "系统分析", "System"),
            new("Process Explorer", "进程树与句柄分析 (Sysinternals)", "Process tree & handles (Sysinternals)",
                "https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer", "系统分析", "System"),
            // —— 故障诊断 ——
            new("BlueScreenView", "蓝屏转储分析 (NirSoft)", "BSOD dump analysis (NirSoft)",
                "https://www.nirsoft.net/utils/blue_screen_view.html", "故障诊断", "Diagnostics"),
            new("BatteryInfoView", "电池健康与循环次数 (NirSoft)", "Battery health & cycles (NirSoft)",
                "https://www.nirsoft.net/utils/battery_information_view.html", "故障诊断", "Diagnostics"),
            // —— 磁盘工具 ——
            new("WizTree", "磁盘空间可视化分析", "Disk space visualizer",
                "https://www.diskanalyzer.com/", "磁盘工具", "Disk"),
            new("Everything", "全盘秒级文件名搜索", "Instant filename search",
                "https://www.voidtools.com/", "磁盘工具", "Disk"),
            // —— 内存测试 ——
            new("MemTest86", "内存稳定性测试", "Memory stability test",
                "https://www.memtest86.com/", "内存测试", "Memory"),
        };
    }
}
