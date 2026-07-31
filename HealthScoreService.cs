using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WINHELP
{
    /// <summary>
    /// 系统健康分服务（New A）：汇总 CPU / 内存 / 系统盘剩余空间 / 开机启动项，
    /// 计算 0–100 综合评分，并给出可执行建议。全部使用已有只读数据源，不修改系统。
    /// </summary>
    public static class HealthScoreService
    {
        public record HealthResult(int Score, string Grade, string Summary, List<string> Suggestions);

        public static HealthResult Compute()
        {
            float cpu = SampleCounter("Processor", "% Processor Time", "_Total");
            float mem = SampleCounter("Memory", "% Committed Bytes In Use", "");
            float diskFree = DiskFreePercent();
            int startup = CountStartupItems();

            double score = 100;
            var suggestions = new List<string>();

            if (cpu >= 85) { score -= 22; suggestions.Add("CPU 占用偏高，建议关闭高占用程序或检查后台进程。"); }
            else if (cpu >= 60) { score -= 9; }

            if (mem >= 85) { score -= 22; suggestions.Add("内存占用偏高，建议关闭不常用的内存大户程序。"); }
            else if (mem >= 60) { score -= 9; }

            if (diskFree < 10) { score -= 25; suggestions.Add("系统盘剩余空间不足 10%，建议运行「系统清理」释放空间。"); }
            else if (diskFree < 20) { score -= 12; suggestions.Add("系统盘剩余空间偏紧，可适时清理临时文件。"); }

            if (startup > 12) { score -= 10; suggestions.Add("开机启动项较多，可在「启动项」中禁用非必要项以加快开机。"); }
            else if (startup > 8) { score -= 5; }

            int s = (int)Math.Clamp(score, 0, 100);
            string grade = s >= 85 ? "优秀" : s >= 70 ? "良好" : s >= 50 ? "一般" : "较差";
            string summary = s >= 85 ? "系统状态很棒，继续保持～"
                            : s >= 70 ? "系统运行良好，无明显瓶颈。"
                            : s >= 50 ? "系统略有压力，可参考下方建议优化。"
                            : "系统压力较大，建议尽快优化。";
            return new HealthResult(s, grade, summary, suggestions);
        }

        private static float SampleCounter(string category, string counter, string instance)
        {
            try
            {
                using var pc = string.IsNullOrEmpty(instance)
                    ? new PerformanceCounter(category, counter)
                    : new PerformanceCounter(category, counter, instance);
                pc.NextValue(); // 预热，避免首读为 0
                return pc.NextValue();
            }
            catch { return -1; }
        }

        private static float DiskFreePercent()
        {
            try
            {
                var drive = new DriveInfo(Environment.GetFolderPath(Environment.SpecialFolder.System));
                if (drive.TotalSize == 0) return 100;
                return (float)(drive.TotalFreeSpace * 100.0 / drive.TotalSize);
            }
            catch { return 100; }
        }

        private static int CountStartupItems()
        {
            int n = 0;
            try
            {
                foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (key != null) n += key.ValueCount;
                }
            }
            catch { }
            return n;
        }
    }
}
