using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace WINHELP
{
    /// <summary>
    /// SetupPage.xaml 交互逻辑 — 装机助手（新电脑常用软件官网导航）
    /// 所有链接均为软件官方地址，无 360 等垃圾软件。
    /// </summary>
    public partial class SetupPage : UserControl
    {
        public SetupPage()
        {
            InitializeComponent();
            // 不设置自身背景：Main 窗口已在 RootGrid/PageHost 上应用共享背景画刷
            // （含自定义背景图），本页保持透明，避免叠加第二层导致"嵌套"显示。
        }

        /// <summary>统一处理所有"打开官网"按钮：从 Tag 读取 URL 并用默认浏览器打开</summary>
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
    }
}