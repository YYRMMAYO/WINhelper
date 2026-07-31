using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>新手引导页：首次使用的提示与使用技巧。</summary>
public partial class BeginnerGuidePage : UserControl
{
    private readonly string[] _tips =
    {
        "按 Win+D 可快速显示桌面，再次按可恢复窗口。",
        "Ctrl+Shift+Esc 直接打开任务管理器，比 Ctrl+Alt+Del 更快。",
        "Win+E 一键打开文件资源管理器，方便查找文件。",
        "Win+Shift+S 可进行区域截图，截图会自动保存到剪贴板。",
        "磁盘空间不足时，可用「系统清理」安全清理临时文件。",
        "禁用不必要的开机启动项，能明显加快开机速度。",
        "网络异常时，先用「网络诊断」排查连通性与 DNS。",
        "Win+V 可打开剪贴板历史，找回之前复制过的内容。",
        "Alt+Tab 可在打开的窗口间快速切换。",
        "Win+I 快速进入 Windows 设置，方便调整系统选项。",
        "遇到电脑问题别慌，「故障排查向导」会一步步带你排查。",
        "定期清理回收站和浏览器缓存，可释放不少磁盘空间。"
    };

    private int _index;
    private System.Windows.Threading.DispatcherTimer? _statusResetTimer;

    public BeginnerGuidePage()
    {
        InitializeComponent();
        ApplyTheme();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);

        _index = (int)(DateTime.Now.DayOfYear % _tips.Length);
        TxtTip.Text = _tips[_index];
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnNextTip, ThemeManager.AccentColor);
        ThemeManager.ApplyButtonTheme(BtnDone, ThemeManager.AccentColor);
    }

    private void BtnDone_Click(object sender, RoutedEventArgs e)
    {
        // 嵌入主窗口后，「完成」按钮可导航回首页；此处暂留空操作，由 MainWindow 统一处理返回。
    }

    private void BtnNextTip_Click(object sender, RoutedEventArgs e)
    {
        _index = (_index + 1) % _tips.Length;
        TxtTip.Text = _tips[_index];
    }

    /// <summary>
    /// 一键复制快捷键文本到剪贴板，并在底部状态栏短暂显示"已复制"反馈。
    /// Tag 中存放的是要复制的原始文本（用 \n 分隔多行）。
    /// </summary>
    private void BtnCopyShortcuts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is string text)
            {
                // Tag 中用 &#x0a; 表示换行，XAML 解析后会变成 \n
                Clipboard.SetText(text);
                ShowStatus("✅ 已复制到剪贴板", success: true);
            }
        }
        catch
        {
            ShowStatus("❌ 复制失败，请手动选择文本", success: false);
        }
    }

    /// <summary>短暂显示状态文字，2 秒后恢复默认提示</summary>
    private void ShowStatus(string message, bool success)
    {
        TxtStatus.Text = message;
        TxtStatus.Foreground = new SolidColorBrush(success
            ? Color.FromRgb(0x27, 0xAE, 0x60)
            : Color.FromRgb(0xE7, 0x4C, 0x3C));

        _statusResetTimer?.Stop();
        _statusResetTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _statusResetTimer.Tick += (_, _) =>
        {
            _statusResetTimer.Stop();
            TxtStatus.Text = "有问题随时打开「故障排查向导」或「电脑帮助」";
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        };
        _statusResetTimer.Start();
    }
}
