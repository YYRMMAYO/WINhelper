using System;

namespace WINHELP
{
    /// <summary>
    /// 显示模式级别：Simple = 普通模式（新手，默认，显示通俗解释、隐藏原始命令输出）；
    /// Pro = 专业模式（显示完整技术数据 / 原始信息）。
    /// </summary>
    public enum UiModeLevel { Simple, Pro }

    /// <summary>
    /// 显示模式管理器（v5.0.0 新增）— 单例。
    /// 持久化在 SettingsManager.settings.json 的 Mode 字段（"simple"/"pro"），
    /// 仿 UiLanguage：切换时触发 Changed 事件，各模块据此重渲染通俗/专业文本。
    /// </summary>
    public static class UiMode
    {
        public static UiModeLevel Current { get; private set; } = UiModeLevel.Simple;

        /// <summary>是否专业模式</summary>
        public static bool IsPro => Current == UiModeLevel.Pro;

        /// <summary>模式切换时触发（各页面据此重渲染）</summary>
        public static event Action? Changed;

        /// <summary>从设置加载（在 App.xaml.cs SettingsManager.Load() 之后调用）</summary>
        public static void Load()
        {
            try
            {
                Current = string.Equals(SettingsManager.Current.Mode, "pro", StringComparison.OrdinalIgnoreCase)
                    ? UiModeLevel.Pro : UiModeLevel.Simple;
            }
            catch { Current = UiModeLevel.Simple; }
        }

        /// <summary>切换模式并持久化，触发 Changed 事件</summary>
        public static void Set(UiModeLevel level)
        {
            if (Current == level) return;
            Current = level;
            try
            {
                SettingsManager.Current.Mode = level == UiModeLevel.Pro ? "pro" : "simple";
                SettingsManager.Save();
            }
            catch { /* 保存失败静默忽略 */ }
            Changed?.Invoke();
        }

        /// <summary>切换为对侧模式（供快速切换按钮使用）</summary>
        public static void Toggle() => Set(IsPro ? UiModeLevel.Simple : UiModeLevel.Pro);

        /// <summary>按当前模式返回文本：普通模式文案 / 专业模式文案</summary>
        public static string L(string simple, string pro) => IsPro ? pro : simple;
    }
}
