using System;
using System.Collections.Generic;

namespace WINHELP
{
    /// <summary>
    /// 系统急救页（RescuePage）命令目录。
    /// 安全约定：所有 Command 均为编译期字面量，通过 <see cref="EnsureRegistered"/>
    /// 注册为 <see cref="CommandRunner"/> 的精确匹配白名单（复刻 IssueCatalog 模式）。
    /// 注意：命令中不嵌入双引号（CommandRunner 经 cmd /c 包裹，嵌套引号会破坏解析）。
    /// </summary>
    public static class RescueCatalog
    {
        /// <summary>一条急救命令。Command 必须为编译期字面量。</summary>
        public sealed class RescueCommand
        {
            public string Key { get; }
            public string LabelZh { get; }
            public string LabelEn { get; }
            public string Command { get; }
            /// <summary>是否需要管理员权限（未提权时弹 UAC）。</summary>
            public bool RequireAdmin { get; }
            public int TimeoutSec { get; }

            public RescueCommand(string key, string labelZh, string labelEn, string command,
                bool requireAdmin = false, int timeoutSec = 60)
            {
                Key = key;
                LabelZh = labelZh;
                LabelEn = labelEn;
                Command = command;
                RequireAdmin = requireAdmin;
                TimeoutSec = timeoutSec;
            }
        }

        /// <summary>急救命令目录（编译期字面量）。</summary>
        public static readonly IReadOnlyList<RescueCommand> Commands = new RescueCommand[]
        {
            // 蓝屏 / 系统事件分析（只读）
            new("wer_events", "提取崩溃事件", "Crash events",
                "wevtutil qe System /rd:true /c:50 /f:text", false, 30),
            // 电池健康报告
            new("battery", "生成电池报告", "Battery report",
                "powercfg /batteryreport /output %TEMP%\\sinan_battery_report.html", false, 60),
            // 端口占用
            new("ports", "扫描端口占用", "Port usage",
                "netstat -ano", false, 15),
            // 驱动备份（提权：写用户桌面目录）
            new("driver_backup", "备份全部驱动", "Backup drivers",
                "dism /online /export-driver /destination:%USERPROFILE%\\Desktop\\DriverBackup",
                true, 300),
        };

        private static bool _registered;

        /// <summary>把本目录中的全部命令注册进 <see cref="CommandRunner"/> 白名单。幂等，可重复调用。</summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            var cmds = new List<string>();
            foreach (var c in Commands) cmds.Add(c.Command);
            CommandRunner.RegisterAllowed(cmds);
        }

        /// <summary>按 Key 查找命令，不存在返回 null。</summary>
        public static RescueCommand? Find(string key)
        {
            foreach (var c in Commands)
                if (c.Key == key) return c;
            return null;
        }
    }
}
