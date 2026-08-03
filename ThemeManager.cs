using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WINHELP
{
    /// <summary>
    /// 主题配置数据
    /// </summary>
    public class ThemeConfig
    {
        public string AccentColorHex { get; set; } = "#4A90D9"; // 默认蓝
        public string BackgroundImagePath { get; set; } = "";
        /// <summary>背景图透明度 0-1，默认 1.0（不透明）</summary>
        public double BackgroundOpacity { get; set; } = 1.0;
        /// <summary>玻璃强度 0.4-0.9，默认 0.65</summary>
        public double GlassStrength { get; set; } = 0.65;
        /// <summary>玻璃模式：Translucent（半透明叠加）/ Acrylic（图片模糊）</summary>
        public string GlassMode { get; set; } = "Translucent";
        /// <summary>预设主题 key：default/blue/green/orange/purple/pink/teal/starry</summary>
        public string PresetKey { get; set; } = "default";
        /// <summary>全局界面字体（中文常用美观字体），默认微软雅黑</summary>
        public string FontFamilyName { get; set; } = "Microsoft YaHei";

        /// <summary>是否跟随系统深浅色（P1-7）</summary>
        public bool FollowSystem { get; set; } = false;
    }

    /// <summary>
    /// 主题预设。
    /// IsStarry=true 时表示这是"星空"渐变主题，会用渐变 + 星点作为窗口背景，AccentColor 取深色。
    /// </summary>
    public record ThemePreset(string Key, string Name, string ColorHex, bool IsStarry = false);

    /// <summary>
    /// 玻璃效果模式。
    /// Translucent: 半透明白色叠加，背景图保持清晰。
    /// Acrylic: 背景图被模糊（BlurEffect），玻璃面板降到 ~0.15 白透以透出模糊的图。
    /// </summary>
    public enum GlassMode { Translucent, Acrylic }

    /// <summary>
    /// 全局主题管理器 — 单例
    /// </summary>
    public static class ThemeManager
    {
        private static readonly string ConfigDir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "theme.json");

        public static Color AccentColor { get; private set; } = Color.FromRgb(0x4A, 0x90, 0xD9);
        public static string BackgroundImagePath { get; private set; } = "";

        // ===== 星空主题状态 =====
        /// <summary>当前是否是星空主题（深色渐变背景）</summary>
        public static bool IsStarryActive { get; set; } = false;

        /// <summary>是否开启「跟随系统深浅色」（P1-7）</summary>
        public static bool FollowSystem { get; private set; } = false;
        /// <summary>星空主题预设（已选中的那个）</summary>
        public static string ActivePresetKey { get; set; } = "default";

        // ===== 字体 =====
        /// <summary>当前全局界面字体名（中文常用美观字体），默认微软雅黑</summary>
        public static string FontFamilyName { get; private set; } = "Microsoft YaHei";

        // ===== 玻璃化新增属性 =====
        /// <summary>背景图不透明度（0-1，1=完全不透明）。用户可在主题设置中拖动滑块调节。</summary>
        public static double BackgroundOpacity { get; private set; } = 1.0;

        /// <summary>玻璃面板不透明度（0.4-0.9）。值越大玻璃越"实"（更白），值越小越"透"。</summary>
        public static double GlassStrength { get; private set; } = 0.65;

        /// <summary>玻璃效果模式：Translucent（默认，图片清晰） / Acrylic（图片模糊）</summary>
        public static GlassMode GlassEffect { get; private set; } = GlassMode.Translucent;

        /// <summary>
        /// 全局共享的主题色画刷（可变）。所有引用它的控件会随颜色变化"即时"同步，
        /// 无需依赖事件分发 —— 这是修复"换色后只有单一模块生效"的核心机制。
        /// </summary>
        public static SolidColorBrush AccentBrush { get; } = new SolidColorBrush(AccentColor);

        /// <summary>主题色 hover 画刷（共享，可变）</summary>
        public static SolidColorBrush DarkerBrush { get; } = new SolidColorBrush(DarkerColor);

        /// <summary>主题色 press 画刷（共享，可变）</summary>
        public static SolidColorBrush DarkestBrush { get; } = new SolidColorBrush(DarkestColor);

        // ===== 玻璃化共享画刷单例（核心架构） =====
        // 与 AccentBrush 同样机制：所有引用者持有同一实例，修改 .Color 即全窗实时刷新。
        // 关键：必须用 DynamicResource 引用（XAML 中），Style 模板里硬编码颜色则不生效。

        /// <summary>背景画刷：图片（ImageBrush）或纯色（SolidColorBrush），单例缓存。Opacity 受 BackgroundOpacity 控制。</summary>
        public static Brush BackgroundBrush { get; private set; } = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5));

        /// <summary>玻璃卡片背景：白色 0.65-0.92 alpha（根据有图/无图自适应）。可写，便于 ApplyGlass 重建新实例。</summary>
        public static SolidColorBrush GlassCardBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF));

        /// <summary>玻璃面板背景（顶栏、状态栏等大区块）</summary>
        public static SolidColorBrush GlassPanelBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));

        /// <summary>侧边栏背景</summary>
        public static SolidColorBrush GlassSidebarBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));

        /// <summary>顶栏背景（与 GlassPanel 区分，Acrylic 模式下更透）</summary>
        public static SolidColorBrush GlassTopBarBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));

        /// <summary>小药丸背景（状态条、搜索框）</summary>
        public static SolidColorBrush GlassPillBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));

        /// <summary>搜索框背景</summary>
        public static SolidColorBrush GlassSearchBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));

        /// <summary>导航项 hover 背景</summary>
        public static SolidColorBrush GlassNavHoverBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x22, 0x4A, 0x90, 0xD9));

        /// <summary>导航项激活背景（淡主题色高亮）</summary>
        public static SolidColorBrush GlassNavActiveBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x40, 0x4A, 0xD0, 0xF5));

        /// <summary>语义文字画刷（O2）：正文主色。星空深色模式下翻为浅色，保证侧边栏可读。</summary>
        public static SolidColorBrush TextPrimaryBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));

        /// <summary>语义文字画刷（O2）：次要文字色。</summary>
        public static SolidColorBrush TextSecondaryBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));

        /// <summary>语义文字画刷（O2）：弱文字色（副标题/提示）。</summary>
        public static SolidColorBrush TextMutedBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6));

        /// <summary>语义图标/强调文字色（O2）。</summary>
        public static SolidColorBrush IconBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A));

        /// <summary>仅用于「直接绘制在深色背景上（非玻璃卡片）」的文字（如首页分组标题、装扮页标题）。
        /// 星空/极光深色背景下翻为浅色，其余情况为深灰，保证在各自背景上均清晰可读。</summary>
        public static SolidColorBrush TextOnDarkBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));

        /// <summary>是否设置了自定义背景壁纸（星空主题视为"有底色"但不算"有图片"）</summary>
        public static bool HasBackgroundImage
            => !IsStarryActive && !string.IsNullOrEmpty(BackgroundImagePath) && File.Exists(BackgroundImagePath);

        /// <summary>按钮文字前景色：有壁纸时用柔和珍珠白（比纯白更优雅），无壁纸（含星空/极光深色背景）时纯白</summary>
        public static Color ButtonForegroundColor
            => HasBackgroundImage ? Color.FromRgb(0xEA, 0xEC, 0xF0) : Colors.White;

        /// <summary>透明图标按钮文字色：有壁纸时浅色（在半透明底上清晰可读），无壁纸（含星空/极光）时灰色</summary>
        public static Color IconButtonForegroundColor
            => HasBackgroundImage ? Color.FromRgb(0xF0, 0xF2, 0xF5) : Color.FromRgb(0x7F, 0x8C, 0x8D);

        /// <summary>主题变更时触发</summary>
        public static event Action? ThemeChanged;

        /// <summary>玻璃化参数变更时触发（透明度/强度/模式）。MainWindow 收到后切换 BackdropImage 可见性 + InvalidateVisual。</summary>
        public static event Action? GlassChanged;

        public static readonly ThemePreset[] Presets =
        {
            new("default",  "默认蓝",   "#4A90D9"),
            new("green",    "清新绿",   "#27AE60"),
            new("orange",   "暖橙色",   "#E67E22"),
            new("purple",   "暗夜紫",   "#8E44AD"),
            new("pink",     "樱花粉",   "#E91E63"),
            new("teal",     "青碧色",   "#009688"),
            new("starry",   "星空",     "#5A6FE0", IsStarry: true),
            new("aurora",   "极光",     "#7AC7FF", IsStarry: true),
        };

        /// <summary>根据十六进制设置主题色，触发全局刷新</summary>
        public static void SetAccent(string hex)
        {
            AccentColor = (Color)ColorConverter.ConvertFromString(hex);
            // 切到单色时关闭星空状态
            IsStarryActive = false;
            SyncBrushes();
            // 切色时若背景图存在，重建（星空状态变了也可能要变）
            RebuildBackgroundBrush();
            ApplyGlass();
            RaiseThemeChanged();
        }

        /// <summary>应用主题预设（含星空渐变）。会切换主题色、背景样式与玻璃参数。</summary>
        public static void SetPreset(ThemePreset preset)
        {
            if (preset == null) return;
            AccentColor = (Color)ColorConverter.ConvertFromString(preset.ColorHex);
            ActivePresetKey = preset.Key;
            IsStarryActive = preset.IsStarry;
            // 注意：进入星空模式「不再」清空 BackgroundImagePath，仅由 RebuildBackgroundBrush
            // 在 IsStarryActive 时改用渐变背景。保留图片路径，以便切回非星空预设时自动恢复自定义壁纸
            // （修复：原逻辑会把已保存的自定义背景图彻底清除，导致"星空→自定义"后壁纸丢失）。
            SyncBrushes();
            RebuildBackgroundBrush();
            ApplyGlass();
            RaiseThemeChanged();
        }

        /// <summary>设置背景图路径（重建 BackgroundBrush 单例）</summary>
        public static void SetBackground(string imagePath)
        {
            BackgroundImagePath = imagePath;
            RebuildBackgroundBrush();
            // 有图/无图状态变化影响玻璃 alpha，重算玻璃画刷
            ApplyGlass();
            RaiseThemeChanged();
        }

        /// <summary>清除背景图</summary>
        public static void ClearBackground()
        {
            BackgroundImagePath = "";
            RebuildBackgroundBrush();
            ApplyGlass();
            RaiseThemeChanged();
        }

        /// <summary>设置背景图不透明度（0-1）</summary>
        public static void SetBackgroundOpacity(double opacity)
        {
            BackgroundOpacity = Math.Clamp(opacity, 0.0, 1.0);
            // ImageBrush.Opacity 字段直接改
            if (BackgroundBrush is ImageBrush ib) ib.Opacity = BackgroundOpacity;
            RaiseThemeChanged();
        }

        /// <summary>设置玻璃强度（0.4-0.9）</summary>
        public static void SetGlassStrength(double strength)
        {
            GlassStrength = Math.Clamp(strength, 0.4, 0.9);
            ApplyGlass();
            RaiseThemeChanged();
        }

        /// <summary>设置玻璃效果模式（Translucent / Acrylic）</summary>
        public static void SetGlassMode(GlassMode mode)
        {
            GlassEffect = mode;
            ApplyGlass();
            RaiseThemeChanged();
        }

        /// <summary>设置全局界面字体（中文常用美观字体），立即应用并持久化</summary>
        public static void SetFont(string fontFamilyName)
        {
            if (string.IsNullOrWhiteSpace(fontFamilyName)) fontFamilyName = "Microsoft YaHei";
            FontFamilyName = fontFamilyName;
            ApplyFont();
            Save();
        }

        /// <summary>
        /// 将当前 FontFamilyName 应用到整个应用：
        /// - 更新 Application.Resources 的 AppFontFamily（XAML 通过 DynamicResource 引用，实时刷新所有文本）；
        /// - 直接给每个已打开的 Window 设置 FontFamily，确保显式样式按钮等也能继承到。
        /// FontFamily 是可继承属性，设置窗口根后即可级联到所有子元素。
        /// </summary>
        public static void ApplyFont()
        {
            try
            {
                var ff = new FontFamily(FontFamilyName);
                if (Application.Current != null)
                {
                    Application.Current.Resources["AppFontFamily"] = ff;
                    foreach (Window w in Application.Current.Windows)
                    {
                        if (w != null) w.FontFamily = ff;
                    }
                }
            }
            catch { /* 字体名无效时忽略，沿用默认 */ }
        }

        // ===== 跟随系统深浅色（P1-7） =====
        private static bool _systemEventsHooked = false;

        /// <summary>开启/关闭跟随系统深浅色，并立即应用一次</summary>
        public static void SetFollowSystem(bool enable)
        {
            FollowSystem = enable;
            if (enable)
            {
                EnsureSystemEventsHooked();
                ApplySystemTheme();
            }
            Save();
        }

        /// <summary>在首个窗口显示后调用：若已开启则订阅系统主题变化并应用</summary>
        public static void InitFollowSystem()
        {
            if (FollowSystem) EnsureSystemEventsHooked();
        }

        private static void EnsureSystemEventsHooked()
        {
            if (_systemEventsHooked) return;
            try { SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; _systemEventsHooked = true; }
            catch { _systemEventsHooked = false; }
        }

        private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (!FollowSystem || e.Category != UserPreferenceCategory.General) return;
            var app = Application.Current;
            if (app?.Dispatcher != null) app.Dispatcher.InvokeAsync(ApplySystemTheme);
            else ApplySystemTheme();
        }

        /// <summary>根据系统当前深浅色应用对应预设（深色 → 星空，浅色 → 默认蓝）</summary>
        public static void ApplySystemTheme()
        {
            bool dark = IsSystemDarkMode();
            var preset = Presets.FirstOrDefault(p => dark ? p.Key == "starry" : p.Key == "default") ?? Presets[0];
            SetPreset(preset);
        }

        private static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0;
            }
            catch { }
            return false;
        }

        /// <summary>将当前主题色同步到共享画刷（控件引用后随颜色变化即时更新）</summary>
        private static void SyncBrushes()
        {
            AccentBrush.Color = AccentColor;
            DarkerBrush.Color = DarkerColor;
            DarkestBrush.Color = DarkestColor;
        }

        /// <summary>安全地逐个触发主题变更，避免某个模块刷新异常阻断其它模块同步</summary>
        private static void RaiseThemeChanged()
        {
            var handlers = ThemeChanged;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch { /* 单个模块刷新失败不应影响其它模块同步 */ }
            }
        }

        /// <summary>
        /// 重建背景画刷单例（图片变化或初始化时调用）。ImageBrush 的 Opacity 字段会被 SetBackgroundOpacity 维护。
        /// 星空模式：使用深色径向+线性渐变作为背景，叠加随机星点（用 VisualBrush 在外部叠层实现更高效，本方法只生成底色）。
        /// </summary>
        private static void RebuildBackgroundBrush()
        {
            if (IsStarryActive)
            {
                BackgroundBrush = BuildStarryBackground(ActivePresetKey);
                return;
            }
            if (!string.IsNullOrEmpty(BackgroundImagePath) && File.Exists(BackgroundImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // 立即加载，解锁文件
                    bmp.UriSource = new Uri(BackgroundImagePath);
                    bmp.EndInit();
                    bmp.Freeze();
                    BackgroundBrush = new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        Opacity = BackgroundOpacity
                    };
                }
                catch
                {
                    BackgroundBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5));
                }
            }
            else
            {
                BackgroundBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5));
            }
        }

        /// <summary>
        /// 构建"星空"渐变背景画刷。
        /// - 星空（默认）：深蓝紫 → 紫黑，顶部稍亮（模拟夜空中心微光）。
        /// - 极光：深蓝 → 青绿 → 紫，强调层次。
        /// 该画刷是单例缓存，宿主窗口 Background 即可。
        /// </summary>
        public static Brush BuildStarryBackground(string presetKey)
        {
            if (presetKey == "aurora")
            {
                // 极光：深蓝 → 青绿 → 紫
                var lg = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint   = new System.Windows.Point(1, 1)
                };
                lg.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x1B, 0x3A), 0.00));
                lg.GradientStops.Add(new GradientStop(Color.FromRgb(0x16, 0x3A, 0x6B), 0.35));
                lg.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0x86, 0xAB), 0.65));
                lg.GradientStops.Add(new GradientStop(Color.FromRgb(0x4A, 0x2D, 0x7E), 1.00));
                return lg;
            }
            // 默认星空：深蓝紫 → 紫黑
            var lg2 = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint   = new System.Windows.Point(1, 1)
            };
            lg2.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x14, 0x3A), 0.00));
            lg2.GradientStops.Add(new GradientStop(Color.FromRgb(0x1E, 0x24, 0x6E), 0.40));
            lg2.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x1B, 0x5C), 0.75));
            lg2.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x0A, 0x22), 1.00));
            return lg2;
        }

        /// <summary>
        /// 为窗口创建背景 Brush（保持向后兼容，内部返回 BackgroundBrush 单例）。
        /// 注意：用户不应直接调用此方法，应使用 ThemeManager.BackgroundBrush 共享单例。
        /// </summary>
        public static Brush CreateBackgroundBrush()
        {
            // 兼容旧代码：若单例未初始化过（Load 未调用），先重建
            if (BackgroundBrush is SolidColorBrush scb && scb.Color == Color.FromRgb(0xF0, 0xF2, 0xF5) &&
                !string.IsNullOrEmpty(BackgroundImagePath) && File.Exists(BackgroundImagePath))
            {
                RebuildBackgroundBrush();
            }
            return BackgroundBrush;
        }

        /// <summary>将背景 Brush 应用到 Window</summary>
        public static void ApplyWindowBackground(Window window)
        {
            if (window == null) return;
            window.Background = BackgroundBrush;
        }

        /// <summary>将背景 Brush 应用到 Panel</summary>
        public static void ApplyPanelBackground(Panel panel)
        {
            if (panel == null) return;
            panel.Background = BackgroundBrush;
        }

        /// <summary>将背景 Brush 应用到 Border</summary>
        public static void ApplyBorderBackground(Border border)
        {
            if (border == null) return;
            border.Background = BackgroundBrush;
        }

        /// <summary>
        /// 注册玻璃共享画刷到 Application.Current.Resources，
        /// 让 GlassTheme.xaml 中通过 DynamicResource 引用这些键。
        /// 必须在 Application.Run() 之后、首个窗口 Show 之前调用。
        /// </summary>
        public static void RegisterGlassResources()
        {
            // ApplyGlass 会计算玻璃 alpha 并把画刷注册进 Application.Current.Resources，
            // 供 XAML 通过 DynamicResource 引用。必须在首个窗口 Show 之前调用。
            ApplyGlass();
        }

        /// <summary>
        /// 根据 GlassEffect / GlassStrength / HasBackgroundImage / IsStarryActive 计算各玻璃画刷的 Color。
        /// 规则：
        /// - 星空模式：背景深色，玻璃 alpha 显著提高（卡片 0xCC、面板 0xB4）以保证可读性，前景色全部走浅色。
        /// - Acrylic 模式：面板 alpha 降到 ~0x28 (15%)，让背后的模糊图透出；卡片 alpha 略高 ~0x44 (27%) 保证可读。
        /// - Translucent 模式 + 有图：使用 GlassStrength（默认 0.65 = 0xA6）作为卡片 alpha。
        /// - Translucent 模式 + 无图：alpha 提到 ~0xEB (92%)，保证在浅灰底上有清晰边界。
        /// </summary>
        public static void ApplyGlass()
        {
            bool hasImg = HasBackgroundImage;
            bool isAcrylic = GlassEffect == GlassMode.Acrylic;
            bool isStarry = IsStarryActive;

            // 卡片 alpha
            byte cardAlpha;
            if (isStarry)             cardAlpha = 0xCC;  // 80%（深色底需高 alpha）
            else if (isAcrylic && hasImg) cardAlpha = 0x44;  // ~27%（有图模糊）
            else if (!hasImg)        cardAlpha = 0xEB;  // ~92%（无图自适应）
            else                     cardAlpha = (byte)(GlassStrength * 255);

            // 面板 alpha（比卡片低一档，层次感）
            byte panelAlpha;
            if (isStarry)             panelAlpha = 0xB4;  // 70%
            else if (isAcrylic && hasImg) panelAlpha = 0x28;  // ~16%
            else if (!hasImg)        panelAlpha = 0xD8;  // ~85%
            else                     panelAlpha = (byte)(Math.Min(GlassStrength + 0.05, 0.9) * 255);

            // 顶栏：与面板一致
            byte topbarAlpha = panelAlpha;

            // 侧边栏：与面板一致
            byte sidebarAlpha = panelAlpha;

            // 星空/极光模式：背景为深色渐变，但玻璃面板保持浅色（与深色文字形成清晰对比）。
            // 文字画刷始终使用深色，避免「浅字 + 浅卡」导致无法阅读（原 bug）。
            // 侧边栏也保持浅色玻璃，导航文字维持深色，保证可读性。
            GlassSidebarBrush  = new SolidColorBrush(Color.FromArgb(sidebarAlpha, 0xFF, 0xFF, 0xFF));
            TextPrimaryBrush   = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
            TextSecondaryBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            TextMutedBrush     = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6));
            IconBrush          = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A));

            // 仅「直接绘制在深色背景上（非卡片）」的文字（如首页分组标题、装扮页标题）
            // 在星空/极光模式下翻为浅色，其余情况维持深灰，保证在各自背景上均清晰可读。
            TextOnDarkBrush = isStarry
                ? new SolidColorBrush(Color.FromRgb(0xD6, 0xDE, 0xEA))
                : new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));

            // 小药丸 alpha（最透）
            byte pillAlpha;
            if (isStarry)             pillAlpha = 0x90;  // 56%
            else if (isAcrylic && hasImg) pillAlpha = 0x1E;  // ~12%
            else if (!hasImg)        pillAlpha = 0xC0;  // ~75%
            else                     pillAlpha = (byte)(Math.Max(GlassStrength - 0.1, 0.3) * 255);

            // 搜索框：同 pill
            byte searchAlpha = pillAlpha;

            // 每次重建画刷实例：SolidColorBrush 一旦加入 Application.Resources 会被 WPF 冻结，
            // 冻结后无法再修改 .Color（会抛 InvalidOperationException，导致整个程序启动崩溃）。
            // 重建新实例并重新注册到 Resources，XAML 通过 DynamicResource 引用即可自动重新解析，实现实时刷新。
            GlassCardBrush    = new SolidColorBrush(Color.FromArgb(cardAlpha,    0xFF, 0xFF, 0xFF));
            GlassPanelBrush   = new SolidColorBrush(Color.FromArgb(panelAlpha,   0xFF, 0xFF, 0xFF));
            GlassSidebarBrush = new SolidColorBrush(Color.FromArgb(sidebarAlpha, 0xFF, 0xFF, 0xFF));
            GlassTopBarBrush  = new SolidColorBrush(Color.FromArgb(topbarAlpha,  0xFF, 0xFF, 0xFF));
            GlassPillBrush    = new SolidColorBrush(Color.FromArgb(pillAlpha,    0xFF, 0xFF, 0xFF));
            GlassSearchBrush  = new SolidColorBrush(Color.FromArgb(searchAlpha,  0xFF, 0xFF, 0xFF));
            GlassNavHoverBrush  = new SolidColorBrush(Color.FromArgb(0x28, AccentColor.R, AccentColor.G, AccentColor.B));
            GlassNavActiveBrush = new SolidColorBrush(Color.FromArgb(0x40, AccentColor.R, AccentColor.G, AccentColor.B));

            // 重新注册到 Application.Resources（覆盖冻结的旧实例，触发 DynamicResource 重新解析）
            if (Application.Current != null)
            {
                var res = Application.Current.Resources;
                res["GlassCardBrush"]    = GlassCardBrush;
                res["GlassPanelBrush"]   = GlassPanelBrush;
                res["GlassSidebarBrush"] = GlassSidebarBrush;
                res["GlassTopBarBrush"]  = GlassTopBarBrush;
                res["GlassPillBrush"]    = GlassPillBrush;
                res["GlassSearchBrush"]  = GlassSearchBrush;
                res["GlassNavHoverBrush"]   = GlassNavHoverBrush;
                res["GlassNavActiveBrush"]  = GlassNavActiveBrush;
                res["TextPrimaryBrush"]   = TextPrimaryBrush;
                res["TextSecondaryBrush"] = TextSecondaryBrush;
                res["TextMutedBrush"]     = TextMutedBrush;
                res["IconBrush"]          = IconBrush;
                res["TextOnDarkBrush"]    = TextOnDarkBrush;
            }

            // 通知玻璃变更
            var handlers = GlassChanged;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch { /* 单个观察者失败不应影响其他 */ }
            }
        }

        /// <summary>获取比主题色略深的颜色（hover 用）</summary>
        public static Color DarkerColor
        {
            get
            {
                float factor = 0.8f;
                return Color.FromRgb(
                    (byte)(AccentColor.R * factor),
                    (byte)(AccentColor.G * factor),
                    (byte)(AccentColor.B * factor));
            }
        }

        /// <summary>获取比主题色更深的颜色（press 用）</summary>
        public static Color DarkestColor
        {
            get
            {
                float factor = 0.65f;
                return Color.FromRgb(
                    (byte)(AccentColor.R * factor),
                    (byte)(AccentColor.G * factor),
                    (byte)(AccentColor.B * factor));
            }
        }

        /// <summary>对 Button 模板应用主题色（含壁纸感知的前景色）</summary>
        public static void ApplyButtonTheme(Button button, Color bgColor, Color? hoverColor = null, Color? pressColor = null, Color? foregroundColor = null)
        {
            if (button == null) return;

            // 主题色按钮共享全局可变画刷：换色时所有窗口的主题色控件会同时刷新，
            // 即使该窗口未订阅 ThemeChanged 也能同步。
            bool isAccent = bgColor.Equals(AccentColor);
            var normalBrush = isAccent ? AccentBrush  : new SolidColorBrush(bgColor);
            var hoverBrush  = isAccent ? DarkerBrush : new SolidColorBrush(hoverColor ?? DarkerColor);
            var pressBrush  = isAccent ? DarkestBrush : new SolidColorBrush(pressColor ?? DarkestColor);
            foregroundColor ??= ButtonForegroundColor; // 有壁纸时自动切换为柔和珍珠白

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;

            button.Template = template;
            button.Background = normalBrush;
            button.Foreground = new SolidColorBrush(foregroundColor.Value);
            button.BorderThickness = new Thickness(0);
            button.Cursor = Cursors.Hand;

            // 幂等绑定 hover/press 画刷：替换旧处理器，避免每次换色重复叠加事件导致泄漏
            if (_hoverHandlers.TryGetValue(button, out var prev))
            {
                if (prev.Enter != null) button.MouseEnter -= prev.Enter;
                if (prev.Leave != null) button.MouseLeave -= prev.Leave;
            }
            MouseEventHandler enter = (s, e) => button.Background = hoverBrush;
            MouseEventHandler leave = (s, e) => button.Background = normalBrush;
            button.MouseEnter += enter;
            button.MouseLeave += leave;
            _hoverHandlers.Remove(button);
            _hoverHandlers.Add(button, new HoverHandlerSet { Enter = enter, Leave = leave });
        }

        /// <summary>保存每个按钮已绑定的 hover 处理器，便于幂等替换</summary>
        private sealed class HoverHandlerSet
        {
            public MouseEventHandler? Enter;
            public MouseEventHandler? Leave;
        }

        private static readonly ConditionalWeakTable<Button, HoverHandlerSet> _hoverHandlers = new();

        /// <summary>为透明图标按钮（如顶栏的"个性装扮""检查更新""设置"）应用壁纸感知样式。
        /// 有壁纸时：浅色文字 + 半透明白底（毛玻璃质感），确保在任意壁纸上清晰可读；
        /// 无壁纸时：透明底 + 灰色文字（原始风格）。</summary>
        public static void ApplyIconButtonTheme(Button button)
        {
            if (button == null) return;
            // 使用简单模板确保背景色完全可控（默认 Button 模板的 hover 会覆盖背景）
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14)); // 药丸形圆角
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;

            button.Template = template;
            button.BorderThickness = new Thickness(0);
            button.Cursor = System.Windows.Input.Cursors.Hand;
            button.Foreground = new SolidColorBrush(IconButtonForegroundColor);

            if (HasBackgroundImage)
            {
                // 毛玻璃质感：28% 不透明白底，轻盈不喧宾夺主
                button.Background = new SolidColorBrush(Colors.White) { Opacity = 0.28 };
            }
            else
            {
                button.Background = Brushes.Transparent;
            }
        }

        /// <summary>保存配置到文件</summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var config = new ThemeConfig
                {
                    AccentColorHex = AccentColor.ToString(),
                    BackgroundImagePath = BackgroundImagePath,
                    FollowSystem = FollowSystem,
                    BackgroundOpacity = BackgroundOpacity,
                    GlassStrength = GlassStrength,
                    GlassMode = GlassEffect.ToString(),
                    PresetKey = ActivePresetKey,
                    FontFamilyName = FontFamilyName,
                };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config));
            }
            catch { /* 保存失败静默忽略 */ }
        }

        /// <summary>从文件加载配置</summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var config = JsonSerializer.Deserialize<ThemeConfig>(File.ReadAllText(ConfigPath));
                if (config == null) return;

                ActivePresetKey = string.IsNullOrEmpty(config.PresetKey) ? "default" : config.PresetKey;
                // 根据 PresetKey 推导 IsStarryActive（容错旧配置无 PresetKey 的情况）
                IsStarryActive = ActivePresetKey == "starry" || ActivePresetKey == "aurora";
                FollowSystem = config.FollowSystem;

                if (!string.IsNullOrEmpty(config.AccentColorHex))
                {
                    AccentColor = (Color)ColorConverter.ConvertFromString(config.AccentColorHex);
                }
                if (!string.IsNullOrWhiteSpace(config.FontFamilyName))
                {
                    FontFamilyName = config.FontFamilyName;
                }
                // 始终恢复已保存的自定义背景图路径（即使当前预设为星空）。
                // 星空模式下 RebuildBackgroundBrush 会忽略该路径改用渐变背景；
                // 用户切回非星空预设时即可自动恢复壁纸（修复重启后壁纸丢失）。
                if (!string.IsNullOrEmpty(config.BackgroundImagePath) && File.Exists(config.BackgroundImagePath))
                {
                    BackgroundImagePath = config.BackgroundImagePath;
                }
                BackgroundOpacity = Math.Clamp(config.BackgroundOpacity, 0.0, 1.0);
                GlassStrength     = Math.Clamp(config.GlassStrength, 0.4, 0.9);
                if (Enum.TryParse<GlassMode>(config.GlassMode, out var mode))
                {
                    GlassEffect = mode;
                }
                SyncBrushes();
                RebuildBackgroundBrush();
                // 画刷注册后再 ApplyGlass（在 RegisterGlassResources 内调用），这里只同步内部状态
            }
            catch { /* 加载失败用默认值 */ }
        }

        /// <summary>为窗口设置应用图标（pack URI 嵌入资源，单文件兼容）。对非 Window 对象静默 no-op，便于 UserControl 复用旧代码。</summary>
        public static void SetWindowIcon(DependencyObject target)
        {
            if (target is not Window window) return;
            try
            {
                window.Icon = new BitmapImage(new Uri("pack://application:,,,/AppIcon.ico"));
            }
            catch { /* 图标加载失败不崩溃 */ }
        }
    }
}
