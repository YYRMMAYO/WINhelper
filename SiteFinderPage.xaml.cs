using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// SiteFinderPage.xaml 的交互逻辑 — 网站检索助手
    /// </summary>
    public partial class SiteFinderPage : UserControl
    {
        /// <summary>静态标志：为 true 时跳过主题订阅（用于启动时初始化版本号）</summary>
        private static bool _skipThemeInit = false;

        public SiteFinderPage()
        {
            InitializeComponent();

            // ===== 版本检测的检测路径 =====
            // 动态设置显示的版本号（来自程序集版本），保证 XAML 占位文本与实际版本永远一致；
            // 然后从"软件版本"文字模块中解析版本号，设置到 UpdateManager.VersionOverride
            // 这样版本检测就以本窗口中显示的版本文字为基准
            try
            {
                TxtSoftwareVersion.Text = $"软件版本 V{UpdateManager.LocalVersion}";
            }
            catch { /* XAML 加载异常时退回占位文本 */ }
            ParseVersionFromText();

            if (!_skipThemeInit)
            {
                ApplyTheme();
                ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
                Localize();
                UiLanguage.Changed += () => Dispatcher.Invoke(Localize);
            }
        }

        /// <summary>语言切换时重新设置所有静态文本</summary>
        private void Localize()
        {
            TxtTitle.Text = UiLanguage.L("🌐 网站与官网", "🌐 Sites & Official");
            BtnBack.Content = UiLanguage.L("← 返回首页", "← Back to Home");
            TxtCommonSites.Text = UiLanguage.L("📌 常用网站", "📌 Common Sites");
            CatVideo.Text = UiLanguage.L("🎬 影音娱乐", "🎬 Video & Entertainment");
            CatSearch.Text = UiLanguage.L("🔍 搜索引擎", "🔍 Search Engines");
            CatSocial.Text = UiLanguage.L("💬 社交社区", "💬 Social & Community");
            CatShop.Text = UiLanguage.L("🛒 购物电商", "🛒 Shopping");
            CatMusic.Text = UiLanguage.L("🎵 音乐", "🎵 Music");
            CatTool.Text = UiLanguage.L("🔧 实用工具", "🔧 Utilities");
            CatNews.Text = UiLanguage.L("📰 资讯学习", "📰 News & Learning");
            TxtOfficialTitle.Text = UiLanguage.L("🏢 软件官网", "🏢 Official Sites");
            CatRec.Text = UiLanguage.L("🎥 录屏 / 录音", "🎥 Rec & Audio");
            CatOffice.Text = UiLanguage.L("📄 办公 / 文档", "📄 Office / Docs");
            CatComm.Text = UiLanguage.L("📞 通讯 / 会议", "📞 Comms / Meet");
            CatCompress.Text = UiLanguage.L("📦 压缩 / 解压", "📦 Archive");
            CatText.Text = UiLanguage.L("✏️ 文本 / 编辑", "✏️ Text / Editor");
            CatImage.Text = UiLanguage.L("🖼️ 截图 / 图像", "🖼️ Capture / Image");
            CatRemote.Text = UiLanguage.L("🔗 远程 / 传输", "🔗 Remote / Transfer");
            TxtSteamTitle.Text = UiLanguage.L("⚡ Steam++ 加速器", "⚡ Steam++ Accelerator");
            TxtSteamDesc.Text = UiLanguage.L("进入 Steam 和 GitHub 前，建议先使用此加速器进行网页加速。",
                "Use this accelerator to speed up web access before opening Steam & GitHub.");
            TxtUpdateTitle.Text = UiLanguage.L("📦 软件更新", "📦 Software Update");
            BtnSteamPP.Content = UiLanguage.L("打开加速器官网", "Open accelerator site");
            BtnUpdate.Content = UiLanguage.L("获取更新", "Get Update");
            TxtPwd.Text = UiLanguage.L("密码：YYRMM", "Password: YYRMM");
        }

        /// <summary>从 TxtSoftwareVersion 文字模块解析版本号并设置到 UpdateManager</summary>
        private void ParseVersionFromText()
        {
            try
            {
                var text = TxtSoftwareVersion?.Text ?? "";
                // 从 "软件版本 V1.5.0" 中提取 "1.5.0"
                var match = Regex.Match(text, @"V(\d+\.\d+\.\d+)");
                if (match.Success)
                {
                    UpdateManager.VersionOverride = match.Groups[1].Value;
                }
            }
            catch { }
        }

        /// <summary>
        /// 启动时调用：创建一个不可见的 SiteFinderPage 实例以初始化版本号。
        /// 不订阅主题事件，不显示窗口，仅触发构造函数中的版本解析逻辑。
        /// </summary>
        public static void EnsureVersionInitialized()
        {
            _skipThemeInit = true;
            try
            {
                _ = new SiteFinderPage();
            }
            catch { }
            finally
            {
                _skipThemeInit = false;
            }
        }

        private void ApplyTheme()
        {
            // 窗口背景
            RootGrid.Background = Brushes.Transparent;

            // 返回按钮 — 灰色（不变）
            ThemeManager.ApplyButtonTheme(BtnBack, Color.FromRgb(0x95, 0xA5, 0xA6),
                hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));

            // Steam++ 按钮 — 暖橙色（不变）
            ThemeManager.ApplyButtonTheme(BtnSteamPP, Color.FromRgb(0xE6, 0x7E, 0x22),
                hoverColor: Color.FromRgb(0xCF, 0x6F, 0x1B));

            // 获取更新按钮 — 主题色
            ThemeManager.ApplyButtonTheme(BtnUpdate, ThemeManager.AccentColor);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}");
            }
        }

        /// <summary>统一处理所有"打开官网"按钮：从 Tag 中读取 URL 并用默认浏览器打开（合并自官网导航模块）。</summary>
        private void OpenSite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
            {
                OpenUrl(url);
            }
        }

        // ===== 影音娱乐 =====
        private void Button_Click_3(object sender, RoutedEventArgs e)   => OpenUrl("https://store.steampowered.com/");
        private void Button_Click_6(object sender, RoutedEventArgs e)   => OpenUrl("https://www.youku.com/");
        private void Button_Click_7(object sender, RoutedEventArgs e)   => OpenUrl("https://www.iqiyi.com/");
        private void Button_Click_8(object sender, RoutedEventArgs e)   => OpenUrl("https://v.qq.com/");

        // ===== 搜索引擎 =====
        private void Button_Click_10(object sender, RoutedEventArgs e)  => OpenUrl("https://www.baidu.com/");
        private void Button_Click_11(object sender, RoutedEventArgs e)  => OpenUrl("https://www.google.com/");
        private void Button_Click_12(object sender, RoutedEventArgs e)  => OpenUrl("https://www.bing.com/");

        // ===== 社交社区 =====
        private void Button_Click_13(object sender, RoutedEventArgs e)  => OpenUrl("https://weibo.com/");
        private void Button_Click_14(object sender, RoutedEventArgs e)  => OpenUrl("https://www.zhihu.com/");
        private void Button_Click_15(object sender, RoutedEventArgs e)  => OpenUrl("https://www.xiaohongshu.com/");
        private void Button_Click_16(object sender, RoutedEventArgs e)  => OpenUrl("https://www.douban.com/");

        // ===== 购物电商 =====
        private void Button_Click_17(object sender, RoutedEventArgs e)  => OpenUrl("https://www.taobao.com/");
        private void Button_Click_18(object sender, RoutedEventArgs e)  => OpenUrl("https://www.jd.com/");
        private void Button_Click_19(object sender, RoutedEventArgs e)  => OpenUrl("https://mobile.yangkeduo.com/");

        // ===== 音乐 =====
        private void Button_Click_20(object sender, RoutedEventArgs e)  => OpenUrl("https://music.163.com/");
        private void Button_Click_21(object sender, RoutedEventArgs e)  => OpenUrl("https://y.qq.com/");

        // ===== 实用工具 =====
        private void Button_Click_22(object sender, RoutedEventArgs e)  => OpenUrl("https://translate.google.com/");
        private void Button_Click_23(object sender, RoutedEventArgs e)  => OpenUrl("https://www.deepl.com/translator");
        private void Button_Click_24(object sender, RoutedEventArgs e)  => OpenUrl("https://pan.baidu.com/");
        private void Button_Click_25(object sender, RoutedEventArgs e)  => OpenUrl("https://www.aliyundrive.com/");

        // ===== 资讯学习 =====
        private void Button_Click_2(object sender, RoutedEventArgs e)   => OpenUrl("https://github.com/");
        private void Button_Click_26(object sender, RoutedEventArgs e)  => OpenUrl("https://36kr.com/");
        private void Button_Click_27(object sender, RoutedEventArgs e)  => OpenUrl("https://sspai.com/");
        private void Button_Click_28(object sender, RoutedEventArgs e)  => OpenUrl("https://www.ithome.com/");
        private void Button_Click_29(object sender, RoutedEventArgs e)  => OpenUrl("https://zh.wikipedia.org/");
        private void Button_Click_39(object sender, RoutedEventArgs e)  => OpenUrl("https://www.icourse163.org/");
        private void Button_Click_40(object sender, RoutedEventArgs e)  => OpenUrl("https://www.guokr.com/");

        // ===== 影音娱乐（补充） =====
        private void Button_Click_30(object sender, RoutedEventArgs e)  => OpenUrl("https://www.mgtv.com/");
        private void Button_Click_31(object sender, RoutedEventArgs e)  => OpenUrl("https://www.ximalaya.com/");

        // ===== 社交社区（补充） =====
        private void Button_Click_41(object sender, RoutedEventArgs e)  => OpenUrl("https://tieba.baidu.com/");

        // ===== 购物电商（补充） =====
        private void Button_Click_32(object sender, RoutedEventArgs e)  => OpenUrl("https://www.vip.com/");
        private void Button_Click_33(object sender, RoutedEventArgs e)  => OpenUrl("https://www.suning.com/");

        // ===== 音乐（补充） =====
        private void Button_Click_34(object sender, RoutedEventArgs e)  => OpenUrl("https://www.kugou.com/");
        private void Button_Click_35(object sender, RoutedEventArgs e)  => OpenUrl("https://music.migu.cn/");

        // ===== 实用工具（补充） =====
        private void Button_Click_36(object sender, RoutedEventArgs e)  => OpenUrl("https://weixin.qq.com/");

        // 微信输入法（办公��具）
        private void BtnWeixinInput_Click(object sender, RoutedEventArgs e) => OpenUrl("https://z.weixin.qq.com/");

        private void Button_Click_37(object sender, RoutedEventArgs e)  => OpenUrl("https://www.dingtalk.com/");
        private void Button_Click_38(object sender, RoutedEventArgs e)  => OpenUrl("https://docs.qq.com/");

        // ===== 右侧面板 =====
        private void Button_Click_1(object sender, RoutedEventArgs e)   => OpenUrl("https://steampp.net/");
        private void Button_Click_5(object sender, RoutedEventArgs e)   => OpenUrl("https://wwbpq.lanzouu.com/b01d71xtzg");
    }
}
