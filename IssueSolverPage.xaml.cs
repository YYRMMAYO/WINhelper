using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// 「问题解决」页面（导航 key="issue"）：常见故障知识库 + 白名单命令一键修复，实时回显。
    /// 所有命令均经 CommandRunner 白名单精确校验，无任何参数拼接，无命令注入面。
    /// </summary>
    public partial class IssueSolverPage : UserControl
    {
        // 当前激活分类，"all" 表示全部
        private string _activeCat = "all";
        private IssueEntry? _current;
        private CancellationTokenSource? _runCts;
        private readonly System.Diagnostics.Stopwatch _sw = new();
        private readonly System.Windows.Threading.DispatcherTimer _timer;

        static IssueSolverPage()
        {
            // 注入白名单，避免在静态字段初始化环里触发
            IssueCatalog.EnsureRegistered();
        }

        public IssueSolverPage()
        {
            InitializeComponent();

            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, __) => UpdateTimer();

            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);

            Localize();
            UiLanguage.Changed += () => Dispatcher.Invoke(Localize);

            BtnCancel.Click += BtnCancel_Click;
            BtnCopy.Click += BtnCopy_Click;
            BtnClearConsole.Click += BtnClearConsole_Click;
            TxtSearch.TextChanged += TxtSearch_TextChanged;
            Unloaded += Page_Unloaded;
        }

        // ===== 主题 / 语言 =====

        private void ApplyTheme()
        {
            // 玻璃画刷由 DynamicResource 自动刷新；此处仅重画彩色按钮与选中分类高亮
            ThemeManager.ApplyButtonTheme(BtnCancel, Color.FromRgb(0xE7, 0x4C, 0x3C), hoverColor: Color.FromRgb(0xC0, 0x39, 0x2B));
            ThemeManager.ApplyButtonTheme(BtnCopy, Color.FromRgb(0x4A, 0x90, 0xD9), hoverColor: Color.FromRgb(0x3A, 0x7B, 0xC8));
            ThemeManager.ApplyButtonTheme(BtnClearConsole, Color.FromRgb(0x95, 0xA5, 0xA6), hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
            BuildChips(); // 选中分类用主题强调色高亮，随主题刷新
        }

        private void Localize()
        {
            // 管理员状态徽标
            if (CommandRunner.IsElevated)
            {
                AdminBadgeText.Text = UiLanguage.L("已以管理员身份运行", "Running as administrator");
                AdminBadge.Background = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
            }
            else
            {
                AdminBadgeText.Text = UiLanguage.L("未提权 · 修复时将请求 UAC", "Not elevated · UAC will prompt");
                AdminBadge.Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xC4, 0x0F));
            }
            AdminBadgeText.Foreground = new SolidColorBrush(Colors.White);

            BuildChips();
            ApplyFilter();
            if (_current != null) ShowDetail(_current);
        }

        // ===== 分类芯片 =====

        private void BuildChips()
        {
            ChipsPanel.Children.Clear();
            ChipsPanel.Children.Add(MakeChip("all", "全部", "All"));
            foreach (var c in IssueCatalog.Categories)
                ChipsPanel.Children.Add(MakeChip(c.Key, c.TitleZh, c.TitleEn));
        }

        private Border MakeChip(string key, string zh, string en)
        {
            bool selected = key == _activeCat;
            var b = new Border
            {
                Style = (Style)FindResource("GlassPillMini"),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Tag = key
            };
            if (selected)
                b.Background = new SolidColorBrush(ThemeManager.AccentColor);

            b.Child = new TextBlock
            {
                Text = UiLanguage.L(zh, en),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = selected ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
            };
            b.MouseLeftButtonUp += (s, e) =>
            {
                _activeCat = key;
                BuildChips();
                ApplyFilter();
            };
            return b;
        }

        // ===== 过滤 / 列表 =====

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (ListPanel == null) return;
            ListPanel.Children.Clear();

            string q = (TxtSearch.Text ?? "").Trim().ToLowerInvariant();
            string[] words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            bool any = false;
            foreach (var c in IssueCatalog.Categories)
            {
                if (_activeCat != "all" && c.Key != _activeCat) continue;
                var items = c.Items.Where(it => Matches(it, words)).ToList();
                if (items.Count == 0) continue;
                any = true;

                ListPanel.Children.Add(new TextBlock
                {
                    Text = c.Icon + "  " + c.Title,
                    Style = (Style)FindResource("GroupHeader")
                });
                foreach (var it in items)
                    ListPanel.Children.Add(BuildIssueCard(it));
            }

            if (!any)
            {
                ListPanel.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("没有匹配的问题，换个关键词试试。", "No matching issue. Try another keyword."),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 16, 6, 0)
                });
            }
        }

        private static bool Matches(IssueEntry it, string[] words)
        {
            if (words.Length == 0) return true;
            foreach (var w in words)
                if (!it.Haystack.Contains(w)) return false;
            return true;
        }

        private Border BuildIssueCard(IssueEntry e)
        {
            var sp = new StackPanel
            {
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(new TextBlock
            {
                Text = e.Icon + "  " + e.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
            });
            sp.Children.Add(new TextBlock
            {
                Text = e.Symptom,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

            var b = new Border
            {
                Style = (Style)FindResource("GlassInnerCard"),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Tag = e
            };
            b.Child = sp;
            b.MouseLeftButtonUp += (s, ev) => ShowDetail(e);
            return b;
        }

        // ===== 详情 =====

        private void ShowDetail(IssueEntry e)
        {
            _current = e;
            if (DetailPanel == null) return;
            DetailPanel.Children.Clear();

            DetailPanel.Children.Add(new TextBlock
            {
                Text = e.Icon + "  " + e.Title,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            DetailPanel.Children.Add(Para(e.Symptom));

            DetailPanel.Children.Add(SectionHead(UiLanguage.L("常见成因", "Common causes")));
            DetailPanel.Children.Add(Para(e.Cause));

            DetailPanel.Children.Add(SectionHead(UiLanguage.L("排查步骤", "Troubleshooting steps")));
            int idx = 1;
            foreach (var step in e.Steps)
            {
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = string.Format("{0}. {1}", idx++, step),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }

            if (e.Fixes.Count > 0)
            {
                DetailPanel.Children.Add(SectionHead(UiLanguage.L("一键修复", "One-click fix")));
                foreach (var f in e.Fixes)
                    DetailPanel.Children.Add(BuildFixRow(f));
            }
            else
            {
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = UiLanguage.L("（此条目为知识科普，暂无一键修复命令。）", "(This is informational; no one-click fix is provided.)"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
        }

        private Border BuildFixRow(FixAction f)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            if (f.NeedAdmin) head.Children.Add(Badge(UiLanguage.L("需管理员", "Admin"), Color.FromRgb(0xE6, 0x7E, 0x22)));
            if (f.NeedReboot) head.Children.Add(Badge(UiLanguage.L("需重启", "Reboot"), Color.FromRgb(0x29, 0x80, 0xB9)));
            if (f.Risk == RiskLevel.Danger) head.Children.Add(Badge(UiLanguage.L("危险", "Danger"), Color.FromRgb(0xE7, 0x4C, 0x3C)));
            else if (f.Risk == RiskLevel.Caution) head.Children.Add(Badge(UiLanguage.L("注意", "Caution"), Color.FromRgb(0xF1, 0xC4, 0x0F)));
            sp.Children.Add(head);

            var cmdBox = new Border
            {
                Style = (Style)FindResource("GlassInnerCard"),
                Margin = new Thickness(0, 0, 0, 4),
                Child = new TextBlock
                {
                    Text = f.Command,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            sp.Children.Add(cmdBox);

            var btn = new Button
            {
                Content = f.Label,
                Style = (Style)FindResource("GlassToolbarButton"),
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = f
            };
            btn.Click += (s, ev) => _ = RunFixAsync(f);
            sp.Children.Add(btn);

            return new Border { Child = sp, Margin = new Thickness(0, 0, 0, 4) };
        }

        private Border Badge(string text, Color color)
        {
            return new Border
            {
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                }
            };
        }

        private TextBlock SectionHead(string text)
        {
            return new TextBlock
            {
                Text = text,
                Style = (Style)FindResource("SectionHeader")
            };
        }

        private TextBlock Para(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        // ===== 确认 / 执行 =====

        private bool ConfirmFix(FixAction f)
        {
            var sb = new StringBuilder();
            sb.Append(UiLanguage.L("即将执行以下命令：\n", "About to run this command:\n"));
            sb.Append(f.Command).Append("\n\n");
            if (f.Warn != null) sb.Append(f.Warn).Append("\n\n");

            if (f.Risk == RiskLevel.Danger)
            {
                sb.Append(UiLanguage.L("此操作风险较高，确定继续？", "This is high-risk. Continue anyway?"));
                return MessageBox.Show(sb.ToString(), UiLanguage.L("危险操作确认", "Confirm risky action"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            }
            if (f.Risk == RiskLevel.Caution || f.Warn != null)
            {
                sb.Append(UiLanguage.L("确定要执行吗？", "Proceed?"));
                return MessageBox.Show(sb.ToString(), UiLanguage.L("操作确认", "Confirm action"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK) == MessageBoxResult.OK;
            }
            return true;
        }

        private async Task RunFixAsync(FixAction f)
        {
            if (_runCts != null) return;        // 已有修复在运行
            if (!ConfirmFix(f)) return;

            var cts = new CancellationTokenSource();
            _runCts = cts;
            SetRunning(true);
            ConsoleCard.Visibility = Visibility.Visible;
            AppendConsole("> " + f.Command);

            _sw.Restart();
            _timer.Start();

            var prog = new Progress<string>(AppendConsole);
            try
            {
                CommandResult r = await CommandRunner.RunAsync(f.Command, f.NeedAdmin, prog, f.TimeoutSec, cts.Token);
                AppendConsole(string.Empty);
                AppendConsole(Summarize(r, f));
                if (f.NeedReboot && r.ExitCode == 0)
                    AppendConsole(UiLanguage.L("提示：需重启电脑后生效。", "Note: reboot required to take effect."));
            }
            catch (Exception ex)
            {
                AppendConsole("[ERR] " + ex.Message);
            }
            finally
            {
                _timer.Stop();
                _sw.Stop();
                TxtTimer.Text = _sw.Elapsed.ToString(@"mm\:ss");
                if (_runCts == cts) _runCts = null;
                cts.Dispose();
                SetRunning(false);
            }
        }

        private void AppendConsole(string line)
        {
            if (TxtConsole == null) return;
            if (TxtConsole.LineCount > 4000) TxtConsole.Clear();
            TxtConsole.AppendText(line + "\n");
            TxtConsole.ScrollToEnd();
        }

        private static string Summarize(CommandResult r, FixAction f)
        {
            string s = r.ExitCode switch
            {
                >= 0 => UiLanguage.L(string.Format("命令已结束，退出码 {0}。", r.ExitCode), string.Format("Command finished, exit code {0}.", r.ExitCode)),
                -1 => UiLanguage.L("命令启动失败。", "Failed to start command."),
                -2 => UiLanguage.L("命令执行超时。", "Command timed out."),
                -3 => UiLanguage.L("已取消（未获管理员授权或用户取消）。", "Cancelled (UAC denied or user cancelled)."),
                -4 => UiLanguage.L("命令被安全白名单拒绝。", "Command rejected by the safety allowlist."),
                _ => UiLanguage.L("命令异常结束。", "Command ended abnormally.")
            };
            if (r.Elevated) s += " " + UiLanguage.L("（已提权执行）", "(elevated)");
            return s;
        }

        // ===== 状态栏 =====

        private void SetRunning(bool running)
        {
            BtnCancel.IsEnabled = running;
            TxtStatus.Text = running
                ? UiLanguage.L("正在执行修复…", "Running fix…")
                : UiLanguage.L("就绪", "Ready");
            if (running) TxtTimer.Text = "00:00";
        }

        private void UpdateTimer()
        {
            if (_sw.IsRunning)
                TxtTimer.Text = _sw.Elapsed.ToString(@"mm\:ss");
        }

        // ===== 按钮 =====

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => _runCts?.Cancel();

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtConsole.Text))
            {
                try { Clipboard.SetText(TxtConsole.Text); } catch { /* 忽略剪贴板异常 */ }
            }
        }

        private void BtnClearConsole_Click(object sender, RoutedEventArgs e)
        {
            TxtConsole.Clear();
            ConsoleCard.Visibility = Visibility.Collapsed;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // 页面被 MainWindow 缓存，仅 Unloaded 不销毁；离开时复位，避免回来按钮卡死
            _runCts?.Cancel();
            _timer.Stop();
            _runCts = null;
            SetRunning(false);
        }
    }
}
