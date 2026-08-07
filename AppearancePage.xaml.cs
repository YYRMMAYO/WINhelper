using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Globalization;
using System.Linq;

namespace WINHELP
{
    /// <summary>
    /// 主题色卡片视图模型
    /// </summary>
    public class SwatchItem
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public Brush Brush { get; set; } = Brushes.Gray;
        public Brush OverlayBrush { get; set; } = Brushes.Transparent;
        public Visibility StarVisibility { get; set; } = Visibility.Collapsed;
    }

    /// <summary>
    /// AppearancePage.xaml 交互逻辑 — 个性装扮（导航 key="theme"，内嵌为右侧页面）
    /// 主题色/背景图/玻璃强度/字体均直接写 ThemeManager 与 theme.json。
    /// 由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。
    /// </summary>
    public partial class AppearancePage : UserControl
    {
        private string _selectedImagePath = "";
        private readonly ObservableCollection<SwatchItem> _swatches = new();

        // 模糊/透明度滑块节流：把昂贵的主题重渲限频到最新值，避免每个刻度都卡顿
        private readonly DispatcherTimer _blurThrottle = new() { Interval = TimeSpan.FromMilliseconds(60) };
        private double? _pendingOpacity = null;
        private double? _pendingGlass = null;

        // 关怀模式缩放节流（LayoutTransform + 窗口尺寸调整同样昂贵）
        private readonly DispatcherTimer _scaleThrottle = new() { Interval = TimeSpan.FromMilliseconds(60) };
        private double? _pendingScale = null;

        /// <summary>请求返回首页（由 MainWindow 注入）</summary>
        public Action? OnCloseRequest;

        public AppearancePage()
        {
            InitializeComponent();
            _blurThrottle.Tick += BlurThrottle_Tick;
            _scaleThrottle.Tick += ScaleThrottle_Tick;

            BuildSwatches();
            SwatchList.ItemsSource = _swatches;

            InitFontSection();

            // 加载当前主题状态
            _selectedImagePath = ThemeManager.BackgroundImagePath;

            // 初始化玻璃效果控件
            SliderOpacity.Value = ThemeManager.BackgroundOpacity * 100;
            SliderGlass.Value = ThemeManager.GlassStrength * 100;
            if (ThemeManager.GlassEffect == GlassMode.Acrylic) RadioAcrylic.IsChecked = true;
            else RadioTranslucent.IsChecked = true;
            UpdateOpacityLabel();
            UpdateGlassLabel();

            // 关怀模式缩放：从已保存的倍率恢复滑块与标签
            SliderScale.Value = ThemeManager.UiScale * 100;
            UpdateScaleLabel();

            UpdateSelectedHighlight();
            UpdateBackgroundPreview();
            UpdateSelectedNameLabel();

            // 跟随系统主题开关状态
            ChkFollowSystem.IsChecked = ThemeManager.FollowSystem;

            if (!string.IsNullOrEmpty(_selectedImagePath))
            {
                BackgroundPathBox.Text = _selectedImagePath;
            }

            ApplyBackground();
            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged() => Dispatcher.Invoke(() =>
        {
            ApplyBackground();
            UpdateSelectedHighlight();
            UpdateSelectedNameLabel();
            UpdateBackgroundPreview();
        });

        /// <summary>本页背景保持透明，直接透出主窗口统一绘制的背景（含自定义壁纸 / 星空），
        /// 避免与 MainWindow 的 RootGrid.Background 重复绘制同一张壁纸造成"重叠"。</summary>
        private void ApplyBackground()
        {
            this.Background = Brushes.Transparent;
        }

        // ===== 色卡构建 =====
        private void BuildSwatches()
        {
            _swatches.Clear();
            foreach (var p in ThemeManager.Presets)
            {
                var item = new SwatchItem
                {
                    Key = p.Key,
                    Name = p.Name,
                    Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.ColorHex)),
                };
                if (p.IsStarry)
                {
                    item.OverlayBrush = ThemeManager.BuildStarryBackground(p.Key);
                    item.StarVisibility = Visibility.Visible;
                }
                _swatches.Add(item);
            }
        }

        // ===== 实时应用 =====
        private void Swatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string key)
            {
                var preset = System.Array.Find(ThemeManager.Presets, x => x.Key == key);
                if (preset == null) return;
                // 手动选择主题色 → 关闭跟随系统主题（手动优先）
                if (ThemeManager.FollowSystem)
                {
                    ThemeManager.SetFollowSystem(false);
                    ChkFollowSystem.IsChecked = false;
                }
                ThemeManager.SetPreset(preset);
                ThemeManager.Save();
                UpdateSelectedHighlight();
                UpdateSelectedNameLabel();
            }
        }

        private void FollowSystem_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            ThemeManager.SetFollowSystem(ChkFollowSystem.IsChecked == true);
            ThemeManager.Save();
        }

        private void UpdateSelectedHighlight()
        {
            // 遍历 ItemsControl 找到匹配的项，切换其内部 SelectedMark.Visibility
            for (int i = 0; i < SwatchList.Items.Count; i++)
            {
                if (SwatchList.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter cp)
                {
                    if (cp.ContentTemplate?.FindName("SelectedMark", cp) is Border mark)
                    {
                        if (cp.DataContext is SwatchItem s)
                        {
                            mark.Visibility = (s.Key == ThemeManager.ActivePresetKey)
                                ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                }
            }
        }

        private void UpdateSelectedNameLabel()
        {
            var p = System.Array.Find(ThemeManager.Presets, x => x.Key == ThemeManager.ActivePresetKey);
            TxtSelectedName.Text = p != null
                ? (p.IsStarry ? $"当前：{p.Name}（深色）" : $"当前：{p.Name}")
                : "未选择";
        }

        private void UpdateOpacityLabel() => TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
        private void UpdateGlassLabel() => TxtGlass.Text = $"{(int)SliderGlass.Value}%";

        private void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityLabel();
            if (!IsLoaded) return;
            _pendingOpacity = SliderOpacity.Value / 100.0;
            if (!_blurThrottle.IsEnabled) _blurThrottle.Start();
        }

        private void Glass_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateGlassLabel();
            if (!IsLoaded) return;
            _pendingGlass = SliderGlass.Value / 100.0;
            if (!_blurThrottle.IsEnabled) _blurThrottle.Start();
        }

        // 节流回调用：限频提交最新值，避免拖动滑块时整窗模糊被反复重渲导致卡顿
        private void BlurThrottle_Tick(object? sender, EventArgs e)
        {
            _blurThrottle.Stop();
            if (_pendingOpacity.HasValue)
            {
                ThemeManager.SetBackgroundOpacity(_pendingOpacity.Value);
                _pendingOpacity = null;
            }
            if (_pendingGlass.HasValue)
            {
                ThemeManager.SetGlassStrength(_pendingGlass.Value);
                _pendingGlass = null;
            }
        }

        // ===== 关怀模式（界面缩放） =====

        private void UpdateScaleLabel() => TxtScale.Text = $"{(int)SliderScale.Value}%";

        private void Scale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateScaleLabel();
            if (!IsLoaded) return;
            _pendingScale = SliderScale.Value / 100.0;
            if (!_scaleThrottle.IsEnabled) _scaleThrottle.Start();
        }

        private void ScaleThrottle_Tick(object? sender, EventArgs e)
        {
            _scaleThrottle.Stop();
            if (!_pendingScale.HasValue) return;
            ThemeManager.SetUiScale(_pendingScale.Value);
            _pendingScale = null;
        }

        private void ScaleNormal_Click(object sender, RoutedEventArgs e) => SetScalePreset(1.00);
        private void ScaleCare_Click(object sender, RoutedEventArgs e) => SetScalePreset(1.25);
        private void ScaleLarge_Click(object sender, RoutedEventArgs e) => SetScalePreset(1.40);

        /// <summary>快捷预设：直接应用（与当前滑块值相同也不跳过），并同步滑块显示。</summary>
        private void SetScalePreset(double scale)
        {
            // 先把滑块同步到目标值（可能触发 ValueChanged 排队一个节流任务），
            // 再取消该排队任务并直接应用一次，避免重复 ApplyUiScale / 重复写配置。
            SliderScale.Value = scale * 100;
            UpdateScaleLabel();
            _scaleThrottle.Stop();
            _pendingScale = null;
            ThemeManager.SetUiScale(scale);
        }

        private void GlassMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            ThemeManager.SetGlassMode(RadioAcrylic.IsChecked == true ? GlassMode.Acrylic : GlassMode.Translucent);
            ThemeManager.Save();
        }

        /// <summary>选择背景图片</summary>
        private void ChooseBackground_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|全部文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedImagePath = dialog.FileName;
                BackgroundPathBox.Text = _selectedImagePath;
                // 切到图片背景时，自动从星空模式切回
                if (ThemeManager.IsStarryActive)
                {
                    ThemeManager.IsStarryActive = false;
                    ThemeManager.ActivePresetKey = "default";
                    UpdateSelectedHighlight();
                    UpdateSelectedNameLabel();
                }
                ThemeManager.SetBackground(_selectedImagePath);
                ThemeManager.Save();
                UpdateBackgroundPreview();
            }
        }

        /// <summary>清除背景</summary>
        private void ClearBackground_Click(object sender, RoutedEventArgs e)
        {
            _selectedImagePath = "";
            BackgroundPathBox.Text = "未选择图片";
            PreviewBorder.Visibility = Visibility.Collapsed;
            PreviewBrush.ImageSource = null;
            ThemeManager.ClearBackground();
            ThemeManager.Save();
        }

        private void UpdateBackgroundPreview()
        {
            if (ThemeManager.IsStarryActive)
            {
                PreviewBorder.Visibility = Visibility.Visible;
                PreviewBrush.ImageSource = null;
                PreviewBorder.Background = ThemeManager.BuildStarryBackground(ThemeManager.ActivePresetKey);
            }
            else if (!string.IsNullOrEmpty(_selectedImagePath) && System.IO.File.Exists(_selectedImagePath))
            {
                PreviewBorder.Visibility = Visibility.Visible;
                try
                {
                    PreviewBrush.ImageSource = new BitmapImage(new Uri(_selectedImagePath));
                }
                catch { }
                PreviewBorder.ClearValue(Border.BackgroundProperty);
            }
            else
            {
                PreviewBorder.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>完成（关闭面板返回主界面）</summary>
        private void Done_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Save();
            OnCloseRequest?.Invoke();
        }

        // ===== 界面字体模块 =====
        /// <summary>初始化字体下拉框：列出系统中已安装的常用中文美观字体，并选中当前配置项。</summary>
        private void InitFontSection()
        {
            try
            {
                // 候选中文常用美观字体（英文 FamilyName → 中文展示名）
                var candidates = new (string Family, string Label)[]
                {
                    ("Microsoft YaHei",      "微软雅黑"),
                    ("Microsoft YaHei UI",   "微软雅黑 UI"),
                    ("Microsoft JhengHei",   "微软正黑体"),
                    ("Microsoft JhengHei UI","微软正黑体 UI"),
                    ("DengXian",             "等线"),
                    ("SimSun",               "宋体"),
                    ("NSimSun",              "新宋体"),
                    ("SimHei",               "黑体"),
                    ("KaiTi",                "楷体"),
                    ("KaiTi_GB2312",         "楷体 GB2312"),
                    ("FangSong",             "仿宋"),
                    ("FangSong_GB2312",      "仿宋 GB2312"),
                    ("LiSu",                 "隶书"),
                    ("YouYuan",              "幼圆"),
                    ("STSong",               "华文宋体"),
                    ("STKaiti",              "华文楷体"),
                    ("STXihei",              "华文细黑"),
                    ("STZhongsong",          "华文中宋"),
                    ("STHeiti",              "华文黑体"),
                    ("Source Han Sans SC",   "思源黑体"),
                    ("Source Han Serif SC",  "思源宋体"),
                    ("Noto Sans CJK SC",     "思源黑体 (Noto)"),
                };

                var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Fonts.SystemFontFamilies)
                    installed.Add(f.Source);

                CmbFont.Items.Clear();
                int defaultIndex = 0, i = 0;
                foreach (var c in candidates)
                {
                    if (!installed.Contains(c.Family)) continue;
                    CmbFont.Items.Add(new ComboBoxItem
                    {
                        Content = $"{c.Label}  ({c.Family})",
                        Tag = c.Family,
                        FontFamily = new FontFamily(c.Family),
                        FontSize = 14,
                    });
                    if (string.Equals(c.Family, ThemeManager.FontFamilyName, StringComparison.OrdinalIgnoreCase))
                        defaultIndex = i;
                    i++;
                }

                // 若已保存的字体不在候选列表但已安装，则补一项，避免回显丢失
                if (!string.IsNullOrWhiteSpace(ThemeManager.FontFamilyName)
                    && installed.Contains(ThemeManager.FontFamilyName)
                    && CmbFont.Items.Cast<ComboBoxItem>().All(x =>
                        !string.Equals((string?)x.Tag, ThemeManager.FontFamilyName, StringComparison.OrdinalIgnoreCase)))
                {
                    CmbFont.Items.Add(new ComboBoxItem
                    {
                        Content = ThemeManager.FontFamilyName,
                        Tag = ThemeManager.FontFamilyName,
                        FontFamily = new FontFamily(ThemeManager.FontFamilyName),
                        FontSize = 14,
                    });
                    defaultIndex = CmbFont.Items.Count - 1;
                }

                if (CmbFont.Items.Count > 0)
                    CmbFont.SelectedIndex = defaultIndex;

                UpdateFontPreview();
            }
            catch { /* 字体枚举失败时不崩溃 */ }
        }

        private void CmbFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbFont.SelectedItem is ComboBoxItem item && item.Tag is string family)
            {
                ThemeManager.SetFont(family);
                UpdateFontPreview();
            }
        }

        private void FontReset_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.SetFont("Microsoft YaHei");
            for (int i = 0; i < CmbFont.Items.Count; i++)
            {
                if (CmbFont.Items[i] is ComboBoxItem it
                    && string.Equals((string?)it.Tag, "Microsoft YaHei", StringComparison.OrdinalIgnoreCase))
                {
                    CmbFont.SelectedIndex = i;
                    break;
                }
            }
            UpdateFontPreview();
        }

        private void UpdateFontPreview()
        {
            try { TxtFontPreview.FontFamily = new FontFamily(ThemeManager.FontFamilyName); }
            catch { }
        }
    }
}
