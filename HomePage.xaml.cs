using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// 全新首页（内嵌在 MainWindow 右侧内容区）：分组功能入口 + 搜索筛选
    /// 侧栏精简后所有功能模块均通过首页卡片访问，分为三组：系统工具 / 效率工具 / 助手与信息
    /// </summary>
    public partial class HomePage : UserControl
    {
        /// <summary>导航请求（由 MainWindow 注入，参数为页面 key）</summary>
        public Action<string>? OnNavigate;

        /// <summary>一键优化请求（由 MainWindow 注入，直接触发顶部「一键优化」逻辑）</summary>
        public Action? OnOptimize;

        private readonly List<(Border card, string key)> _cards = new();

        // 卡片标题 / 副标题的中英对照（按 Tag 索引）
        private static readonly Dictionary<string, (string ZhT, string EnT, string ZhS, string EnS)> CardDict = new()
        {
            // ===== 系统工具（主级卡片） =====
            ["clean"]    = ("系统清理", "System Cleaner", "垃圾 / 大文件 / 磁盘可视化", "Junk / large files / disk treemap"),
            ["startup"]  = ("启动项", "Startup", "禁用开机自启 · 影响评估", "Disable autostart · impact check"),
            ["system"]   = ("系统状况", "System Status", "设备检测 · 进程 · 智能诊断", "Device · processes · smart diagnosis"),
            ["net"]      = ("网络诊断", "Network Diagnostics", "连通性检测与测速", "Connectivity test & speed"),
            // ===== 效率工具 =====
            ["wizard"]   = ("故障向导", "Troubleshoot Wizard", "向导式排查常见问题", "Step-by-step troubleshooting"),
            ["shred"]    = ("文件粉碎", "File Shredder", "安全彻底删除敏感文件", "Securely delete sensitive files"),
            ["snapshot"] = ("截图标注", "Screenshot", "截图并标注编辑", "Capture & annotate"),
            ["uninstall"] = ("卸载残留", "Uninstall Leftovers", "清理软件卸载后的残留", "Clean up leftover files after uninstall"),
            ["notes"]     = ("便签", "Notes", "桌面便签快速记录", "Quick desktop notes"),
            ["recorder"]  = ("录音录像", "Recorder", "麦克风录音与屏幕录像", "Mic recording & screen capture"),
            // ===== 助手与信息 =====
            ["agent"]    = ("Agent 助手", "Agent Assistant", "接入 API 获取 AI 帮助", "Connect API for AI help"),
            ["site"]     = ("网站检索助手", "Site Finder", "常用网站一键直达", "Quick access to common sites"),
            ["tool"]     = ("WIN 助手", "WIN Helper", "实用软件官方下载", "Official downloads"),
            ["nav"]      = ("官网导航", "Official Sites", "常用软件官方直达", "Official software links"),
            ["help"]     = ("电脑帮助", "PC Help", "系统工具与使用技巧", "Tools & tips"),
            ["report"]   = ("月度报告", "Monthly Report", "使用统计与成就", "Usage stats & achievements"),
            ["novice"]   = ("新手导览", "Beginner Guide", "小白也能懂的功能", "Features for beginners"),
            ["tutorial"] = ("AI 密钥教程", "AI Key Tutorial", "申请并填入 AI 密钥", "Get & enter your AI key"),
            ["bug"]      = ("BUG 反馈", "Bug Report", "问题反馈与建议提交", "Report issues & suggestions"),
            ["setup"]    = ("装机助手", "Setup Assistant", "常用软件安装推荐", "Recommended software installer"),
        };

        public HomePage()
        {
            InitializeComponent();
            CollectCards();
            Localize();
            ThemeCardAccent();
            UiLanguage.Changed += () => Dispatcher.Invoke(Localize);
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ThemeCardAccent);
        }

        private void CollectCards()
        {
            // 从三个 WrapPanel 中收集所有卡片
            CollectFromPanel(CardsWrapSystem);
            CollectFromPanel(CardsWrapTools);
            CollectFromPanel(CardsWrapAssist);
        }

        private void CollectFromPanel(WrapPanel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is Border b && b.Tag is string key)
                {
                    _cards.Add((b, key));
                }
            }
        }

        /// <summary>语言切换时重新设置所有卡片 / 英雄文本</summary>
        private void Localize()
        {
            SecSystem.Text = UiLanguage.L("🖥️ 系统工具", "🖥️ System Tools");
            SecTools.Text = UiLanguage.L("⚡ 效率工具", "⚡ Efficiency Tools");
            SecAssist.Text = UiLanguage.L("🤖 助手与信息", "🤖 Assistants & Info");

            foreach (var (card, key) in _cards)
            {
                if (!CardDict.TryGetValue(key, out var c)) continue;
                GetCardTexts(card, out var title, out var sub);
                if (title != null) title.Text = UiLanguage.L(c.ZhT, c.EnT);
                if (sub != null) sub.Text = UiLanguage.L(c.ZhS, c.EnS);
            }
        }

        /// <summary>一键优化卡片背景随主题强调色变化（需求 #7 的延伸：首页强调卡与横幅同色）</summary>
        private void ThemeCardAccent()
        {
            if (CardOptimize == null) return;
            var a = ThemeManager.AccentColor;
            var d = ThemeManager.DarkerColor;
            CardOptimize.Background = new LinearGradientBrush(a, d, new Point(0, 0), new Point(1, 1));
        }

        /// <summary>从卡片容器中找到标题（粗体）与副标题（最后一个 TextBlock）</summary>
        private static void GetCardTexts(Border b, out TextBlock? title, out TextBlock? sub)
        {
            title = null; sub = null;
            if (b.Child is not StackPanel sp) return;
            var tbs = sp.Children.OfType<TextBlock>().ToList();
            title = tbs.FirstOrDefault(tb => tb.FontWeight == FontWeights.Bold);
            sub = tbs.LastOrDefault();
        }

        private static string GetText(DependencyObject obj)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var c = VisualTreeHelper.GetChild(obj, i);
                if (c is TextBlock tb) sb.Append(tb.Text).Append(' ');
                sb.Append(GetText(c));
            }
            return sb.ToString();
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border b || b.Tag is not string key) return;
            if (key == "optimize")
                OnOptimize?.Invoke();
            else if (key == "notes")
                // 便签模块使用独立页面
                OnNavigate?.Invoke("notes");
            else
                OnNavigate?.Invoke(key);
        }

        /// <summary>按关键词显示 / 隐藏卡片（跨三个面板搜索）；某组无匹配时一并隐藏其分组标题。</summary>
        public void Filter(string keyword)
        {
            keyword = (keyword ?? "").Trim().ToLowerInvariant();
            bool sysVisible = false, toolsVisible = false, assistVisible = false;
            foreach (var (card, key) in _cards)
            {
                var text = (key + " " + GetText(card)).ToLowerInvariant();
                bool show = string.IsNullOrEmpty(keyword) || text.Contains(keyword);
                card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) continue;
                if (CardsWrapSystem.Children.Contains(card)) sysVisible = true;
                else if (CardsWrapTools.Children.Contains(card)) toolsVisible = true;
                else if (CardsWrapAssist.Children.Contains(card)) assistVisible = true;
            }
            // 英雄卡片（一键优化）始终显示；无匹配的分组标题隐藏
            SecSystem.Visibility = sysVisible ? Visibility.Visible : Visibility.Collapsed;
            SecTools.Visibility = toolsVisible ? Visibility.Visible : Visibility.Collapsed;
            SecAssist.Visibility = assistVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
