using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>软件卸载页（导航 key="uninstall"）：管理 / 卸载已安装程序、清理残留。由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。</summary>
public partial class WindowUninstaller : UserControl
{
    /// <summary>数据模型：ProgramItem。</summary>
    private sealed class ProgramItem
    {
        public string DisplayName { get; set; } = "";
        public long EstimatedSizeKB;
        public string SizeText => EstimatedSizeKB > 0
            ? (EstimatedSizeKB > 1024 * 1024 ? $"{EstimatedSizeKB / 1024.0 / 1024:F1} GB"
               : EstimatedSizeKB > 1024 ? $"{EstimatedSizeKB / 1024.0:F1} MB"
               : $"{EstimatedSizeKB} KB")
            : "";
        public string? UninstallString;
        public string? KeyPath;
    }

    /// <summary>数据模型：ResidueItem。</summary>
    private sealed class ResidueItem
    {
        public string Path { get; set; } = "";
        public bool IsChecked { get; set; }
    }

    private readonly ObservableCollection<ProgramItem> _programs = new();
    private readonly ObservableCollection<ResidueItem> _residues = new();
    private ProgramItem? _selected;

    public WindowUninstaller()
    {
        InitializeComponent();
        ListPrograms.ItemsSource = _programs;
        ListResidue.ItemsSource = _residues;

        ApplyTheme();
        RefreshStrings();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(() => { ApplyTheme(); RefreshStrings(); });
        UiLanguage.Changed += () => Dispatcher.Invoke(RefreshStrings);
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnScan, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnUninstall, Color.FromRgb(0xE6, 0x7E, 0x22));
        ThemeManager.ApplyButtonTheme(BtnScanResidue, Color.FromRgb(0x5B, 0x8D, 0xEF));
        ThemeManager.ApplyButtonTheme(BtnDeleteResidue, Color.FromRgb(0xE7, 0x4C, 0x3C));
    }

    private void RefreshStrings()
    {
        TxtTitle.Text = UiLanguage.L("卸载残留", "Uninstall Residue");
        TxtSub.Text = UiLanguage.L("管理已安装程序，清理卸载后的残留文件",
            "Manage installed programs and clean up leftover files after uninstall");
        BtnScan.Content = UiLanguage.L("扫描已安装程序", "Scan Installed");
        BtnUninstall.Content = UiLanguage.L("卸载", "Uninstall");
        TxtResidueTitle.Text = UiLanguage.L("残留文件", "Leftover Files");
        BtnScanResidue.Content = UiLanguage.L("扫描残留", "Scan Leftover");
        BtnDeleteResidue.Content = UiLanguage.L("删除选中", "Delete Selected");
        if (TxtStatus != null && _programs.Count == 0 && _residues.Count == 0)
            TxtStatus.Text = UiLanguage.L("点击「扫描已安装程序」开始", "Click 'Scan Installed' to start");
    }

    // ===== 扫描程序 =====

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        BtnScan.IsEnabled = false;
        TxtStatus.Text = UiLanguage.L("正在扫描已安装程序…", "Scanning installed programs…");
        _programs.Clear();
        _residues.Clear();
        BtnUninstall.IsEnabled = false;
        BtnScanResidue.IsEnabled = false;
        BtnDeleteResidue.IsEnabled = false;

        var roots = new[]
        {
            Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        var found = new System.Collections.Generic.List<ProgramItem>();
        await Task.Run(() =>
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                try
                {
                    foreach (var name in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(name);
                            if (key == null) continue;
                            var disp = key.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(disp)) continue;
                            var item = new ProgramItem
                            {
                                DisplayName = disp,
                                EstimatedSizeKB = key.GetValue("EstimatedSize") is int sz ? sz : 0,
                                UninstallString = key.GetValue("UninstallString") as string,
                                KeyPath = $@"{root.Name}\{name}"
                            };
                            lock (found) found.Add(item);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            found.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        });

        foreach (var item in found) _programs.Add(item);
        TxtStatus.Text = UiLanguage.L($"扫描完成：发现 {found.Count} 个程序", $"Scan done: {found.Count} programs found");
        BtnScan.IsEnabled = true;
    }

    private void ListPrograms_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = ListPrograms.SelectedItem as ProgramItem;
        bool has = _selected != null;
        BtnUninstall.IsEnabled = has && !string.IsNullOrWhiteSpace(_selected?.UninstallString);
        BtnScanResidue.IsEnabled = has;
        _residues.Clear();
        BtnDeleteResidue.IsEnabled = false;
    }

    // ===== 卸载 =====

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || string.IsNullOrWhiteSpace(_selected.UninstallString)) return;

        var cmd = _selected.UninstallString.Trim();

        // 安全：把注册表中的 UninstallString 完整展示给用户，
        // 并阻止恶意程序通过解释器（cmd/powershell 等）隐藏执行的卸载命令（安全审计建议 P0）。
        string? reason = CheckUninstallCommand(cmd);
        if (reason != null)
        {
            MessageBox.Show(Window.GetWindow(this),
                UiLanguage.L($"已阻止启动此卸载命令，原因：{reason}\n\n命令内容：\n{cmd}",
                    $"Blocked launching this uninstall command, reason: {reason}\n\nCommand:\n{cmd}"),
                UiLanguage.L("已阻止不安全卸载命令", "Unsafe uninstall command blocked"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtStatus.Text = UiLanguage.L("已阻止不安全卸载命令", "Blocked unsafe uninstall command");
            return;
        }

        var r = MessageBox.Show(Window.GetWindow(this),
            UiLanguage.L($"确定要卸载「{_selected.DisplayName}」吗？\n\n将执行以下命令：\n{cmd}",
                $"Uninstall '{_selected.DisplayName}'?\n\nThe following command will run:\n{cmd}"),
            UiLanguage.L("确认卸载", "Confirm Uninstall"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _selected.UninstallString,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            TxtStatus.Text = UiLanguage.L("已启动卸载程序，请按提示操作", "Uninstaller launched, follow the prompts");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("卸载启动失败：" + ex.Message, "Failed to launch: " + ex.Message);
        }
    }

    /// <summary>
    /// 检查注册表 UninstallString 是否属于危险模式。
    /// 正常的卸载命令通常是 C:\...\uninstall.exe /x 或 msiexec /x {GUID}；
    /// 恶意程序常以 cmd /c start / powershell -c 等解释器包裹任意命令。
    /// 返回 null 表示安全，否则返回阻止原因。
    /// </summary>
    private static string? CheckUninstallCommand(string cmd)
    {
        try
        {
            var low = cmd.ToLowerInvariant();

            // 1) 必须以引号包裹的可执行文件路径或 exe/msi 直接路径开头；禁止裸解释器调用
            //    提取第一段（引号内或第一个空格前）
            string first;
            cmd = cmd.TrimStart();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                first = end > 1 ? cmd.Substring(1, end - 1) : cmd.Substring(1);
            }
            else
            {
                int sp = cmd.IndexOf(' ');
                first = sp > 0 ? cmd.Substring(0, sp) : cmd;
            }

            var exe = Path.GetFileName(first).ToLowerInvariant();

            // 禁止经系统解释器间接执行
            string[] dangerousExes = { "cmd.exe", "cmd", "powershell.exe", "powershell",
                "pwsh.exe", "pwsh", "wscript.exe", "cscript.exe", "mshta.exe",
                "rundll32.exe", "regsvr32.exe", "conhost.exe" };
            if (dangerousExes.Contains(exe))
                return "命令通过系统解释器执行，可能包含隐藏的恶意操作";

            // 2) 命令中若包含明显恶意关键字，直接阻止
            string[] maliciousPatterns =
            {
                "powershell", "cmd.exe", " wget ", " iwr ", " irm ", "bitsadmin",
                "certutil", "start http", "start https", " download",
                " -enc ", " -encodedcommand ", " -windowstyle hidden", " -w hidden"
            };
            foreach (var p in maliciousPatterns)
            {
                if (low.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return "命令包含可疑的下载/隐藏执行特征";
            }
        }
        catch
        {
            return "命令格式无法解析";
        }
        return null;
    }

    // ===== 扫描残留 =====

    private async void BtnScanResidue_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        BtnScanResidue.IsEnabled = false;
        TxtStatus.Text = UiLanguage.L("正在扫描残留文件…", "Scanning leftovers…");
        _residues.Clear();
        BtnDeleteResidue.IsEnabled = false;

        var name = _selected.DisplayName;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        var found = new System.Collections.Generic.List<string>();
        await Task.Run(() =>
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        var dn = Path.GetFileName(dir) ?? "";
                        if (dn.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (dn.Length > 3 && name.IndexOf(dn, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            lock (found) found.Add(dir);
                        }
                    }
                }
                catch { }
            }
            // 注册表卸载项本身也视作残留（可选删除）
            if (!string.IsNullOrWhiteSpace(_selected!.KeyPath))
                lock (found) found.Add("REG:" + _selected.KeyPath);
        });

        foreach (var f in found) _residues.Add(new ResidueItem { Path = f, IsChecked = false });
        TxtStatus.Text = UiLanguage.L($"扫描完成：发现 {found.Count} 处残留", $"Scan done: {found.Count} leftover(s) found");
        BtnScanResidue.IsEnabled = true;
        BtnDeleteResidue.IsEnabled = _residues.Count > 0;
    }

    // ===== 删除残留 =====

    private async void BtnDeleteResidue_Click(object sender, RoutedEventArgs e)
    {
        var sel = _residues.Where(r => r.IsChecked).ToList();
        if (sel.Count == 0) return;

        var r = MessageBox.Show(Window.GetWindow(this),
            UiLanguage.L($"确定要删除选中的 {sel.Count} 项残留吗？此操作不可恢复！", $"Delete the {sel.Count} selected leftover(s)? This cannot be undone!"),
            UiLanguage.L("确认删除", "Confirm Delete"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        int ok = 0, fail = 0;
        await Task.Run(() =>
        {
            foreach (var item in sel)
            {
                try
                {
                    if (item.Path.StartsWith("REG:"))
                    {
                        var keyPath = item.Path.Substring(4);
                        DeleteRegistryKeyTree(keyPath);
                    }
                    else if (Directory.Exists(item.Path))
                        Directory.Delete(item.Path, true);
                    else if (File.Exists(item.Path))
                        File.Delete(item.Path);
                    else
                        continue;
                    Dispatcher.Invoke(() => _residues.Remove(item));
                    ok++;
                }
                catch
                {
                    fail++;
                }
            }
        });

        TxtStatus.Text = UiLanguage.L($"删除完成：成功 {ok}，失败 {fail}", $"Done: {ok} ok, {fail} failed");
        BtnDeleteResidue.IsEnabled = _residues.Any(x => x.IsChecked);
    }

    private static void DeleteRegistryKeyTree(string fullPath)
    {
        // fullPath 形如 HKEY_LOCAL_MACHINE\Software\...
        var idx = fullPath.IndexOf('\\');
        var hiveName = fullPath.Substring(0, idx);
        var sub = fullPath.Substring(idx + 1);
        RegistryKey? hive = hiveName switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            _ => null
        };
        if (hive == null) return;
        using var key = hive.OpenSubKey(sub, true);
        if (key == null) return;
        // 删除子项（递归），再删除自身
        foreach (var child in key.GetSubKeyNames())
        {
            try { key.DeleteSubKeyTree(child, true); } catch { }
        }
        // 删除本键（需回到父键删除）
        var lastSep = sub.LastIndexOf('\\');
        var parentPath = sub.Substring(0, lastSep);
        var leaf = sub.Substring(lastSep + 1);
        using var parent = hive.OpenSubKey(parentPath, true);
        parent?.DeleteSubKeyTree(leaf, true);
    }
}
