using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// ProToolPage.xaml 交互逻辑 — 专业工具（导航 key="protool"，v4.9.0 新增）。
    /// 内置绿色免安装专业工具官方下载（ToolCatalog 数据源，P3 已联网核验）+ 用户自定义插件
    /// （%APPDATA%/WINHELP/Plugins/*.json，经 PluginManifest.IsValidEntry 校验后展示）。
    /// 点击内置 URL 走 SafeUrl.Open；插件 key 型 Entry 经 OnNavigate 跳内置模块，
    /// URL 型经 SafeUrl.OpenTrusted（白名单）。
    /// </summary>
    public partial class ProToolPage : UserControl
    {
        /// <summary>导航回调（由 ModuleRegistry.CreatePage 注入）</summary>
        public Action<string>? OnNavigate;

        public ProToolPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            Loaded += (_, __) => BuildCatalog();
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
        }

        private void BuildCatalog()
        {
            if (RootPanel.Children.Count > 0) return; // 只构建一次

            // 按分类分组（保持 ToolCatalog 顺序）
            var groups = new List<(string Zh, string En, List<ProTool> Tools)>();
            foreach (var t in ToolCatalog.All)
            {
                var g = groups.FirstOrDefault(x => x.Zh == t.CategoryZh);
                if (g.Zh == null) groups.Add((t.CategoryZh, t.CategoryEn, new List<ProTool> { t }));
                else g.Tools.Add(t);
            }

            foreach (var g in groups)
            {
                RootPanel.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L(g.Zh, g.En),
                    FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 6, 0, 4),
                    Foreground = (Brush)FindResource("TextPrimaryBrush")
                });
                var wp = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                foreach (var t in g.Tools) wp.Children.Add(BuildToolCard(t.Name, t.DescZh, t.DescEn, t.Url));
                RootPanel.Children.Add(wp);
            }

            // ===== 用户插件（经 IsValidEntry 校验）=====
            var plugins = PluginLoader.Plugins;
            if (plugins.Count > 0)
            {
                RootPanel.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("我的插件", "My plugins"),
                    FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 12, 0, 4),
                    Foreground = (Brush)FindResource("TextPrimaryBrush")
                });
                var wp = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                foreach (var p in plugins)
                    wp.Children.Add(BuildToolCard(p.Name, p.Desc, p.Desc, p.Entry, isPlugin: true));
                RootPanel.Children.Add(wp);
            }
        }

        private Border BuildToolCard(string name, string descZh, string descEn, string entry, bool isPlugin = false)
        {
            var card = new Border
            {
                Style = (Style)FindResource("GlassCard"),
                Width = 240, Margin = new Thickness(0, 0, 10, 10),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = entry
            };
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock
            {
                Text = name, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            });
            sp.Children.Add(new TextBlock
            {
                Text = UiLanguage.L(descZh, descEn),
                FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });
            sp.Children.Add(new TextBlock
            {
                Text = isPlugin ? UiLanguage.L("插件 · 点击打开", "Plugin · click to open")
                                : UiLanguage.L("官方下载 ↗", "Official ↗"),
                FontSize = 10, Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(ThemeManager.AccentColor)
            });
            card.Child = sp;
            card.MouseLeftButtonUp += (_, __) => OpenEntry(entry, isPlugin);
            return card;
        }

        private void OpenEntry(string entry, bool isPlugin)
        {
            if (string.IsNullOrWhiteSpace(entry)) return;
            if (isPlugin)
            {
                // 插件入口：内置模块 key → 跳转；否则必须为可信 https（IsValidEntry 已校验）
                if (ModuleRegistry.Find(entry) != null)
                {
                    OnNavigate?.Invoke(entry);
                    return;
                }
                SafeUrl.OpenTrusted(entry);
                return;
            }
            // 内置工具：编译期常量 URL，仅校验 scheme
            SafeUrl.Open(entry, "提示");
        }
    }
}
