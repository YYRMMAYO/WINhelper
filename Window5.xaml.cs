using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// Window5.xaml 交互逻辑 — BUG 反馈页面
    /// </summary>
    public partial class Window5 : UserControl
    {
        // 腾讯文档 BUG 收集表单
        private const string BugFormUrl = "https://docs.qq.com/form/page/DSXJMWkNDYVFUWHBo";
        private const string GitHubUrl = "https://github.com/YYRMMAYO/WINhelper/issues";

        public Window5()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        }

        private void ApplyTheme()
        {
            RootGrid.Background = ThemeManager.CreateBackgroundBrush();

            // 打开表单按钮 — 主题色
            ThemeManager.ApplyButtonTheme(BtnOpenForm, ThemeManager.AccentColor);

            // GitHub 按钮 — 深色
            ThemeManager.ApplyButtonTheme(BtnGitHub, Color.FromRgb(0x2C, 0x3E, 0x50),
                hoverColor: Color.FromRgb(0x1A, 0x2A, 0x3A));
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
                MessageBox.Show($"无法打开链接: {ex.Message}");
            }
        }


        private void Button_Click_OpenForm(object sender, RoutedEventArgs e) => OpenUrl(BugFormUrl);

        private void Button_Click_GitHub(object sender, RoutedEventArgs e) => OpenUrl(GitHubUrl);
    }
}
