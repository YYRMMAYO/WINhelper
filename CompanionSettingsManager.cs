using System.IO;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// 陪伴运行（小窗模式）配置数据
    /// </summary>
    public class CompanionSettings
    {
        /// <summary>用户自定义小窗图片路径（为空则显示占位图）</summary>
        public string ImagePath { get; set; } = "";

        /// <summary>是否链接北京时间（通过 http://time.syiban.com/ 同步）</summary>
        public bool SyncBeijingTime { get; set; } = true;

        /// <summary>小窗是否始终置顶</summary>
        public bool AlwaysOnTop { get; set; } = true;

        /// <summary>是否显示秒</summary>
        public bool ShowSeconds { get; set; } = true;
    }

    /// <summary>
    /// 陪伴运行设置管理器 — 单例，持久化到 %AppData%/WINHELP/companion.json
    /// </summary>
    public static class CompanionSettingsManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "companion.json");

        public static CompanionSettings Current { get; private set; } = new();

        /// <summary>从文件加载设置</summary>
        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var s = JsonSerializer.Deserialize<CompanionSettings>(json);
                    if (s != null) Current = s;
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
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 保存失败静默忽略 */ }
        }
    }
}
