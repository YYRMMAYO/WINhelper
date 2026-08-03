using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// 陪伴运行说明页（导航 key="companion"，内嵌在右侧内容区，不再弹窗）
    /// 由 MainWindow._factories 懒加载；依赖 CompanionManager 状态与 ThemeManager 玻璃画刷。
    /// </summary>
    public partial class CompanionPage : UserControl
    {
        public CompanionPage()
        {
            InitializeComponent();

            ApplyBackground();
            ApplyAccent();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(() => { ApplyBackground(); ApplyAccent(); });

            Refresh();
            CompanionManager.ModeChanged += Refresh;
        }

        private void ApplyBackground()
        {
            // 透明：透出主窗口统一背景，避免自定义壁纸被本页重复绘制造成重叠
            this.Background = Brushes.Transparent;
        }

        private void ApplyAccent()
        {
            BtnRebind.Background = new SolidColorBrush(ThemeManager.AccentColor);
        }

        private void Refresh()
        {
            bool on = CompanionManager.IsInCompanionMode;

            // 主按钮文案 + 主题色
            BtnToggle.Content = on ? "关闭陪伴运行" : "开启陪伴运行";
            ThemeManager.ApplyButtonTheme(BtnToggle, ThemeManager.AccentColor);
            ApplyAccent();

            // 主状态卡：标题/描述
            TxtBigStatus.Text = on ? "陪伴运行中" : "陪伴未运行";
            TxtBigDesc.Text = on
                ? "主界面已隐藏，桌面仅保留陪伴小窗。再次按 F11 / 全局热键可恢复。"
                : "开启后程序会缩小成悬浮小窗，隐藏主窗口，专注陪你工作。";

            // 模式行
            TxtMode.Text = on ? "小窗模式" : "标准模式";

            // 状态徽章
            StatusBadgeDot.Fill = new SolidColorBrush(on ? Color.FromRgb(0x27, 0xAE, 0x60) : Color.FromRgb(0xBD, 0xC3, 0xC7));
            StatusBadgeText.Text = on ? "运行中" : "未开启";
            StatusBadgeText.Foreground = new SolidColorBrush(on ? Color.FromRgb(0x27, 0xAE, 0x60) : Color.FromRgb(0x7F, 0x8C, 0x8D));
            StatusBadge.Background = new SolidColorBrush(on ? Color.FromRgb(0xE8, 0xF5, 0xE9) : Color.FromRgb(0xF0, 0xF2, 0xF5));

            // 顶部光晕：根据状态变色 + emoji
            Color halo = on ? Color.FromRgb(0x27, 0xAE, 0x60) : Color.FromRgb(0xBD, 0xC3, 0xC7);
            HaloStop1.Color = Color.FromArgb(0x22, halo.R, halo.G, halo.B);
            HaloStop2.Color = Color.FromArgb(0x00, halo.R, halo.G, halo.B);
            HaloEmoji.Text = on ? "✨" : "💛";

            // 全局热键文案：实际注册成功的标签
            TxtHotkey.Text = string.IsNullOrEmpty(CompanionManager.HotkeyLabel) ? "未注册" : CompanionManager.HotkeyLabel;

            // 主按钮文字提示
            TxtHint.Text = on
                ? "按 F11 / 全局热键也能切换"
                : "按 F11 或全局热键也能切换";
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            CompanionManager.Toggle();
        }

        /// <summary>开始录制新的全局热键</summary>
        private void BtnRebind_Click(object sender, RoutedEventArgs e)
        {
            if (GlobalHotkeyCapture.IsCapturing) return;

            CapturePanel.Visibility = Visibility.Visible;
            BtnRebind.IsEnabled = false;
            BtnToggle.IsEnabled = false;

            CompanionManager.BeginHotkeyCapture(result =>
            {
                Dispatcher.Invoke(() =>
                {
                    CapturePanel.Visibility = Visibility.Collapsed;
                    BtnRebind.IsEnabled = true;
                    BtnToggle.IsEnabled = true;

                    if (string.IsNullOrEmpty(result))
                    {
                        TxtCaptureSub.Text = "该组合被占用，已保留原热键，请重试";
                        // 短暂提示后恢复
                        TxtHotkey.Text = CompanionManager.HotkeyLabel;
                        return;
                    }
                    TxtHotkey.Text = result;
                    TxtCaptureSub.Text = $"已更新为 {result} 并保存";
                });
            });
        }

        /// <summary>取消录制热键</summary>
        private void BtnCancelCapture_Click(object sender, RoutedEventArgs e)
        {
            CompanionManager.CancelHotkeyCapture();
            CapturePanel.Visibility = Visibility.Collapsed;
            BtnRebind.IsEnabled = true;
            BtnToggle.IsEnabled = true;
            TxtHotkey.Text = CompanionManager.HotkeyLabel;
        }
    }
}
