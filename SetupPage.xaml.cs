using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// SetupPage.xaml 交互逻辑 — 装机助手（导航 key="setup"，新电脑常用软件官网导航）
    /// 所有链接均为软件官方地址，无 360 等捆绑软件。
    /// 由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷、LocExtension 多语言，
    /// 以及 SiteCatalog.cs（软件 / 官网清单的 C# 实例数据）。
    /// </summary>
    public partial class SetupPage : UserControl
    {
        public SetupPage()
        {
            InitializeComponent();
            // 不设置自身背景：Main 窗口已在 RootGrid/PageHost 上应用共享背景画刷
            // （含自定义背景图），本页保持透明，避免叠加第二层导致"嵌套"显示。

            BuildCatalog();
            // 语言切换时重建卡片文本（卡片用代码直写 Text，无 loc:Loc 自动刷新）
            UiLanguage.Changed += () => Dispatcher.Invoke(BuildCatalog);
        }

        /// <summary>依据 SiteCatalog 动态生成各分类标题与软件卡片。</summary>
        private void BuildCatalog()
        {
            if (ContentStack == null) return;
            ContentStack.Children.Clear();

            foreach (var group in SiteCatalog.Groups)
            {
                ContentStack.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L(group.TitleZh, group.TitleEn),
                    Style = (Style)FindResource("GroupHeader")
                });

                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var item in group.Items)
                {
                    wrap.Children.Add(BuildSiteCard(item));
                }
                ContentStack.Children.Add(wrap);
            }
        }

        /// <summary>生成单张软件卡片（与 SiteCatalog 条目对应）。</summary>
        private Border BuildSiteCard(SiteEntry item)
        {
            var sp = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 14, 0)
            };
            sp.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
            });
            sp.Children.Add(new TextBlock
            {
                Text = UiLanguage.L(item.DescZh, item.DescEn),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
                Margin = new Thickness(0, 2, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            var btn = new Button
            {
                Style = (Style)FindResource("SiteOpenButton"),
                Content = UiLanguage.L("打开官网", "Open Website"),
                Tag = item.Url
            };
            btn.Click += OpenSite_Click;
            sp.Children.Add(btn);

            return new Border
            {
                Style = (Style)FindResource("SetupCard"),
                Child = sp
            };
        }

        /// <summary>统一处理所有"打开官网"按钮：从 Tag 读取 URL 并用默认浏览器打开</summary>
        private void OpenSite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
            {
                OpenUrl(url);
            }
        }

        private static void OpenUrl(string url) => SafeUrl.Open(url);
    }
}
