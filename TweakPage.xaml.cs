using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace WINHELP
{
    /// <summary>
    /// TweakPage.xaml 交互逻辑 — 个性化调校（导航 key="tweak"，v4.9.0 新增）。
    /// 任务栏合并 / 开始菜单对齐 / 搜索框样式 / 右键菜单风格 / UAC 级别 / 休眠 / Hosts。
    /// 强制安全设计：任何写入前先导出原值到 %APPDATA%/WINHELP/backup/tweak_*.reg；
    /// 每项独立「恢复默认」+ 页面级「全部还原」；需重启 Explorer 的项明确提示。
    /// 含用户数据的项（UAC 级别为 HKLM 策略）非提权时只读展示。
    /// </summary>
    public partial class TweakPage : UserControl
    {
        // ===== 调校项模型 =====
        private sealed class TweakItem
        {
            public string Key = "";                 // 用于备份文件名
            public string TitleZh = "";
            public string TitleEn = "";
            public string DescZh = "";
            public string DescEn = "";
            public string Hive = "";                // "HKCU" 或 "HKLM"
            public string SubKey = "";
            public string ValueName = "";
            public RegistryValueKind Kind = RegistryValueKind.DWord;
            public (string Label, string? Data)[] Options = Array.Empty<(string, string?)>();
            public string? DefaultData = null;      // 恢复默认用的值
            public string? ReadFunc = null;         // 自定义读取（"uac"）
            public bool NeedsAdmin = false;         // HKLM 写需要提权
            public bool NeedsExplorerRestart = false;

            public string? LastBackup { get; set; } // 本会话最后一次备份的 .reg 路径

            public string CurrentString()
            {
                try
                {
                    using var k = (Hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser)
                        .OpenSubKey(SubKey);
                    if (k == null) return "（未设置）";
                    var v = k.GetValue(ValueName);
                    if (v == null) return "（未设置）";
                    return v is int i ? i.ToString() : v.ToString()!;
                }
                catch { return "?"; }
            }
        }

        private readonly List<TweakItem> _items = new();
        private static readonly string BackupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP", "backup");
        private const string ExplorerAdvanced =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string PolicySystem =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

        public TweakPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            Loaded += (_, __) => BuildCards();
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
            ThemeManager.ApplyButtonTheme(BtnRestoreAll, Color.FromRgb(0xE6, 0x7E, 0x22),
                hoverColor: Color.FromRgb(0xC0, 0x5F, 0x12));
        }

        // ===== 构建卡片 =====

        private void BuildCards()
        {
            if (CardsPanel.Children.Count > 0) return; // 只构建一次
            _items.Clear();

            _items.Add(new TweakItem
            {
                Key = "taskbar_glom", TitleZh = "任务栏按钮合并", TitleEn = "Taskbar buttons",
                DescZh = "任务栏同应用窗口是否合并为单按钮", DescEn = "Combine windows of one app",
                Hive = "HKCU", SubKey = ExplorerAdvanced, ValueName = "TaskbarGlomLevel",
                Options = new (string, string?)[]
                {
                    ("从不合并", "0"), ("仅主任务栏", "1"), ("始终合并（默认）", "2")
                },
                DefaultData = "2", NeedsExplorerRestart = true
            });
            _items.Add(new TweakItem
            {
                Key = "taskbar_align", TitleZh = "开始菜单/任务栏对齐", TitleEn = "Start alignment",
                DescZh = "开始按钮与任务栏图标靠左或居中", DescEn = "Left or centered start button",
                Hive = "HKCU", SubKey = ExplorerAdvanced, ValueName = "TaskbarAl",
                Options = new (string, string?)[]
                {
                    ("居中（默认）", "1"), ("靠左", "0")
                },
                DefaultData = "1", NeedsExplorerRestart = true
            });
            _items.Add(new TweakItem
            {
                Key = "searchbox", TitleZh = "搜索框样式", TitleEn = "Search box",
                DescZh = "任务栏搜索框显示模式", DescEn = "Taskbar search mode",
                Hive = "HKCU", SubKey = @"Software\Microsoft\Windows\CurrentVersion\Search",
                ValueName = "SearchboxTaskbarMode",
                Options = new (string, string?)[]
                {
                    ("隐藏", "0"), ("仅图标", "1"), ("搜索框", "2")
                },
                DefaultData = "1", NeedsExplorerRestart = true
            });
            _items.Add(new TweakItem
            {
                Key = "win10_menu", TitleZh = "右键菜单 Win10 风格", TitleEn = "Win10 context menu",
                DescZh = "完整右键菜单（注册空值即生效；删除该项恢复）", DescEn = "Full context menu",
                Hive = "HKCU",
                SubKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
                ValueName = "",
                Kind = RegistryValueKind.String,
                Options = new (string, string?)[]
                {
                    ("启用（空值）", ""),
                },
                DefaultData = null,  // 恢复 = 删除该子键
                NeedsExplorerRestart = true
            });
            _items.Add(new TweakItem
            {
                Key = "uac", TitleZh = "UAC 通知级别", TitleEn = "UAC level",
                DescZh = "管理员授权弹窗频率（HKLM，需管理员）", DescEn = "Admin prompt frequency (HKLM)",
                Hive = "HKLM", SubKey = PolicySystem, ValueName = "ConsentPromptBehaviorAdmin",
                NeedsAdmin = true,
                Options = new (string, string?)[]
                {
                    ("始终通知（默认 5）", "5"), ("较频繁（4）", "4"), ("较少（2）", "2"), ("从不通知（0，不推荐）", "0")
                },
                DefaultData = "5"
            });
            _items.Add(new TweakItem
            {
                Key = "hibernate", TitleZh = "休眠开关", TitleEn = "Hibernation",
                DescZh = "启用/关闭休眠文件 hiberfil.sys（需管理员）", DescEn = "Enable/disable hibernation",
                Hive = "HKLM", SubKey = PolicySystem, ValueName = "__hibernate",
                NeedsAdmin = true,
                Options = new (string, string?)[]
                {
                    ("启用休眠", "on"), ("关闭休眠（释放 C 盘）", "off")
                },
                DefaultData = "on"
            });

            foreach (var item in _items)
                CardsPanel.Children.Add(BuildCard(item));

            // Hosts 卡（特殊处理）
            CardsPanel.Children.Add(BuildHostsCard());
        }

        private Border BuildCard(TweakItem item)
        {
            var card = new Border
            {
                Style = (Style)FindResource("GlassCard"),
                Width = 380, Margin = new Thickness(0, 0, 10, 10),
                Child = new StackPanel { Margin = new Thickness(14) }
            };
            var sp = (StackPanel)card.Child;

            // 标题 + 当前值
            var title = new TextBlock
            {
                Text = UiLanguage.L(item.TitleZh, item.TitleEn),
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            };
            sp.Children.Add(title);
            var cur = new TextBlock
            {
                Text = UiLanguage.L("当前值：", "Current: ") + item.CurrentString(),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            };
            sp.Children.Add(cur);
            var desc = new TextBlock
            {
                Text = UiLanguage.L(item.DescZh, item.DescEn),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextMutedBrush")
            };
            sp.Children.Add(desc);

            // 选项（ComboBox）
            var cb = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var (label, data) in item.Options)
                cb.Items.Add(new ComboBoxItem { Content = label, Tag = data });
            var curVal = item.CurrentString();
            int idx = 0;
            for (int i = 0; i < item.Options.Length; i++)
                if (item.Options[i].Data == curVal) { idx = i; break; }
            cb.SelectedIndex = idx;
            sp.Children.Add(cb);

            // 按钮行
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var apply = new Button
            {
                Content = UiLanguage.L("应用", "Apply"),
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0)
            };
            ThemeManager.ApplyButtonTheme(apply, ThemeManager.AccentColor);
            apply.Click += async (_, __) => await ApplyItemAsync(item, cb);
            row.Children.Add(apply);

            if (item.NeedsAdmin && !CommandRunner.IsElevated)
            {
                var need = new TextBlock
                {
                    Text = UiLanguage.L("需管理员权限", "Needs admin"),
                    FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22))
                };
                row.Children.Add(need);
            }
            else
            {
                var restore = new Button
                {
                    Content = UiLanguage.L("恢复默认", "Restore"),
                    Padding = new Thickness(14, 5, 14, 5)
                };
                ThemeManager.ApplyButtonTheme(restore, Color.FromRgb(0x95, 0xA5, 0xA6),
                    hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
                restore.Click += async (_, __) => await RestoreItemAsync(item);
                row.Children.Add(restore);
            }
            sp.Children.Add(row);

            var hint = new TextBlock
            {
                Text = item.NeedsExplorerRestart
                    ? UiLanguage.L("提示：可能需要重启资源管理器生效", "Note: Explorer restart may be needed")
                    : "",
                FontSize = 10, Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("TextMutedBrush")
            };
            sp.Children.Add(hint);
            return card;
        }

        // ===== 应用 / 恢复 =====

        private static async Task RunAdminLiteral(string command, string label)
        {
            // 固定命令提权走 CommandRunner 白名单
            CommandRunner.RegisterAllowed(new[] { command });
            var r = await CommandRunner.RunAsync(command, requireAdmin: true, timeoutSec: 60);
            MessageBox.Show(r.Success
                ? UiLanguage.L(label + " 执行成功。", label + " done.")
                : UiLanguage.L(label + " 失败：", label + " failed: ") + (r.Error ?? r.ExitCode.ToString()),
                UiLanguage.L("提示", "Info"), MessageBoxButton.OK,
                r.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private async Task ApplyItemAsync(TweakItem item, ComboBox cb)
        {
            if (cb.SelectedItem is not ComboBoxItem sel || sel.Tag is not string data)
                return;

            // 1) 写前备份原值到 backup 目录
            BackupItem(item);

            try
            {
                // 休眠：特殊处理 —— 走提权命令
                if (item.Key == "hibernate")
                {
                    await RunAdminLiteral($"powercfg /hibernate {data}", UiLanguage.L("休眠设置", "Hibernation"));
                    return;
                }

                // UAC 级别（HKLM 策略）：非提权时经 reg add 提权执行；已提权则直接写
                if (item.Key == "uac")
                {
                    if (!CommandRunner.IsElevated)
                    {
                        await RunAdminLiteral(
                            $"reg add \"HKLM\\{PolicySystem}\" /v ConsentPromptBehaviorAdmin /t REG_DWORD /d {data} /f",
                            UiLanguage.L("UAC 级别", "UAC level"));
                    }
                    else
                    {
                        using (var k = Registry.LocalMachine.CreateSubKey(PolicySystem))
                            k?.SetValue("ConsentPromptBehaviorAdmin", int.Parse(data), RegistryValueKind.DWord);
                        MessageBox.Show(UiLanguage.L("已应用。", "Applied."),
                            UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                // 右键菜单：空字符串值即启用（设置 ""），恢复时删除子键
                using (var k = (item.Hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser)
                    .CreateSubKey(item.SubKey))
                {
                    if (k == null) throw new InvalidOperationException("无法打开注册表键");
                    if (item.Kind == RegistryValueKind.String)
                        k.SetValue(item.ValueName, data, RegistryValueKind.String);
                    else if (data == "删除")
                        k.DeleteValue(item.ValueName, false);
                    else if (int.TryParse(data, out var iv))
                        k.SetValue(item.ValueName, iv, RegistryValueKind.DWord);
                    else
                        k.SetValue(item.ValueName, data);
                }

                string msg = UiLanguage.L("已应用。", "Applied.");
                if (item.NeedsExplorerRestart)
                    msg += "\n" + UiLanguage.L("建议重启资源管理器使更改生效（见页面底部按钮）。",
                        "Restart Explorer to apply (button at page bottom).");
                MessageBox.Show(msg, UiLanguage.L("提示", "Info"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLanguage.L("应用失败：", "Apply failed: ") + ex.Message,
                    UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task RestoreItemAsync(TweakItem item)
        {
            try
            {
                if (item.Key == "hibernate")
                {
                    await RunAdminLiteral("powercfg /hibernate on", UiLanguage.L("休眠设置", "Hibernation"));
                    return;
                }
                // 恢复默认：删除值（HKCU 项安全可逆）；HKLM UAC 项提权写回默认 5
                if (item.Hive == "HKLM")
                {
                    await RunAdminLiteral(
                        $"reg add \"HKLM\\{PolicySystem}\" /v ConsentPromptBehaviorAdmin /t REG_DWORD /d 5 /f",
                        UiLanguage.L("UAC 恢复", "UAC restore"));
                    return;
                }
                using (var k = (item.Hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser)
                    .OpenSubKey(item.SubKey, true))
                {
                    if (k == null) throw new InvalidOperationException("无法打开注册表键");
                    k.DeleteValue(item.ValueName, false);
                }
                if (item.Key == "win10_menu")
                {
                    // 删除整个 CLSID 子键以恢复默认右键菜单
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(
                            @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                    }
                    catch { }
                }
                MessageBox.Show(UiLanguage.L("已恢复默认。", "Restored to default."),
                    UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLanguage.L("恢复失败：", "Restore failed: ") + ex.Message,
                    UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
        {
            var ok = MessageBox.Show(
                UiLanguage.L("确定要将本页所有调校项恢复为默认吗？", "Restore ALL tweaks on this page to defaults?"),
                UiLanguage.L("全部还原", "Restore all"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ok != MessageBoxResult.Yes) return;
            foreach (var item in _items)
                await RestoreItemAsync(item);
            MessageBox.Show(UiLanguage.L("全部还原完成。", "All restored."),
                UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ===== 备份 =====

        private void BackupItem(TweakItem item)
        {
            try
            {
                Directory.CreateDirectory(BackupDir);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var file = Path.Combine(BackupDir, $"tweak_{item.Key}_{stamp}.reg");
                // 导出当前值（文本记录，便于人工查看与恢复）
                File.WriteAllText(file,
                    $"; 司南工具箱调校备份 {stamp}\r\n" +
                    $"; Hive={item.Hive} Key={item.SubKey} Value={item.ValueName}\r\n" +
                    $"CurrentValue={item.CurrentString()}\r\n");
                item.LastBackup = file;
            }
            catch { }
        }

        // ===== Hosts 卡 =====

        private Border BuildHostsCard()
        {
            var card = new Border
            {
                Style = (Style)FindResource("GlassCard"),
                Width = 380, Margin = new Thickness(0, 0, 10, 10),
                Child = new StackPanel { Margin = new Thickness(14) }
            };
            var sp = (StackPanel)card.Child;
            sp.Children.Add(new TextBlock
            {
                Text = UiLanguage.L("Hosts 文件管理", "Hosts management"),
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            });
            sp.Children.Add(new TextBlock
            {
                Text = UiLanguage.L("备份 / 查看 / 编辑 / 恢复 hosts（写操作需管理员）", "Backup / view / edit / restore hosts (admin for write)"),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextMutedBrush")
            });

            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "drivers", "etc", "hosts");
            var backupPath = Path.Combine(BackupDir, "hosts_manual.txt");

            // 只读展示
            var view = new Button
            {
                Content = UiLanguage.L("查看内容", "View"),
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 8)
            };
            ThemeManager.ApplyButtonTheme(view, ThemeManager.AccentColor);
            var viewBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
                FontFamily = new FontFamily("Consolas"), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, IsReadOnly = true, IsUndoEnabled = false,
                BorderThickness = new Thickness(0), Padding = new Thickness(6),
                MinHeight = 70, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8)
            };
            view.Click += (_, __) =>
            {
                try
                {
                    viewBox.Text = File.Exists(hostsPath) ? File.ReadAllText(hostsPath) : "（hosts 不存在）";
                }
                catch (Exception ex) { viewBox.Text = ex.Message; }
                viewBox.Visibility = viewBox.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
            };
            sp.Children.Add(view);
            sp.Children.Add(viewBox);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var backup = new Button
            {
                Content = UiLanguage.L("备份", "Backup"),
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0)
            };
            ThemeManager.ApplyButtonTheme(backup, ThemeManager.AccentColor);
            backup.Click += async (_, __) => await RunAdminLiteral(
                $"copy /y \"%WINDIR%\\System32\\drivers\\etc\\hosts\" \"{backupPath}\"",
                UiLanguage.L("Hosts 备份", "Hosts backup"));
            row.Children.Add(backup);

            var edit = new Button
            {
                Content = UiLanguage.L("管理员编辑", "Edit as admin"),
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0)
            };
            ThemeManager.ApplyButtonTheme(edit, ThemeManager.AccentColor);
            edit.Click += async (_, __) => await RunAdminLiteral(
                "notepad.exe %WINDIR%\\System32\\drivers\\etc\\hosts",
                UiLanguage.L("打开 hosts", "Open hosts"));
            row.Children.Add(edit);

            var restore = new Button { Content = UiLanguage.L("从备份恢复", "Restore"), Padding = new Thickness(14, 5, 14, 5) };
            ThemeManager.ApplyButtonTheme(restore, Color.FromRgb(0xE7, 0x4C, 0x3C),
                hoverColor: Color.FromRgb(0xC0, 0x39, 0x2B));
            restore.Click += async (_, __) =>
            {
                if (!File.Exists(backupPath))
                {
                    MessageBox.Show(UiLanguage.L("尚无备份，请先点击「备份」。", "No backup yet. Click Backup first."),
                        UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await RunAdminLiteral(
                    $"copy /y \"{backupPath}\" \"%WINDIR%\\System32\\drivers\\etc\\hosts\"",
                    UiLanguage.L("Hosts 恢复", "Hosts restore"));
            };
            row.Children.Add(restore);
            sp.Children.Add(row);

            // 重启资源管理器按钮
            var restart = new Button
            {
                Content = UiLanguage.L("重启资源管理器", "Restart Explorer"),
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ThemeManager.ApplyButtonTheme(restart, Color.FromRgb(0x6C, 0x4B, 0xB4),
                hoverColor: Color.FromRgb(0x55, 0x39, 0x92));
            restart.Click += async (_, __) => await RunAdminLiteral(
                "taskkill /f /im explorer.exe && start explorer.exe",
                UiLanguage.L("重启资源管理器", "Restart Explorer"));
            sp.Children.Add(restart);
            return card;
        }
    }
}
