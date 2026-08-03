using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>文件粉碎页（导航 key="shred"）：安全擦除文件（不可恢复）。由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。</summary>
public partial class WindowShredder : UserControl
{
    /// <summary>数据模型：ShredItem。</summary>
    private sealed class ShredItem
    {
        public string Path { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusColor { get; set; } = "#7F8C8D";
    }

    private readonly ObservableCollection<ShredItem> _items = new();
    private bool _busy;

    public WindowShredder()
    {
        InitializeComponent();
        ListItems.ItemsSource = _items;

        DropArea.PreviewDragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        DropArea.Drop += DropArea_Drop;

        PassSlider.ValueChanged += (_, _) => PassValue.Text = ((int)PassSlider.Value).ToString();

        ApplyTheme();
        RefreshStrings();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(() => { ApplyTheme(); RefreshStrings(); });
        UiLanguage.Changed += () => Dispatcher.Invoke(RefreshStrings);
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnSelectFiles, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnSelectFolder, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnShred, Color.FromRgb(0xE7, 0x4C, 0x3C));
        ThemeManager.ApplyButtonTheme(BtnClear, Color.FromRgb(0x95, 0xA5, 0xA6));
    }

    private void RefreshStrings()
    {
        TxtTitle.Text = UiLanguage.L("文件粉碎", "File Shredder");
        TxtSub.Text = UiLanguage.L("不可恢复地销毁文件 / 文件夹（多次随机覆写）",
            "Irrecoverably destroy files / folders (multiple random overwrites)");
        TxtDrop.Text = UiLanguage.L("📂 将文件 / 文件夹拖拽到此处", "📂 Drag files / folders here");
        BtnSelectFiles.Content = UiLanguage.L("选择文件", "Select Files");
        BtnSelectFolder.Content = UiLanguage.L("选择文件夹", "Select Folder");
        TxtPassesLabel.Text = UiLanguage.L("粉碎遍数", "Passes");
        BtnShred.Content = UiLanguage.L("开始粉碎", "Shred");
        BtnClear.Content = UiLanguage.L("清空列表", "Clear List");
        if (!_busy) TxtStatus.Text = UiLanguage.L("就绪", "Ready");
    }

    // ===== 选择 / 拖放 =====

