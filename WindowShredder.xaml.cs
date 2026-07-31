using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>文件粉碎页：安全擦除文件（不可恢复）。</summary>
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
        _items.Add(new ShredItem { Path = path, Status = UiLanguage.L("待处理", "Queued") });
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

        var r = MessageBox.Show(Window.GetWindow(this),
            UiLanguage.L($"确定要用 {passes} 遍随机数据彻底覆写并删除选中的 {_items.Count} 项吗？此操作不可恢复！",
                $"Permanently overwrite and delete the {_items.Count} selected item(s) with {passes} random passes? This cannot be undone!"),
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
