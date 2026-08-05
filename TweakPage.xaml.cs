using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace WINHELP
{
    /// <summary>
    /// TweakPage.xaml 交互逻辑 — 个性化调校（导航 key="tweak"，v4.9.0 新增，v5.0.0 全面修复）。
    /// 任务栏合并 / 开始菜单对齐 / 搜索框样式 / 右键菜单风格 / UAC 级别 / 休眠 / Hosts。
    /// 强制安全设计（v5.0.0 修复闭环）：每次写入前备份真实原值到 %APPDATA%/WINHELP/backup/tweak_{key}.json，
    /// 「恢复默认」优先从备份回退原值（无备份才回退硬编码默认）；每项独立「恢复默认」+ 页面级「全部还原」；
    /// 需重启 Explorer 的项明确提示；休眠状态按 hiberfil.sys 真实读取；
    /// 提权运行时 HKCU 写入经 HKEY_USERS\&lt;SID&gt; 保证落在当前登录用户（修复"改了没反应"）。
    /// </summary>
    public partial class TweakPage : UserControl
    {
        // ===== 调校项模型 =====
        private sealed class TweakItem
        {
            public string Key = "";                 // 用于备份文件名与恢复分发
            public string TitleZh = "";
            public string TitleEn = "";
            public string DescZh = "";
            public string DescEn = "";
            public string Hive = "";                // "HKCU" 或 "HKLM"
            public string SubKey = "";
            public string ValueName = "";
            public RegistryValueKind Kind = RegistryValueKind.DWord;
            public (string Label, string? Data)[] Options = Array.Empty<(string, string?)>();
            public string? DefaultData = null;      // 无备份时回退的默认值
            public bool NeedsAdmin = false;         // HKLM 写需要提权
            public bool NeedsExplorerRestart = false;

            // 卡片引用：应用/恢复成功后即时刷新（v5.0.0）
            public TextBlock? CurText;
            public ComboBox? Combo;

            /// <summary>当前实际值（休眠项按 hiberfil.sys 真实状态；不存在返回 ""）</summary>
            public string CurrentString()
            {
                try
                {
                    if (Key == "hibernate") return HibernationEnabled() ? "on" : "off";
                    using var k = (Hive == "HKLM" ? Registry.LocalMachine : OpenHkcuSubKey(SubKey, false));
                    if (k == null) return "";
                    var v = k.GetValue(ValueName);
                    if (v == null) return "";
                    if (v is string s) return s.Length == 0 ? "" : s;
                    return v is int i ? i.ToString() : v.ToString()!;
                }
                catch { return "?"; }
            }
        }

        /// <summary>备份记录（JSON，v5.0.0：记录真实原值，恢复时优先回退原值）</summary>
        private sealed class TweakBackup
        {
            public string Key { get; set; } = "";
            public string Hive { get; set; } = "";
            public string SubKey { get; set; } = "";
            public string ValueName { get; set; } = "";
            public string Kind { get; set; } = "";
            public bool OriginalExists { get; set; }
            public string? OriginalValue { get; set; }   // null 表示原值不存在
            public string? HibernationState { get; set; } // "on"/"off"，仅休眠用
        }

        private readonly List<TweakItem> _items = new();
        private bool _built;
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
            UiLanguage.Changed += () => Dispatcher.Invoke(() => { _built = false; RebuildAll(); });
            Loaded += (_, __) => BuildCards();
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
            ThemeManager.ApplyButtonTheme(BtnRestoreAll, Color.FromRgb(0xE6, 0x7E, 0x22),
                hoverColor: Color.FromRgb(0xC0, 0x5F, 0x12));
        }

        // ===== HKCU 读取/写入（提权时经 HKEY_USERS\<SID> 保证写当前登录用户，修复 Bug 9） =====

        private static RegistryKey? OpenHkcuSubKey(string subKey, bool writable)
        {
            if (CommandRunner.IsElevated)
            {
                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
                if (!string.IsNullOrEmpty(sid))
                    return Registry.Users.OpenSubKey(sid + @"\" + subKey, writable);
            }
            return Registry.CurrentUser.OpenSubKey(subKey, writable);
        }

        private static RegistryKey? CreateHkcuSubKey(string subKey)
        {
            if (CommandRunner.IsElevated)
            {
                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
                if (!string.IsNullOrEmpty(sid))
                    return Registry.Users.CreateSubKey(sid + @"\" + subKey);
            }
            return Registry.CurrentUser.CreateSubKey(subKey);
        }

        /// <summary>休眠是否启用（以 hiberfil.sys 存在性为准，无需提权）</summary>
        private static bool HibernationEnabled() =>
            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "hiberfil.sys"));

        // ===== 构建卡片 =====

        private void BuildCards()
        {
            if (_built) return;
            _built = true;
            RebuildAll();
        }

        /// <summary>重建全部卡片（语言切换 / 首次加载）</summary>
        private void RebuildAll()
        {
            if (CardsPanel == null) return;
            CardsPanel.Children.Clear();
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
                DescZh = "启用完整右键菜单（注册空值即生效；恢复即还原系统默认）", DescEn = "Full context menu (empty value enables; restore returns to default)",
                Hive = "HKCU",
                SubKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
                ValueName = "",
                Kind = RegistryValueKind.String,
                Options = new (string, string?)[]
                {
                    ("启用完整右键菜单", "enable"),
                },
                DefaultData = null,  // 恢复 = 删除该子键
                NeedsExplorerRestart = true
            });
            _items.Add(new TweakItem
            {
                Key = "uac", TitleZh = "UAC 通知级别", TitleEn = "UAC level",
                DescZh = "管理员授权弹窗频率（系统级设置，需管理员权限）", DescEn = "Admin prompt frequency (system-wide, needs admin)",
                Hive = "HKLM", SubKey = PolicySystem, ValueName = "ConsentPromptBehaviorAdmin",
                NeedsAdmin = true,
                Options = new (string, string?)[]
                {
                    ("始终通知（默认）", "5"), ("较频繁", "4"), ("较少", "2"), ("从不通知（不推荐）", "0")
                },
                DefaultData = "5"
            });
            _items.Add(new TweakItem
            {
                Key = "hibernate", TitleZh = "休眠开关", TitleEn = "Hibernation",
                DescZh = "启用/关闭休眠文件 hiberfil.sys（需管理员权限，恢复会回到你原来的状态）", DescEn = "Enable/disable hibernation (needs admin; restore returns to your previous state)",
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
                Text = UiLanguage.L("当前值：", "Current: ") + ItemLabel(item, item.CurrentString()),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            };
            sp.Children.Add(cur);
            item.CurText = cur; // 应用后刷新引用
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
            cb.SelectedIndex = MatchIndex(item);
            sp.Children.Add(cb);
            item.Combo = cb;

            // 按钮行
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var apply = new Button
            {
                Content = UiLanguage.L("应用", "Apply"),
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0)
            };
            ThemeManager.ApplyButtonTheme(apply, ThemeManager.AccentColor);
            apply.Click += async (_, __) => await ApplyItemAsync(item);
            row.Children.Add(apply);

            // v5.0.0（修复 Bug 4）：需管理员项在非提权时也显示「恢复默认」，点击走提权恢复
            var restore = new Button
            {
                Content = UiLanguage.L("恢复默认", "Restore"),
                Padding = new Thickness(14, 5, 14, 5)
            };
            ThemeManager.ApplyButtonTheme(restore, Color.FromRgb(0x95, 0xA5, 0xA6),
                hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
            restore.Click += async (_, __) => await RestoreItemAsync(item);
            row.Children.Add(restore);

            if (item.NeedsAdmin && !CommandRunner.IsElevated)
            {
                row.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("需管理员权限", "Needs admin"),
                    FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22))
                });
            }
            sp.Children.Add(row);

            var hint = new TextBlock
            {
                Text = item.NeedsExplorerRestart
                    ? UiLanguage.L("提示：应用后请点击页面底部「重启资源管理器」生效", "Note: click Restart Explorer at page bottom after applying")
                    : "",
                FontSize = 10, Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("TextMutedBrush")
            };
            sp.Children.Add(hint);
            return card;
        }

        /// <summary>选项文本：当前值 → 对应选项标签（找不到就显示原值）</summary>
        private static string ItemLabel(TweakItem item, string val)
        {
            foreach (var (label, data) in item.Options)
                if (data == val) return label;
            return string.IsNullOrEmpty(val) ? UiLanguage.L("（未设置）", "(unset)") : val;
        }

        /// <summary>当前值在选项中的索引</summary>
        private static int MatchIndex(TweakItem item)
        {
            string curVal = item.CurrentString();
            for (int i = 0; i < item.Options.Length; i++)
                if (item.Options[i].Data == curVal) return i;
            return 0;
        }

        /// <summary>应用/恢复成功后刷新卡片当前值与选中项（v5.0.0）</summary>
        private void RefreshCard(TweakItem item)
        {
            if (item.CurText != null)
                item.CurText.Text = UiLanguage.L("当前值：", "Current: ") + ItemLabel(item, item.CurrentString());
            if (item.Combo != null)
                item.Combo.SelectedIndex = MatchIndex(item);
        }

        // ===== 备份 =====

        /// <summary>写前备份真实原值到 backup/tweak_{key}.json（v5.0.0：JSON 而非伪 .reg，恢复可回退原值）</summary>
        private void BackupItem(TweakItem item)
        {
            try
            {
                Directory.CreateDirectory(BackupDir);
                var b = new TweakBackup
                {
                    Key = item.Key,
                    Hive = item.Hive,
                    SubKey = item.SubKey,
                    ValueName = item.ValueName,
                    Kind = item.Kind.ToString(),
                    HibernationState = item.Key == "hibernate"
                        ? (HibernationEnabled() ? "on" : "off") : null
                };
                using var k = (item.Hive == "HKLM" ? Registry.LocalMachine : OpenHkcuSubKey(item.SubKey, false));
                if (k != null)
                {
                    var v = k.GetValue(item.ValueName);
                    b.OriginalExists = v != null;
                    if (v != null)
                        b.OriginalValue = v is byte[] bytes ? Convert.ToBase64String(bytes) : v.ToString();
                }
                File.WriteAllText(Path.Combine(BackupDir, $"tweak_{item.Key}.json"),
                    JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 备份失败不阻断主流程 */ }
        }

        /// <summary>读取最近一次备份；无则返回 null</summary>
        private static TweakBackup? LoadBackup(string key)
        {
            try
            {
                var path = Path.Combine(BackupDir, $"tweak_{key}.json");
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<TweakBackup>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        // ===== 提权命令（静默，由调用方决定提示） =====

        /// <summary>
        /// 提权执行注册表/电源命令。v5.2.0 安全加固：命令必须先通过值域白名单校验
        /// （data 只能取编译期预定义的值），再按固定模板拼装，杜绝未来新增项时把用户输入拼进命令。
        /// </summary>
        private static async Task<CommandResult> RunAdminAsync(string command)
        {
            // 值域校验：只允许本页预定义的 UAC 级别(0..12) 与休眠状态(on/off) 拼入命令
            bool valid = false;
            if (command.StartsWith("powercfg /hibernate ", StringComparison.Ordinal))
            {
                valid = command is "powercfg /hibernate on" or "powercfg /hibernate off";
            }
            else if (command.StartsWith("reg add \"HKLM\\", StringComparison.Ordinal))
            {
                const string marker = "/v ConsentPromptBehaviorAdmin /t REG_DWORD /d ";
                int m = command.IndexOf(marker, StringComparison.Ordinal);
                if (m > 0 && command.EndsWith(" /f", StringComparison.Ordinal))
                {
                    int len = command.Length - m - marker.Length - 3; // 减去 " /f"
                    if (len > 0 && int.TryParse(command.Substring(m + marker.Length, len), out var v))
                        valid = v >= 0 && v <= 12;
                }
            }
            if (!valid)
            {
                return new CommandResult
                {
                    ExitCode = -4,
                    Error = UiLanguage.L("命令未通过安全校验，已拒绝执行。", "Command failed security validation and was rejected.")
                };
            }

            CommandRunner.RegisterAllowed(new[] { command });
            return await CommandRunner.RunAsync(command, requireAdmin: true, timeoutSec: 60);
        }

        // ===== 应用 =====

        private async Task ApplyItemAsync(TweakItem item)
        {
            if (item.Combo?.SelectedItem is not ComboBoxItem sel || sel.Tag is not string data)
                return;

            // 1) 写前备份真实原值
            BackupItem(item);

            try
            {
                // 休眠：走提权命令
                if (item.Key == "hibernate")
                {
                    var r = await RunAdminAsync($"powercfg /hibernate {data}");
                    MessageBox.Show(r.Success
                        ? UiLanguage.L("休眠设置已应用。", "Hibernation applied.")
                        : UiLanguage.L("休眠设置失败：", "Hibernation failed: ") + (r.Error ?? r.ExitCode.ToString()),
                        UiLanguage.L("提示", "Info"), MessageBoxButton.OK,
                        r.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    RefreshCard(item);
                    return;
                }

                // UAC 级别（HKLM 策略）：非提权经 reg add 提权；已提权直接写
                if (item.Key == "uac")
                {
                    // v5.2.0：UAC 通知级别属于系统安全策略，改错可能导致无法提权或提权全部放行，
                    // 属于高危代码执行项，执行前必须 5 连确认。
                    if (!RiskGuard.ConfirmHighRisk(
                            UiLanguage.L("修改 UAC 通知级别", "Change UAC notification level"),
                            $"reg add \"HKLM\\{PolicySystem}\" /v ConsentPromptBehaviorAdmin /t REG_DWORD /d {data} /f",
                            UiLanguage.L(
                                "这是系统级安全策略：设置为「从不通知」会让所有程序以管理员权限静默运行，大幅降低系统安全性；设置不当可能导致后续无法弹出授权窗口。",
                                "System-wide security policy: setting it to Never notify silently runs all programs elevated and greatly reduces security; wrong values can break elevation prompts.")))
                        return;

                    if (!CommandRunner.IsElevated)
                    {
                        var r = await RunAdminAsync(
                            $"reg add \"HKLM\\{PolicySystem}\" /v ConsentPromptBehaviorAdmin /t REG_DWORD /d {data} /f");
                        MessageBox.Show(r.Success
                            ? UiLanguage.L("已应用。", "Applied.")
                            : UiLanguage.L("应用失败：", "Apply failed: ") + (r.Error ?? r.ExitCode.ToString()),
                            UiLanguage.L("提示", "Info"), MessageBoxButton.OK,
                            r.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    }
                    else
                    {
                        using (var k = Registry.LocalMachine.CreateSubKey(PolicySystem))
                            k?.SetValue("ConsentPromptBehaviorAdmin", int.Parse(data), RegistryValueKind.DWord);
                        MessageBox.Show(UiLanguage.L("已应用。", "Applied."),
                            UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    RefreshCard(item);
                    return;
                }

                // 右键菜单 Win10 风格：创建 InprocServer32 并写空字符串即启用
                if (item.Key == "win10_menu")
                {
                    using var k = CreateHkcuSubKey(item.SubKey);
                    k?.SetValue("", "", RegistryValueKind.String);
                    MessageBox.Show(UiLanguage.L("已应用（建议重启资源管理器生效）。", "Applied (restart Explorer to take effect)."),
                        UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshCard(item);
                    return;
                }

                // 普通 HKCU 项：直写
                using (var k = CreateHkcuSubKey(item.SubKey))
                {
                    if (k == null) throw new InvalidOperationException("无法打开注册表键");
                    if (item.Kind == RegistryValueKind.String)
                        k.SetValue(item.ValueName, data, RegistryValueKind.String);
                    else if (int.TryParse(data, out var iv))
                        k.SetValue(item.ValueName, iv, RegistryValueKind.DWord);
                    else
                        k.SetValue(item.ValueName, data);
                }

                string msg = UiLanguage.L("已应用。", "Applied.");
                if (item.NeedsExplorerRestart)
                    msg += "\n" + UiLanguage.L("建议点击页面底部「重启资源管理器」使更改生效。",
                        "Click Restart Explorer at page bottom to apply.");
                MessageBox.Show(msg, UiLanguage.L("提示", "Info"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshCard(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLanguage.L("应用失败：", "Apply failed: ") + ex.Message,
                    UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ===== 恢复 =====

        /// <summary>恢复核心逻辑：优先从备份回退原值，无备份回退默认值；返回是否成功</summary>
        private async Task<bool> RestoreItemCoreAsync(TweakItem item)
        {
            try
            {
                var backup = LoadBackup(item.Key);

                // 休眠：回到备份记录的原状态（无备份回退"启用"）
                if (item.Key == "hibernate")
                {
                    var state = backup?.HibernationState ?? item.DefaultData ?? "on";
                    var r = await RunAdminAsync($"powercfg /hibernate {state}");
                    return r.Success;
                }

                // UAC（HKLM）：写回备份原值，无备份回退默认 5
                if (item.Key == "uac")
                {
                    var val = backup is { OriginalExists: true, OriginalValue: not null }
                        ? backup.OriginalValue : item.DefaultData ?? "5";
                    if (!CommandRunner.IsElevated)
                    {
                        var r = await RunAdminAsync(
                            $"reg add \"HKLM\\{PolicySystem}\" /v ConsentPromptBehaviorAdmin /t REG_DWORD /d {val} /f");
                        return r.Success;
                    }
                    using (var k = Registry.LocalMachine.CreateSubKey(PolicySystem))
                        k?.SetValue("ConsentPromptBehaviorAdmin", int.Parse(val), RegistryValueKind.DWord);
                    return true;
                }

                // 右键菜单：原值存在则写回，否则删除整个 CLSID 子键还原系统默认
                if (item.Key == "win10_menu")
                {
                    if (backup is { OriginalExists: true, OriginalValue: not null })
                    {
                        using var k = CreateHkcuSubKey(item.SubKey);
                        k?.SetValue("", backup.OriginalValue, RegistryValueKind.String);
                    }
                    else
                    {
                        try
                        {
                            Registry.CurrentUser.DeleteSubKeyTree(
                                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                        }
                        catch { }
                    }
                    return true;
                }

                // 普通 HKCU 项：原值存在则写回原值，不存在则删除值（回到系统默认）
                using (var k = CreateHkcuSubKey(item.SubKey))
                {
                    if (k == null) throw new InvalidOperationException("无法打开注册表键");
                    if (backup is { OriginalExists: true, OriginalValue: not null })
                    {
                        var raw = backup.OriginalValue;
                        if (item.Kind == RegistryValueKind.String)
                            k.SetValue(item.ValueName, raw, RegistryValueKind.String);
                        else if (int.TryParse(raw, out var iv))
                            k.SetValue(item.ValueName, iv, RegistryValueKind.DWord);
                        else
                            k.SetValue(item.ValueName, raw);
                    }
                    else
                    {
                        k.DeleteValue(item.ValueName, false);
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>单项恢复：带结果弹窗 + 刷新卡片</summary>
        private async Task RestoreItemAsync(TweakItem item)
        {
            bool ok = await RestoreItemCoreAsync(item);
            RefreshCard(item);
            MessageBox.Show(ok
                ? UiLanguage.L("已恢复。", "Restored.")
                : UiLanguage.L("恢复失败（可能被拒绝授权或命令执行失败）。", "Restore failed (denied or command error)."),
                UiLanguage.L("提示", "Info"), MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        /// <summary>全部还原：逐项执行并统计成败（v5.0.0 修复 Bug 10：不再无条件"全部完成"）</summary>
        private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
        {
            var ok = MessageBox.Show(
                UiLanguage.L("确定要将本页所有调校项恢复为默认吗？", "Restore ALL tweaks on this page to defaults?"),
                UiLanguage.L("全部还原", "Restore all"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ok != MessageBoxResult.Yes) return;

            int success = 0, failed = 0;
            foreach (var item in _items)
            {
                bool r = await RestoreItemCoreAsync(item);
                RefreshCard(item);
                if (r) success++; else failed++;
            }
            string msg = failed == 0
                ? UiLanguage.L(string.Format("全部还原完成：{0} 项全部成功。", success),
                    string.Format("All restored: {0} item(s) succeeded.", success))
                : UiLanguage.L(string.Format("还原完成：{0} 项成功，{1} 项失败（多为未授权，可逐项重试）。", success, failed),
                    string.Format("Done: {0} ok, {1} failed (mostly due to permission - retry individually).", success, failed));
            MessageBox.Show(msg, UiLanguage.L("提示", "Info"), MessageBoxButton.OK,
                failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
                Style = (Style)FindResource("CodePanel"),
                MinHeight = 70, MaxHeight = 130,
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
                // v5.2.0：覆盖 hosts 属于系统级文件覆盖，先 2 次确认
                if (!RiskGuard.ConfirmTwice(
                        UiLanguage.L("从备份恢复 hosts", "Restore hosts from backup"),
                        $"copy /y \"{backupPath}\" \"%WINDIR%\\System32\\drivers\\etc\\hosts\""))
                    return;
                await RunAdminLiteral(
                    $"copy /y \"{backupPath}\" \"%WINDIR%\\System32\\drivers\\etc\\hosts\"",
                    UiLanguage.L("Hosts 恢复", "Hosts restore"));
            };
            row.Children.Add(restore);
            sp.Children.Add(row);

            // 重启资源管理器（v5.0.0 修复 Bug 8：提权 kill + 非提权重启，避免 Explorer 以管理员运行）
            var restart = new Button
            {
                Content = UiLanguage.L("重启资源管理器", "Restart Explorer"),
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ThemeManager.ApplyButtonTheme(restart, Color.FromRgb(0x6C, 0x4B, 0xB4),
                hoverColor: Color.FromRgb(0x55, 0x39, 0x92));
            restart.Click += async (_, __) =>
            {
                // v5.2.0：结束并重启资源管理器会让桌面与任务栏短暂消失（约 1-2 秒），
                // 未保存的窗口操作可能丢失，执行前先确认。
                var ok = MessageBox.Show(
                    UiLanguage.L(
                        "将结束并重启 Windows 资源管理器（explorer.exe），桌面与任务栏会短暂消失约 1~2 秒后恢复。确定继续吗？",
                        "Explorer will be restarted; the desktop and taskbar will briefly disappear for 1-2 seconds. Continue?"),
                    UiLanguage.L("重启资源管理器", "Restart Explorer"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (ok != MessageBoxResult.Yes) return;

                // 1) 结束 explorer（同会话进程，无需提权）
                try
                {
                    CommandRunner.RegisterAllowed(new[] { "taskkill /f /im explorer.exe" });
                    await CommandRunner.RunAsync("taskkill /f /im explorer.exe", requireAdmin: false, timeoutSec: 30);
                }
                catch { /* 结束失败也继续尝试重启 */ }
                // 2) 延迟后以当前用户会话非提权启动新 Explorer
                await Task.Delay(1200);
                try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); }
                catch { }
            };
            sp.Children.Add(restart);
            return card;
        }

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
    }
}