    private void BtnSelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = UiLanguage.L("选择要粉碎的文件", "Select files to shred")
        };
        if (dlg.ShowDialog() == true)
            foreach (var f in dlg.FileNames) AddItem(f);
    }

    private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = UiLanguage.L("选择要粉碎的文件夹", "Select folder to shred")
        };
        if (dlg.ShowDialog() == true)
            AddItem(dlg.FolderName);
    }

    private void DropArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var p in paths) AddItem(p);
        }
    }

    private void AddItem(string path)
    {
        if (_items.Any(i => i.Path == path)) return;
        // 安全防护：拒绝添加系统关键路径，避免误操作造成不可恢复的系统损坏（安全审计建议 P0）
        if (IsProtectedPath(path))
        {
            _items.Add(new ShredItem
            {
                Path = path,
                Status = UiLanguage.L("❌ 已拒绝：系统关键路径不可粉碎", "❌ Blocked: critical system path"),
                StatusColor = "#E74C3C"
            });
            TxtStatus.Text = UiLanguage.L("已阻止对系统关键路径的粉碎操作", "Blocked shredding of a critical system path");
            return;
        }
        _items.Add(new ShredItem { Path = path, Status = UiLanguage.L("待处理", "Queued") });
    }

    /// <summary>判断路径是否为受保护的系统关键路径（禁止粉碎）。</summary>
    private static bool IsProtectedPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(full)) return true;

            // 1) 盘符根目录（如 C:\）— 绝不能粉碎整个分区
            if (full.Length == 2 && full[1] == ':') return true;
            if (full.Length == 3 && full[1] == ':' && (full[2] == '\\' || full[2] == '/')) return true;

            // 2) 系统关键目录
            var protectedDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),          // C:\Windows
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),     // C:\Program Files
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),  // C:\Program Files (x86)
                Environment.GetFolderPath(Environment.SpecialFolder.System),           // C:\Windows\System32
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),      // C:\Users\<name>
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), // 桌面
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),      // 文档
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),       // 图片
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),          // 音乐
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),         // 视频
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), // AppData\Local
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),      // AppData\Roaming
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) // ProgramData
            };

            foreach (var dir in protectedDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var d = Path.GetFullPath(dir).TrimEnd('\\', '/');
                // 精确匹配或位于受保护目录之内
                if (full.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 3) 当前运行的程序自身（防止粉碎正在使用的 exe / 资源）
            var selfDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(selfDir))
            {
                var sd = Path.GetFullPath(selfDir).TrimEnd('\\', '/');
                if (full.Equals(sd, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(sd + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // 路径解析失败时保守处理：禁止粉碎
            return true;
        }
        return false;
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _items.Clear();
        Prog.Value = 0;
    }

    // ===== 粉碎逻辑 =====

    private async void BtnShred_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _items.Count == 0) return;
        var passes = (int)PassSlider.Value;

        // 安全：确认弹窗列出全部将被粉碎的具体路径，避免"以为删 A 实际删了 B"（安全审计建议 P0）
        var list = new System.Text.StringBuilder();
        int shown = 0;
        foreach (var it in _items)
        {
            if (shown >= 5) { list.AppendLine("…"); break; }
            list.AppendLine("• " + it.Path);
            shown++;
        }

        var r = MessageBox.Show(Window.GetWindow(this),
            UiLanguage.L($"确定要用 {passes} 遍随机数据彻底覆写并删除以下 {_items.Count} 项吗？此操作不可恢复！\n\n{list}",
                $"Permanently overwrite and delete the following {_items.Count} item(s) with {passes} random passes? This cannot be undone!\n\n{list}"),
            UiLanguage.L("确认粉碎", "Confirm Shred"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        _busy = true;
        BtnShred.IsEnabled = false;
        BtnClear.IsEnabled = false;
        TxtStatus.Text = UiLanguage.L("正在粉碎…", "Shredding…");

        int done = 0;
        foreach (var item in _items)
        {
            await Task.Run(() => ShredPath(item, passes));
            done++;
            Dispatcher.Invoke(() => Prog.Value = done * 100.0 / _items.Count);
        }

        Prog.Value = 100;
        TxtStatus.Text = UiLanguage.L($"完成：已处理 {_items.Count} 项", $"Done: processed {_items.Count} item(s)");
        _busy = false;
        BtnShred.IsEnabled = true;
        BtnClear.IsEnabled = true;
    }

    private void ShredPath(ShredItem item, int passes)
    {
        try
        {
            var attr = File.GetAttributes(item.Path);
            if (attr.HasFlag(FileAttributes.Directory))
                ShredDirectory(item, item.Path, passes);
            else
                ShredFile(item, item.Path, passes);
            Dispatcher.Invoke(() =>
            {
                item.Status = UiLanguage.L("已粉碎", "Shredded");
                item.StatusColor = "#27AE60";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                item.Status = UiLanguage.L("失败：" + ex.Message, "Failed: " + ex.Message);
                item.StatusColor = "#E74C3C";
            });
        }
    }

    private void ShredDirectory(ShredItem? item, string dir, int passes)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            ShredFile(null, file, passes);
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            ShredDirectory(null, sub, passes);
        try { Directory.Delete(dir, false); } catch { /* 非空则忽略 */ }
    }

    private void ShredFile(ShredItem? item, string path, int passes)
    {
        try
        {
            // 清除只读属性以便覆写
            File.SetAttributes(path, FileAttributes.Normal);
        }
        catch { }

        long length = new FileInfo(path).Length;
        using var rng = RandomNumberGenerator.Create();
        byte[] buffer = new byte[1024 * 1024];

        for (int p = 0; p < passes; p++)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            long remaining = length;
            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(buffer.Length, remaining);
                rng.GetBytes(buffer);
                fs.Write(buffer, 0, toWrite);
                remaining -= toWrite;
            }
            fs.Flush(true);
        }
        File.Delete(path);
    }
}
