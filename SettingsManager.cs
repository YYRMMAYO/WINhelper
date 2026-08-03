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

        // ===== 首页智能排序 / 收藏（P0-3） =====
        public Dictionary<string, int> RecentModules { get; set; } = new();
        public List<string> StarredModules { get; set; } = new();

        // ===== 首页 NEW 徽标「点击一次后永久隐藏」集合（跨版本保留，不再随升级恢复） =====
        public List<string> DismissedNewModules { get; set; } = new();

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
                // 兼容旧配置：确保集合字段不为 null
                if (Current.RecentModules == null) Current.RecentModules = new();
                if (Current.StarredModules == null) Current.StarredModules = new();
                if (Current.DismissedNewModules == null) Current.DismissedNewModules = new();
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

        // ===== 首页智能排序 / 收藏（P0-3） =====

        /// <summary>记录模块使用次数（用于首页智能排序），变化立即落盘</summary>
        public static void RecordModuleUsage(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!Current.RecentModules.ContainsKey(key)) Current.RecentModules[key] = 0;
            Current.RecentModules[key]++;
            Save();
        }

        /// <summary>该模块是否已被收藏</summary>
        public static bool IsStarred(string key) => !string.IsNullOrEmpty(key) && Current.StarredModules.Contains(key);

        /// <summary>切换模块收藏状态</summary>
        public static void ToggleStar(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var list = Current.StarredModules;
            if (list.Contains(key)) list.Remove(key);
            else list.Add(key);
            Save();
        }

        // ===== 首页 NEW 徽标「点击一次后永久隐藏」（跨版本保留） =====

        /// <summary>该模块的新品徽标是否已被用户点击 dismiss（永久不再显示）</summary>
        public static bool IsNewDismissed(string key) =>
            !string.IsNullOrEmpty(key) && Current.DismissedNewModules.Contains(key);

        /// <summary>永久隐藏某模块的 NEW 徽标（点击一次即生效，跨版本保留）</summary>
        public static void DismissNew(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!Current.DismissedNewModules.Contains(key))
            {
                Current.DismissedNewModules.Add(key);
                Save();
            }
        }

        // ===== 设置导入 / 导出（New E：换机迁移） =====

        /// <summary>将当前设置导出到指定文件（复制 settings.json）</summary>
        public static void ExportSettings(string path)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (File.Exists(ConfigPath)) File.Copy(ConfigPath, path, true);
            }
            catch { /* 导出失败静默忽略 */ }
        }

        /// <summary>从指定文件导入设置；成功返回 true 并落盘。
        /// 安全：导入前做字段校验，拒绝恶意/损坏的配置文件
        /// （如非法热键、非法日期、越界值等），避免被篡改的 JSON 静默改动行为（安全审计建议 P2）。</summary>
        public static bool ImportSettings(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s == null) return false;

                // —— 字段校验（不合法则拒绝导入） ——
                if (string.IsNullOrEmpty(s.SchedulerTime) ||
                    !TimeSpan.TryParse(s.SchedulerTime, out _))
                    return false;

                if (s.SchedulerDayOfWeek is < -1 or > 6)
                    return false;

                if (s.LauncherHotkeyVk is <= 0 or > 0xFF)
                    return false;

                if (s.CompanionHotkeyVk is < 0 or > 0xFF)
                    return false;

                if (s.RecentModules == null) s.RecentModules = new();
                if (s.StarredModules == null) s.StarredModules = new();
                if (s.DismissedNewModules == null) s.DismissedNewModules = new();
                if (s.VersionHistory == null) s.VersionHistory = new();

                // 日期字段：default 表示从未使用，校验合理性
                if (s.FirstUse != default && s.FirstUse > DateTime.Now.AddMinutes(5))
                    return false;
                if (s.LastOptimize != default && s.LastOptimize > DateTime.Now.AddMinutes(5))
                    return false;
                if (s.LastUsageDate != default && s.LastUsageDate > DateTime.Now.AddMinutes(5))
                    return false;

                if (s.OptimizeCount < 0 || s.CleanedBytes < 0 || s.UsageStreak < 0)
                    return false;

                Current = s;
                Save();
                return true;
            }
            catch { return false; }
        }
    }
}
