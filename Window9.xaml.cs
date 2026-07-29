using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// Window9.xaml 交互逻辑 — 官网导航（日常工作 / 办公常用软件官方直达）
    /// </summary>
    public partial class Window9 : UserControl
    {
        /// <summary>请求返回首页（由 MainWindow 注入）</summary>
        public Action? OnCloseRequest;

        public Window9()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        }

        private void ApplyTheme()
        {
            RootGrid.Background = ThemeManager.CreateBackgroundBrush();

            if (BtnBack != null)
                ThemeManager.ApplyButtonTheme(BtnBack, Color.FromRgb(0x95, 0xA5, 0xA6),
                    hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
        }

        /// <summary>统一处理所有"打开官网"按钮：从 Tag 中读取 URL 并用默认浏览器打开</summary>
        private void OpenSite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
            {
                OpenUrl(url);
            }
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
                MessageBox.Show($"无法打开链接: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Button_Click_Back(object sender, RoutedEventArgs e) => OnCloseRequest?.Invoke();
    }
}
