using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// 版本历史记录项 — 每次安装/升级写入一条，确保更新后不丢失原版本信息
    /// </summary>
    public class VersionRecord
    {
        public string Version { get; set; } = "";
        public DateTime InstalledAt { get; set; } = default;
    }

    /// <summary>
    /// 应用设置数据
    /// </summary>
    public class AppSettings
    {
        public bool AutoStart { get; set; } = false;
        public bool AutoCheckUpdate { get; set; } = true;
        public bool CloseToTray { get; set; } = true;

        // ===== 启动器（N1） =====
        /// <summary>启动器全局热键修饰键位（RegisterHotKey MOD_* 位掩码），默认 Ctrl</summary>
        public int LauncherHotkeyModifiers { get; set; } = 0x0002;
        /// <summary>启动器全局热键虚拟键码，默认 `（0xC0）</summary>
        public int LauncherHotkeyVk { get; set; } = 0xC0;

        // ===== 定时计划（N5） =====
        public bool SchedulerEnabled { get; set; } = false;
        /// <summary>0=周日 ~ 6=周六</summary>
        public int SchedulerDayOfWeek { get; set; } = 0;
        /// <summary>触发时间 HH:mm</summary>
        public string SchedulerTime { get; set; } = "03:00";

        // ===== 还原点（N7） =====
        public bool RestorePointEnabled { get; set; } = true;

        // ===== 隐私痕迹（N13） =====
        public bool PrivacyCleanEnabled { get; set; } = false;

        // ===== 语言（N16） =====
        public string Language { get; set; } = "zh";

        // ===== 陪伴运行全局热键（用户自注册） =====
        /// <summary>陪伴运行全局热键修饰键位（RegisterHotKey MOD_* 位掩码），0 表示未自定义（使用默认回退）</summary>
        public int CompanionHotkeyModifiers { get; set; } = 0;
        /// <summary>陪伴运行全局热键虚拟键码，0 表示未自定义（使用默认回退）</summary>
        public int CompanionHotkeyVk { get; set; } = 0;

        /// <summary>是否已自定义陪伴运行热键（需同时具备修饰键与主键）</summary>
        public bool HasCustomCompanionHotkey => CompanionHotkeyModifiers != 0 && CompanionHotkeyVk != 0;

        // ===== 版本历史（升级后保留原版本信息） =====
        public List<VersionRecord> VersionHistory { get; set; } = new();

        // ===== 月度报告 / 成就（N15） =====
        public DateTime FirstUse { get; set; } = default;
        public DateTime LastOptimize { get; set; } = default;
        public int OptimizeCount { get; set; } = 0;
        public long CleanedBytes { get; set; } = 0;
        public int UsageStreak { get; set; } = 0;
        public DateTime LastUsageDate { get; set; } = default;
    }

    /// <summary>
    /// 全局设置管理器 — 单例
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

        private const string RUN_REGISTRY_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "WINHELP";

        public static AppSettings Current { get; private set; } = new();

        /// <summary>从文件加载设置</summary>
        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                        Current = settings;
                }
            }
            catch { /* 加载失败使用默认值 */ }
        }

        /// <summary>保存设置到文件</summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current));
            }
            catch { /* 保存失败静默忽略 */ }
        }

        /// <summary>设置/取消开机自动启动（注册表 HKCU\Run）</summary>
        public static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_REGISTRY_KEY, true);
                if (enable)
                {
                    var exePath = Environment.ProcessPath
                        ?? Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
                    key?.SetValue(APP_NAME, $"\"{exePath}\"");
                }
                else
                {
                    key?.DeleteValue(APP_NAME, false);
                }
            }
            catch { /* 注册表操作失败静默忽略 */ }
        }

        /// <summary>查询是否已设置开机启动</summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_REGISTRY_KEY, false);
                return key?.GetValue(APP_NAME) != null;
            }
            catch { return false; }
        }
    }
}
