using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// BugReportPage.xaml 交互逻辑 — BUG 反馈页面
    /// </summary>
    public partial class BugReportPage : UserControl
    {
        // 腾讯文档 BUG 收集表单
        private const string BugFormUrl = "https://docs.qq.com/form/page/DSXJMWkNDYVFUWHBo";
        private const string GitHubUrl = "https://github.com/YYRMMAYO/WINhelper/issues";

        // 崩溃日志位置（与全局 crash.log 写入位置保持一致）
        private static readonly string CrashLogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINHELP", "crash.log");

        public BugReportPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            RefreshLogState();
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;

            // 打开表单按钮 — 主题色
            ThemeManager.ApplyButtonTheme(BtnOpenForm, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnCopyLog, ThemeManager.AccentColor);

            // GitHub 按钮 — 深色
            ThemeManager.ApplyButtonTheme(BtnGitHub, Color.FromRgb(0x2C, 0x3E, 0x50),
                hoverColor: Color.FromRgb(0x1A, 0x2A, 0x3A));
        }

        /// <summary>刷新崩溃日志状态文案（是否存在 / 大小）</summary>
        private void RefreshLogState()
        {
            if (File.Exists(CrashLogPath))
            {
                var len = new FileInfo(CrashLogPath).Length;
                TxtLogState.Text = UiLanguage.L($"存在，约 {len / 1024} KB", $"present, ~{len / 1024} KB");
                ChkAttachLog.IsEnabled = true;
                BtnCopyLog.IsEnabled = true;
            }
            else
            {
                TxtLogState.Text = UiLanguage.L("无（本次运行未记录到崩溃）", "none (no crash recorded)");
                ChkAttachLog.IsEnabled = false;
                BtnCopyLog.IsEnabled = false;
            }
        }

        private void ChkAttachLog_Changed(object sender, RoutedEventArgs e) { /* 状态仅影响打开表单时的自动复制 */ }

        /// <summary>读取崩溃日志内容（不含密钥等敏感信息仅含崩溃堆栈）</summary>
        private string? ReadCrashLog()
        {
            try { return File.Exists(CrashLogPath) ? File.ReadAllText(CrashLogPath) : null; }
            catch { return null; }
        }

        private void Button_Click_CopyLog(object sender, RoutedEventArgs e)
        {
            var log = ReadCrashLog();
            if (string.IsNullOrEmpty(log))
            {
                TxtLogHint.Text = UiLanguage.L("没有可复制的崩溃日志。", "No crash log to copy.");
                TxtLogHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                return;
            }
            try
            {
                Clipboard.SetText(log);
                TxtLogHint.Text = UiLanguage.L("已复制到剪贴板，可在表单中粘贴（Ctrl+V）。", "Copied to clipboard; paste (Ctrl+V) into the form.");
                TxtLogHint.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
            }
            catch (Exception ex)
            {
                TxtLogHint.Text = UiLanguage.L("复制失败：", "Copy failed: ") + ex.Message;
                TxtLogHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
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
                MessageBox.Show($"无法打开链接: {ex.Message}");
            }
        }


        private void Button_Click_OpenForm(object sender, RoutedEventArgs e)
        {
            // 勾选「附带崩溃日志」时，自动复制内容到剪贴板，便于用户在表单中粘贴
            if (ChkAttachLog.IsChecked == true)
            {
                var log = ReadCrashLog();
                if (!string.IsNullOrEmpty(log))
                {
                    try { Clipboard.SetText(log); } catch { }
                    MessageBox.Show(
                        UiLanguage.L("已自动复制崩溃日志到剪贴板。\n请在打开的反馈表单中粘贴（Ctrl+V）以帮助我们定位问题。",
                                     "The crash log has been copied to your clipboard.\nPlease paste it (Ctrl+V) into the feedback form to help us diagnose the issue."),
                        UiLanguage.L("已附带崩溃日志", "Crash log attached"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            OpenUrl(BugFormUrl);
        }

        private void Button_Click_GitHub(object sender, RoutedEventArgs e) => OpenUrl(GitHubUrl);
    }
}
