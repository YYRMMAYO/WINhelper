using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// 首页（v5.3.0 重写）：
    /// - 顶部轻量欢迎条（问候 + 上次优化 / 可清理统计 + 一键优化）；
    /// - 下方按「系统工具 / 效率工具 / 助手与信息」三组展示功能卡片；
    /// - 卡片图标统一为「标题首字徽标」（无 emoji），支持收藏星标与使用频率排序；
    /// - 全部数据来自 ModuleRegistry（单一事实源）。
    /// </summary>
    public partial class HomePage : UserControl
    {
        /// <summary>导航请求（由 MainWindow 注入，参数为页面 key）</summary>
        public Action<string>? OnNavigate;

        /// <summary>一键优化请求（由 MainWindow 注入，直接触发顶部「一键优化」逻辑）</summary>
        public Action? OnOptimize;

        // 卡片：Border + key + 所属分组面板 + 排序号 + 首字徽标文本（用于随语言切换）
        private readonly List<(Border card, string key, WrapPanel panel, int order, TextBlock chip)> _cards = new();

        // 收藏星标附加标记：用于区分卡片内嵌的星标按钮点击与卡片导航点击
        public static readonly DependencyProperty IsStarProperty =
            DependencyProperty.RegisterAttached("IsStar", typeof(bool), typeof(HomePage), new PropertyMetadata(false));

        // v5.4.0：首页统计短缓存（60s 内不重扫磁盘；一键优化完成/语言切换时强制刷新）
        private DateTime _statsCacheAt = DateTime.MinValue;
        private string _statsCacheText = "";

        public HomePage()
        {
            InitializeComponent();
            BuildCards();
            ApplySort();
            Localize();
            RefreshStats();
            ThemeWelcomeButton();
            UiLanguage.Changed += () => Dispatcher.Invoke(Localize);
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ThemeWelcomeButton);
        }

        /// <summary>依据 ModuleRegistry 生成首页模块卡片（首字徽标 + 统一尺寸）。</summary>
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

                var card = BuildCard(m, out var chipText);
                WrapCardContent(card, m.Key);

                int order = m.HomeGroup == "system" ? orderSys++
                           : m.HomeGroup == "tools" ? orderTools++
                           : orderAssist++;
                _cards.Add((card, m.Key, panel, order, chipText));
                panel.Children.Add(card);
            }
        }

        /// <summary>生成单张首页卡片：首字徽标（随语言切换）+ 标题 + 副标题。</summary>
        private Border BuildCard(ModuleDefinition m, out TextBlock chipText)
        {
            // 首字徽标（无 emoji；中文取标题首字，英文取标题首字母大写）
            var chip = new Border { Style = (Style)FindResource("IconChip") };
            chipText = new TextBlock
            {
                Text = FirstChar(m.TitleZh),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeManager.AccentColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            chip.Child = chipText;

            var title = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 3),
            };
            var sub = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };

            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var sp = new StackPanel();
            sp.Children.Add(chip);
            sp.Children.Add(title);
            sp.Children.Add(sub);

            var border = new Border
            {
                Tag = m.Key,
                Style = (Style)FindResource("HomeCard"),
            };
            border.MouseLeftButtonDown += Card_Click;
            border.Child = sp;
            return border;
        }

        /// <summary>把卡片内容包进 Grid，叠加右上角收藏星标。</summary>
        private void WrapCardContent(Border b, string key)
        {
            if (b.Child is not StackPanel sp) return;
            b.Child = null;
            sp.HorizontalAlignment = HorizontalAlignment.Stretch;
            sp.VerticalAlignment = VerticalAlignment.Stretch;

            var grid = new Grid();
            grid.Children.Add(sp);

            bool starred = SettingsManager.IsStarred(key);
            var star = new Button
            {
                Tag = key,
                Width = 24, Height = 24,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 6, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 13,
                Content = starred ? "★" : "☆",
                Foreground = new SolidColorBrush(starred
                    ? Color.FromRgb(0xE8, 0xA3, 0x1D) : Color.FromRgb(0xC6, 0xCC, 0xD4)),
                ToolTip = UiLanguage.L("收藏（置顶显示）", "Star (pin to top)"),
            };
            star.SetValue(IsStarProperty, true);
            star.Click += Star_Click;
            grid.Children.Add(star);

            b.Child = grid;
        }

        /// <summary>按「收藏优先 → 使用频率降序 → 原始顺序」重排每个分组面板内的卡片。</summary>
        public void ApplySort()
        {
            var groups = new[] { CardsWrapSystem, CardsWrapTools, CardsWrapAssist };
            foreach (var panel in groups)
            {
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
                ? Color.FromRgb(0xE8, 0xA3, 0x1D) : Color.FromRgb(0xC6, 0xCC, 0xD4));
            ApplySort(); // 立即重排以反映收藏状态
        }

        /// <summary>语言切换时重新设置所有卡片 / 欢迎条文本</summary>
        private void Localize()
        {
            SecSystem.Text = UiLanguage.L("系统工具", "System Tools");
            SecTools.Text = UiLanguage.L("效率工具", "Efficiency Tools");
            SecAssist.Text = UiLanguage.L("助手与信息", "Assistants & Info");

            foreach (var (card, key, _, _, chip) in _cards)
            {
                var m = ModuleRegistry.Find(key);
                if (m == null) continue;
                GetCardTexts(card, out var title, out var sub);
                if (title != null) title.Text = UiLanguage.L(m.TitleZh, m.TitleEn);
                if (sub != null) sub.Text = UiLanguage.L(m.SubtitleZh, m.SubtitleEn);
                // 首字徽标随语言切换：中文标题首字 / 英文标题首字母
                chip.Text = UiLanguage.Current == Lang.En ? FirstEn(m.TitleEn) : FirstChar(m.TitleZh);
            }
            SetHeroDate();
            TxtHeroGreeting.Text = GetGreeting();
            RefreshStats(true);   // 语言切换强制刷新（缓存文本可能为旧语言）
        }

        /// <summary>刷新欢迎条统计（上次优化 + 可清理量）。后台扫描防 UI 卡顿；60s 内命中缓存不重扫。</summary>
        public void RefreshStats(bool force = false)
        {
            try
            {
                var last = SettingsManager.Current.LastOptimize;
                string lastStr = (last == default) ? UiLanguage.L("从未优化", "Never optimized") : last.ToString("yyyy-MM-dd HH:mm");

                // 短缓存：60 秒内直接复用上次结果（每次切回首页都重扫磁盘是浪费）
                if (!force && (DateTime.Now - _statsCacheAt).TotalSeconds < 60)
                {
                    TxtHeroExtra.Text = _statsCacheText;
                    return;
                }

                // 同步设置"上次优化"部分，可清理量异步计算（扫盘可能耗时）
                _ = Task.Run(() =>
                {
                    long pending;
                    try
                    {
                        pending = Cleaner.SumMatching(Cleaner.TempDirs(), "*", System.IO.SearchOption.TopDirectoryOnly).size
                                + Cleaner.QueryRecycleBin().size;
                    }
                    catch { pending = 0; }
                    var text = $"{UiLanguage.L("上次优化", "Last optimize")}：{lastStr}  ·  " +
                               $"{UiLanguage.L("可清理", "Reclaimable")}：{MainWindow.FmtSize(pending)}";
                    _statsCacheAt = DateTime.Now;
                    _statsCacheText = text;
                    Dispatcher.Invoke(() =>
                    {
                        if (TxtHeroExtra == null) return;
                        TxtHeroExtra.Text = text;
                    });
                });
            }
            catch
            {
                TxtHeroExtra.Text = "";
            }
        }

        /// <summary>一键优化按钮背景随主题强调色变化</summary>
        private void ThemeWelcomeButton()
        {
            if (BtnWelcomeOptimize == null) return;
            ThemeManager.ApplyButtonTheme(BtnWelcomeOptimize, ThemeManager.AccentColor);
        }

        private void BtnWelcomeOptimize_Click(object sender, RoutedEventArgs e) => OnOptimize?.Invoke();

        /// <summary>从卡片容器中找到标题（粗体）与副标题（最后一个 TextBlock）</summary>
        private static void GetCardTexts(Border b, out TextBlock? title, out TextBlock? sub)
        {
            title = null; sub = null;
            var sp = b.Child is Grid g ? g.Children.OfType<StackPanel>().FirstOrDefault() : b.Child as StackPanel;
            if (sp == null) return;
            var tbs = sp.Children.OfType<TextBlock>().ToList();
            title = tbs.FirstOrDefault(tb => tb.FontWeight == FontWeights.SemiBold);
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
            // 点击卡片内嵌的「收藏星标」时不触发导航
            var o = e.OriginalSource as DependencyObject;
            while (o != null)
            {
                if (o is DependencyObject d && (bool)d.GetValue(IsStarProperty)) return;
                o = VisualTreeHelper.GetParent(o);
            }

            if (sender is not Border b || b.Tag is not string key) return;
            SettingsManager.RecordModuleUsage(key); // 记录使用频率

            if (key == "notes")
                OnNavigate?.Invoke("notes");
            else
                OnNavigate?.Invoke(key);
        }

        /// <summary>按关键词显示 / 隐藏卡片（跨三个面板搜索）；某组无匹配时一并隐藏其分组标题。</summary>
        public void Filter(string keyword)
        {
            keyword = (keyword ?? "").Trim().ToLowerInvariant();
            bool sysVisible = false, toolsVisible = false, assistVisible = false;
            foreach (var (card, key, panel, _, _) in _cards)
            {
                var text = (key + " " + GetText(card)).ToLowerInvariant();
                bool show = string.IsNullOrEmpty(keyword) || text.Contains(keyword);
                card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) continue;
                if (panel == CardsWrapSystem) sysVisible = true;
                else if (panel == CardsWrapTools) toolsVisible = true;
                else if (panel == CardsWrapAssist) assistVisible = true;
            }
            SecSystem.Visibility = sysVisible ? Visibility.Visible : Visibility.Collapsed;
            SecTools.Visibility = toolsVisible ? Visibility.Visible : Visibility.Collapsed;
            SecAssist.Visibility = assistVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>按当前语言设置日期（日 / 月）</summary>
        private void SetHeroDate()
        {
            var now = DateTime.Now;
            TxtHeroDay.Text = now.Day.ToString();
            string[] monthEn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            TxtHeroMonth.Text = UiLanguage.Current == Lang.En
                ? monthEn[now.Month - 1]
                : $"{now.Month}月";
        }

        /// <summary>根据当前时段返回问候语（随语言切换）</summary>
        private static string GetGreeting()
        {
            int hour = DateTime.Now.Hour;
            return hour switch
            {
                >= 5 and < 12 => UiLanguage.L("早上好，欢迎回来", "Good morning, welcome back"),
                >= 12 and < 18 => UiLanguage.L("下午好，欢迎回来", "Good afternoon, welcome back"),
                >= 18 and < 23 => UiLanguage.L("晚上好，欢迎回来", "Good evening, welcome back"),
                _ => UiLanguage.L("夜深了，注意休息", "Late night—time to rest")
            };
        }

        private static string FirstChar(string s)
            => string.IsNullOrEmpty(s) ? "" : s[..1];

        /// <summary>英文标题首字母（大写），跳过空格 / 符号等非字母字符；无字母返回空串。</summary>
        private static string FirstEn(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            foreach (char c in s)
                if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
            return "";
        }
    }
}
