using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// 全新首页（内嵌在 MainWindow 右侧内容区）：分组功能入口 + 搜索筛选
    /// 侧栏精简后所有功能模块均通过首页卡片访问，分为三组：系统工具 / 效率工具 / 助手与信息。
    /// 新增（P0-3）：智能排序（收藏优先 + 使用频率）、收藏星标、状态徽标（推荐 / NEW）。
    /// <para>导航模块 key="home"（由 MainWindow._factories 注册）；首页卡片由 ModuleRegistry
    /// （C# 实例）驱动，BuildCards 在构造函数里生成卡片并注入星标 / NEW 徽标；
    /// 依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。</para>
    /// </summary>
    public partial class HomePage : UserControl
    {
        /// <summary>导航请求（由 MainWindow 注入，参数为页面 key）</summary>
        public Action<string>? OnNavigate;

        /// <summary>一键优化请求（由 MainWindow 注入，直接触发顶部「一键优化」逻辑）</summary>
        public Action? OnOptimize;

        // 卡片：Border + key + 所属分组面板（用于按组排序）
        private readonly List<(Border card, string key, WrapPanel panel, int order)> _cards = new();

        // 收藏星标附加标记：用于区分卡片内嵌的星标按钮点击与卡片导航点击
        public static readonly DependencyProperty IsStarProperty =
            DependencyProperty.RegisterAttached("IsStar", typeof(bool), typeof(HomePage), new PropertyMetadata(false));

        // 推荐 / 新品徽标集合（可按运营节奏调整）
        private static readonly HashSet<string> Recommended = new() { "clean", "startup", "system", "agent", "snapshot" };
        private static readonly HashSet<string> NewModules = new() { "issue", "report", "uninstall" };

        public HomePage()
        {
            InitializeComponent();
            BuildCards();
            ApplySort();
            Localize();
            ThemeCardAccent();
            UiLanguage.Changed += () => Dispatcher.Invoke(Localize);
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ThemeCardAccent);
        }

        /// <summary>依据 ModuleRegistry 生成首页模块卡片（取代原先写在 XAML 的 Border）。</summary>
        private void BuildCards()
        {
            _cards.Clear();
            var panels = new Dictionary<string, WrapPanel?>
            {
                ["system"] = CardsWrapSystem,
                ["tools"] = CardsWrapTools,
                ["assist"] = CardsWrapAssist,
            };
            int orderSys = 0, orderTools = 0, orderAssist = 0;
            foreach (var m in ModuleRegistry.All)
            {
                if (m.HomeGroup == null) continue;
                if (!panels.TryGetValue(m.HomeGroup, out var panel) || panel == null) continue;

                var card = BuildCard(m);
                WrapCardContent(card, m.Key);

                int order = m.HomeGroup == "system" ? orderSys++
                           : m.HomeGroup == "tools" ? orderTools++
                           : orderAssist++;
                _cards.Add((card, m.Key, panel, order));
                panel.Children.Add(card);
            }
        }

        /// <summary>生成单张首页卡片（主级 / 常规两套样式），结构与原有 XAML 卡片一致。</summary>
        private Border BuildCard(ModuleDefinition m)
        {
            var sp = new StackPanel();
            TextBlock title, sub;

            if (m.IsPrimary)
            {
                var chip = new Border { Style = (Style)FindResource("IconChip") };
                chip.Child = new TextBlock
                {
                    Text = m.Icon,
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                sp.Children.Add(chip);
                title = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) };
                sub = new TextBlock { FontSize = 12 };
            }
            else
            {
                sp.Children.Add(new TextBlock { Text = m.Icon, FontSize = 22, Margin = new Thickness(0, 0, 0, 8) });
                title = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold };
                sub = new TextBlock { FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            }

            // 用 DynamicResource 引用文字画刷，主题切换时实时同步（等价于原 XAML 的 {DynamicResource ...}）
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            sp.Children.Add(title);
            sp.Children.Add(sub);

            var border = new Border
            {
                Tag = m.Key,
                Style = (Style)FindResource(m.IsPrimary ? "HomeCardPrimary" : "HomeCard")
            };
            border.MouseLeftButtonDown += Card_Click;
            border.Child = sp;
            return border;
        }

        /// <summary>把卡片原始 StackPanel 内容包进 Grid，叠加「收藏星标」与「状态徽标」。
        /// 约定：传入 Border 的 Child 必须是承载图标与文字的 StackPanel。</summary>
        private void WrapCardContent(Border b, string key)
        {
            if (b.Child is not StackPanel sp) return;
            // 先断开 sp 与原 Border 的逻辑父子关系，再把它包进 Grid。
            // 否则 grid.Children.Add(sp) 会因 sp 仍属于 Border.Child 而抛
            // InvalidOperationException："指定的元素已经是另一个元素的逻辑子元素"。
            b.Child = null;
            sp.HorizontalAlignment = HorizontalAlignment.Stretch;
            sp.VerticalAlignment = VerticalAlignment.Stretch;

            var grid = new Grid();
            grid.Children.Add(sp);

            // 收藏星标（右上角）
            var star = new Button
            {
                Tag = key,
                Width = 26, Height = 26,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 15,
                Content = SettingsManager.IsStarred(key) ? "★" : "☆",
                Foreground = new SolidColorBrush(SettingsManager.IsStarred(key)
                    ? Color.FromRgb(0xF1, 0xC4, 0x0F) : Color.FromRgb(0xBD, 0xC3, 0xC7)),
            };
            star.SetValue(IsStarProperty, true);
            star.Click += Star_Click;
            grid.Children.Add(star);

            // 状态徽标（右下角）：NEW 优先，其次 推荐。
            // NEW 徽标仅在用户尚未点击 dismiss 时显示（点击一次即永久隐藏，跨版本保留）。
            bool showNew = NewModules.Contains(key) && !SettingsManager.IsNewDismissed(key);
            string? badge = showNew ? "NEW"
                              : (!NewModules.Contains(key) && Recommended.Contains(key)) ? UiLanguage.L("推荐", "Recommended")
                              : null;
            if (badge != null)
            {
                var badgeBorder = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 6, 4),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(7, 2, 7, 2),
                    // 标记该边框为 NEW 徽标，便于点击后从视觉树移除
                    Tag = showNew ? "NewBadge" : null,
                    Background = NewModules.Contains(key)
                        ? new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))
                        : new SolidColorBrush(Color.FromArgb(0x33, ThemeManager.AccentColor.R, ThemeManager.AccentColor.G, ThemeManager.AccentColor.B)),
                };
                var badgeText = new TextBlock
                {
                    Text = badge,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = NewModules.Contains(key) ? Brushes.White : new SolidColorBrush(ThemeManager.AccentColor),
                };
                badgeBorder.Child = badgeText;
                grid.Children.Add(badgeBorder);
            }

            b.Child = grid;
        }

        /// <summary>按「收藏优先 → 使用频率降序 → 原始顺序」重排每个分组面板内的卡片。</summary>
        public void ApplySort()
        {
            var groups = new[] { CardsWrapSystem, CardsWrapTools, CardsWrapAssist };
            foreach (var panel in groups)
            {
                // 保留非卡片子元素（如一键优化卡 CardOptimize）的原始相对位置
                var fixedChildren = panel.Children.Cast<UIElement>()
                    .Where(c => !_cards.Any(x => x.card == c)).ToList();
                var sortable = _cards.Where(c => c.panel == panel).ToList();
                sortable.Sort((a, b) =>
                {
                    bool sa = SettingsManager.IsStarred(a.key), sb = SettingsManager.IsStarred(b.key);
                    if (sa != sb) return sa ? -1 : 1;
                    int ua = SettingsManager.Current.RecentModules.TryGetValue(a.key, out var x) ? x : 0;
                    int ub = SettingsManager.Current.RecentModules.TryGetValue(b.key, out var y) ? y : 0;
                    if (ua != ub) return ub.CompareTo(ua);
                    return a.order.CompareTo(b.order);
                });
                panel.Children.Clear();
                foreach (var f in fixedChildren) panel.Children.Add(f);
                foreach (var s in sortable) panel.Children.Add(s.card);
            }
        }

        private void Star_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string key) return;
            SettingsManager.ToggleStar(key);
            bool starred = SettingsManager.IsStarred(key);
            btn.Content = starred ? "★" : "☆";
            btn.Foreground = new SolidColorBrush(starred
                ? Color.FromRgb(0xF1, 0xC4, 0x0F) : Color.FromRgb(0xBD, 0xC3, 0xC7));
            ApplySort(); // 立即重排以反映收藏状态
        }

        /// <summary>语言切换时重新设置所有卡片 / 英雄文本</summary>
        private void Localize()
        {
            SecSystem.Text = UiLanguage.L("🖥️ 系统工具", "🖥️ System Tools");
            SecTools.Text = UiLanguage.L("⚡ 效率工具", "⚡ Efficiency Tools");
            SecAssist.Text = UiLanguage.L("🤖 助手与信息", "🤖 Assistants & Info");

            foreach (var (card, key, _, _) in _cards)
            {
                var m = ModuleRegistry.Find(key);
                if (m == null) continue;
                GetCardTexts(card, out var title, out var sub);
                if (title != null) title.Text = UiLanguage.L(m.TitleZh, m.TitleEn);
                if (sub != null) sub.Text = UiLanguage.L(m.SubtitleZh, m.SubtitleEn);
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
            var sp = b.Child is Grid g ? g.Children.OfType<StackPanel>().FirstOrDefault() : b.Child as StackPanel;
            if (sp == null) return;
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

        /// <summary>从卡片视觉树中移除 NEW 徽标（点击后即时消失，无需重建整页）</summary>
        private static void RemoveNewBadge(Border card)
        {
            if (card.Child is not Grid g) return;
            for (int i = g.Children.Count - 1; i >= 0; i--)
            {
                if (g.Children[i] is Border bd && bd.Tag as string == "NewBadge")
                {
                    g.Children.RemoveAt(i);
                    break;
                }
            }
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            // 点击卡片内嵌的「收藏星标」时不触发导航
            var o = e.OriginalSource as DependencyObject;
            while (o != null)
            {
                if (o is DependencyObject d && (bool)d.GetValue(IsStarProperty)) return;
                o = VisualTreeHelper.GetParent(o);
            }

            if (sender is not Border b || b.Tag is not string key) return;
            SettingsManager.RecordModuleUsage(key); // 记录使用频率

            // 若该模块的 NEW 徽标尚未被忽略，点击即永久隐藏（跨版本保留）
            if (NewModules.Contains(key) && !SettingsManager.IsNewDismissed(key))
            {
                SettingsManager.DismissNew(key);
                RemoveNewBadge(b);
            }

            if (key == "optimize")
                OnOptimize?.Invoke();
            else if (key == "notes")
                OnNavigate?.Invoke("notes");
            else
                OnNavigate?.Invoke(key);
        }

        /// <summary>按关键词显示 / 隐藏卡片（跨三个面板搜索）；某组无匹配时一并隐藏其分组标题。</summary>
        public void Filter(string keyword)
        {
            keyword = (keyword ?? "").Trim().ToLowerInvariant();
            bool sysVisible = false, toolsVisible = false, assistVisible = false;
            foreach (var (card, key, panel, _) in _cards)
            {
                var text = (key + " " + GetText(card)).ToLowerInvariant();
                bool show = string.IsNullOrEmpty(keyword) || text.Contains(keyword);
                card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) continue;
                if (panel == CardsWrapSystem) sysVisible = true;
                else if (panel == CardsWrapTools) toolsVisible = true;
                else if (panel == CardsWrapAssist) assistVisible = true;
            }
            // 英雄卡片（一键优化）始终显示；无匹配的分组标题隐藏
            SecSystem.Visibility = sysVisible ? Visibility.Visible : Visibility.Collapsed;
            SecTools.Visibility = toolsVisible ? Visibility.Visible : Visibility.Collapsed;
            SecAssist.Visibility = assistVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
