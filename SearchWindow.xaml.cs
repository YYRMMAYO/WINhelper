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
    /// 命令面板条目：既可是模块（跳转），也可是动作（直接执行）。
    /// 统一通过 <see cref="Execute"/> 委托触发，调用方（MainWindow）负责构建列表并注入行为。
    /// </summary>
    public class CommandItem
    {
        public string Key { get; set; } = "";
        public string Icon { get; set; } = "🔍";
        public string Group { get; set; } = "";
        public string TitleZh { get; set; } = "";
        public string TitleEn { get; set; } = "";
        public string SubZh { get; set; } = "";
        public string SubEn { get; set; } = "";
        public Action? Execute { get; set; }

        public string Title => UiLanguage.L(TitleZh, TitleEn);
        public string Sub => UiLanguage.L(SubZh, SubEn);
        public string Haystack => (TitleZh + " " + TitleEn + " " + Key + " " + SubZh + " " + SubEn).ToLowerInvariant();
    }

    /// <summary>
    /// 全局命令面板（独立窗体，Ctrl+K 唤起）：跨所有模块 + 动作搜索直达。
    /// 模态浮层，由 MainWindow 构建 CommandItem 列表并注入导航/动作委托，避免直接耦合各页面。
    /// </summary>
    public partial class SearchWindow : Window
    {
        private readonly List<CommandItem> _all;
        private readonly List<CommandItem> _filtered = new();

        public SearchWindow(IEnumerable<CommandItem> items)
        {
            InitializeComponent();
            _all = items.ToList();
            ApplyThemeColors();
            ThemeManager.ThemeChanged += OnThemeChanged;
            Loaded += (_, _) =>
            {
                // 让面板出现在主窗口上方约 1/8 处（命令面板惯例：靠上居中）
                if (Owner is Window owner)
                {
                    var p = owner.PointToScreen(new Point(0, 0));
                    Left = p.X + (owner.Width - Width) / 2;
                    Top = p.Y + Math.Max(36, owner.Height * 0.12);
                }
                TxtQuery.Focus();
                TxtQuery.SelectAll();
            };
            Filter("");
        }

        private void OnThemeChanged() => ApplyThemeColors();

        private void ApplyThemeColors()
        {
            bool dark = ThemeManager.IsStarryActive;
            var accent = ThemeManager.AccentColor;
            SetBrush("PalFg", dark ? Colors.White : Color.FromRgb(0x20, 0x24, 0x2B));
            SetBrush("PalSub", dark ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0x5A, 0x60, 0x6B));
            SetBrush("PalSel", Color.FromArgb(0x33, accent.R, accent.G, accent.B));
            SetBrush("PalCard", dark ? Color.FromArgb(0xEE, 0x1B, 0x1F, 0x2A) : Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF));
            SetBrush("PalBorder", Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        }

        private void SetBrush(object key, Color color)
        {
            if (Resources[key] is SolidColorBrush b) b.Color = color;
        }

        private void TxtQuery_TextChanged(object sender, TextChangedEventArgs e) => Filter(TxtQuery.Text);

        private void Filter(string q)
        {
            q = (q ?? "").Trim().ToLowerInvariant();
            var tokens = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _filtered.Clear();
            foreach (var it in _all)
            {
                if (tokens.Length == 0) { _filtered.Add(it); continue; }
                if (tokens.All(t => it.Haystack.Contains(t))) _filtered.Add(it);
            }
            ListResults.ItemsSource = null;
            ListResults.ItemsSource = _filtered;
            ListResults.SelectedIndex = _filtered.Count > 0 ? 0 : -1;
            TxtPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ListResults_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ExecuteSelected();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (e.Key == Key.Down) { Move(1); e.Handled = true; return; }
            if (e.Key == Key.Up) { Move(-1); e.Handled = true; return; }
            if (e.Key == Key.Enter) { ExecuteSelected(); e.Handled = true; return; }
        }

        private void Move(int d)
        {
            if (_filtered.Count == 0) return;
            int i = ListResults.SelectedIndex;
            i = (i < 0) ? 0 : (i + d + _filtered.Count) % _filtered.Count;
            ListResults.SelectedIndex = i;
            ListResults.ScrollIntoView(_filtered[i]);
        }

        private void ExecuteSelected()
        {
            if (ListResults.SelectedItem is CommandItem it)
            {
                try { it.Execute?.Invoke(); } catch { /* 动作执行异常不应关闭失败 */ }
                Close();
            }
        }

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e) => Close();
    }
}
