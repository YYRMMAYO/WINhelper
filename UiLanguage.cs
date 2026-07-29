using System.IO;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// UI 语言（中文 / 英文）全局管理 — 单例，持久化到 AppData/WINHELP/lang.json
    /// 用于「系统状况 / 硬件识别」面板的中英文切换，切换后重新渲染以消除中文缺字/乱码问题。
    /// </summary>
    public enum Lang { Zh, En }

    public static class UiLanguage
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "lang.json");

        public static Lang Current { get; private set; } = Lang.Zh;

        /// <summary>语言切换时触发（窗口据此重新渲染文本）</summary>
        public static event Action? Changed;

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = JsonSerializer.Deserialize<LangConfig>(File.ReadAllText(ConfigPath));
                    if (cfg != null) Current = cfg.Lang;
                }
            }
            catch { /* 失败时回退中文 */ }
        }

        /// <summary>切换语言并持久化，触发 Changed 事件</summary>
        public static void Set(Lang lang)
        {
            if (Current == lang) return;
            Current = lang;
            Save();
            Changed?.Invoke();
        }

        /// <summary>按当前语言返回文本：中文 / 英文</summary>
        public static string L(string zh, string en) => Current == Lang.En ? en : zh;

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new LangConfig { Lang = Current }));
            }
            catch { /* 保存失败静默忽略 */ }
        }

        private sealed class LangConfig
        {
            public Lang Lang { get; set; } = Lang.Zh;
        }
    }
}
