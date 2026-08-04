using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// SettingsPage.xaml — 软件设置（导航 key="settings"，内嵌为右侧页面）
    /// 由 MainWindow._factories 懒加载；依赖 SettingsManager 与 ThemeManager 玻璃画刷。
    /// </summary>
    public partial class SettingsPage : UserControl
    {
        private bool _isLoading = true; // 防止加载时触发 Changed 事件

        /// <summary>请求返回首页（由 MainWindow 注入）</summary>
        public Action? OnCloseRequest;

        public SettingsPage()
        {
            InitializeComponent();

            // 加载当前设置到 UI
            ToggleAutoStart.IsChecked = SettingsManager.Current.AutoStart;
            ToggleAutoCheckUpdate.IsChecked = SettingsManager.Current.AutoCheckUpdate;
            ToggleCloseToTray.IsChecked = SettingsManager.Current.CloseToTray;

            // 加载定时计划设置
            ToggleSchedulerEnabled.IsChecked = SettingsManager.Current.SchedulerEnabled;
            ComboDay.SelectedIndex = SettingsManager.Current.SchedulerDayOfWeek == -1
                ? 0
                : SettingsManager.Current.SchedulerDayOfWeek + 1;
            TxtTime.Text = SettingsManager.Current.SchedulerTime;

            // 显示版本号和构建时间
            TxtVersion.Text = UpdateManager.LocalVersion;
            TxtFullVersion.Text = UiLanguage.L($"完整版本号：{UpdateManager.FullVersion}", $"Full version: {UpdateManager.FullVersion}");

            // 版本历史（升级后保留原版本信息）
            RenderVersionHistory();

            // 更新状态栏默认可见
            UpdateStatusBar.Visibility = Visibility.Visible;

            // 应用主题色到按钮
            BtnDone.Background = new SolidColorBrush(ThemeManager.AccentColor);

            // 全局背景应用到本页
            ApplyBackground();
            ThemeManager.ThemeChanged += OnThemeChanged;

            // 多语言文本
            ApplyLocalization();
            UiLanguage.Changed += OnLanguageChanged;

            // 语言选择器初始状态（在 _isLoading 期间设置，避免触发 Changed）
            CmbLanguage.SelectedIndex = UiLanguage.Current == Lang.En ? 1 : 0;

            _isLoading = false;
        }

        private void OnLanguageChanged()
        {
            Dispatcher.Invoke(() => { ApplyLocalization(); RenderVersionHistory(); });
        }

        /// <summary>语言选择器切换：持久化并触发全局 Changed</summary>
        private void CmbLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var lang = CmbLanguage.SelectedIndex == 1 ? Lang.En : Lang.Zh;
            UiLanguage.Set(lang);
        }

        /// <summary>刷新定时计划相关文案（中英切换）</summary>
        private void ApplyLocalization()
        {
            TxtSchedulerTitle.Text = UiLanguage.L("⏰ 定时计划", "⏰ Scheduled Plan");
            TxtEnableScheduler.Text = UiLanguage.L("启用定时自动优化", "Enable scheduled auto-optimization");
            TxtSchedulerHint.Text = UiLanguage.L(
                "启用后，软件在运行时会按计划自动执行一键优化（清理临时文件 + 清空回收站）。",
                "When enabled, the app automatically runs one-click optimization (clean temp files + empty recycle bin) on schedule while running.");
            TxtDayLabel.Text = UiLanguage.L("执行频率", "Frequency");
            TxtTimeLabel.Text = UiLanguage.L("执行时间", "Time (HH:mm)");
            CbDaily.Content = UiLanguage.L("每天", "Daily");
            CbMon.Content = UiLanguage.L("周一", "Mon");
            CbTue.Content = UiLanguage.L("周二", "Tue");
            CbWed.Content = UiLanguage.L("周三", "Wed");
            CbThu.Content = UiLanguage.L("周四", "Thu");
            CbFri.Content = UiLanguage.L("周五", "Fri");
            CbSat.Content = UiLanguage.L("周六", "Sat");
            CbSun.Content = UiLanguage.L("周日", "Sun");

            // 界面语言选择器
            TxtLangLabel.Text = UiLanguage.L("界面语言", "Interface Language");
            TxtLangHint.Text = UiLanguage.L("切换后整个软件将即时中英切换",
                "Switching applies Chinese/English across the app instantly");
            CmbLangZh.Content = UiLanguage.L("中文", "中文");
            CmbLangEn.Content = UiLanguage.L("English", "English");

            TxtHistoryTitle.Text = UiLanguage.L("版本历史", "Version History");
        }

        /// <summary>渲染版本历史列表（最新在上），并标注原版本信息</summary>
        private void RenderVersionHistory()
        {
            VersionHistoryPanel.Children.Clear();
            var hist = UpgradeTracker.History;
            if (hist.Count == 0) return;

            for (int i = hist.Count - 1; i >= 0; i--)
            {
                var rec = hist[i];
                bool isCurrent = i == hist.Count - 1;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var ver = new TextBlock
                {
                    Text = "v" + rec.Version + (isCurrent ? UiLanguage.L("（当前）", " (current)") : ""),
                    FontSize = 12.5,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(isCurrent
                        ? Color.FromRgb(0x2E, 0x86, 0xC1)
                        : Color.FromRgb(0x2C, 0x3E, 0x50)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(ver, 0);

                var date = new TextBlock
                {
                    Text = rec.InstalledAt == default ? "—" : rec.InstalledAt.ToString("yyyy-MM-dd HH:mm"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(date, 1);

                row.Children.Add(ver);
                row.Children.Add(date);
                VersionHistoryPanel.Children.Add(row);
            }

            if (!string.IsNullOrEmpty(UpgradeTracker.PreviousVersion))
            {
                var note = new TextBlock
                {
                    Text = UiLanguage.L($"本次由 v{UpgradeTracker.PreviousVersion} 升级而来，原版本信息已保留。",
                        $"Upgraded from v{UpgradeTracker.PreviousVersion}; previous version info is preserved."),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                VersionHistoryPanel.Children.Add(note);
            }
        }

        private void OnThemeChanged()
        {
            Dispatcher.Invoke(() =>
            {
                BtnDone.Background = new SolidColorBrush(ThemeManager.AccentColor);
                ApplyBackground();
            });
        }

        /// <summary>把全局背景（纯色或壁纸）应用到本页</summary>
        private void ApplyBackground()
        {
            RootBorder.Background = Brushes.Transparent;
        }

        /// <summary>开关状态变更时立即保存</summary>
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            SettingsManager.Current.AutoStart = ToggleAutoStart.IsChecked == true;
            SettingsManager.Current.AutoCheckUpdate = ToggleAutoCheckUpdate.IsChecked == true;
            SettingsManager.Current.CloseToTray = ToggleCloseToTray.IsChecked == true;

            // 立即应用开机启动
            SettingsManager.SetAutoStart(SettingsManager.Current.AutoStart);

            // 持久化保存
            SettingsManager.Save();
        }

        /// <summary>定时计划设置变更时立即保存</summary>
        private void Scheduler_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            SettingsManager.Current.SchedulerEnabled = ToggleSchedulerEnabled.IsChecked == true;

            // ComboBox 第 0 项 = 每天(-1)，其余 1..7 对应 周一..周日(0..6)
            SettingsManager.Current.SchedulerDayOfWeek = ComboDay.SelectedIndex <= 0
                ? -1
                : ComboDay.SelectedIndex - 1;

            // 仅在时间格式合法时保存，避免写入无效值
            string? t = TxtTime.Text?.Trim();
            if (TimeSpan.TryParse(t, out _))
            {
                SettingsManager.Current.SchedulerTime = t!;
            }

            // 持久化保存（SchedulerManager 会在下一分钟检查读取到最新值）
            SettingsManager.Save();
        }

        /// <summary>手动检查更新</summary>
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.Content = "⏳ 检查中...";
            BtnCheckUpdate.IsEnabled = false;

            bool hasUpdate = false;
            UpdateManager.UpdateInfo? foundInfo = null;

            UpdateManager.UpdateAvailable += OnUpdateFound;

            await UpdateManager.CheckAsync();

            UpdateManager.UpdateAvailable -= OnUpdateFound;
            BtnCheckUpdate.Content = "检查更新";
            BtnCheckUpdate.IsEnabled = true;

            UpdateStatusBar.Visibility = Visibility.Visible;

            if (hasUpdate && foundInfo != null)
            {
                UpdateStatusBar.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0));
                TxtUpdateStatus.Text = $"发现新版本 v{foundInfo.RemoteVersion}！点击下载页面获取最新版本。";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00));
            }
            else
            {
                UpdateStatusBar.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
                TxtUpdateStatus.Text = $"当前已是最新版本（{UpdateManager.FullVersion}）✓";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            }

            void OnUpdateFound(UpdateManager.UpdateInfo info)
            {
                hasUpdate = true;
                foundInfo = info;
            }
        }

        /// <summary>GitHub 链接点击</summary>
        private void GitHubLink_Click(object sender, MouseButtonEventArgs e)
        {
            UpdateManager.OpenDownloadUrl(UpdateManager.PrimaryDownloadUrl);
        }

        /// <summary>下载链接点击（蓝奏云备选源）</summary>
        private void DownloadLink_Click(object sender, MouseButtonEventArgs e)
        {
            UpdateManager.OpenDownloadUrl(UpdateManager.BackupDownloadUrl);
        }

        /// <summary>关闭按钮</summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            OnCloseRequest?.Invoke();
        }

        /// <summary>导出设置到用户选择的文件（settings.json 副本）</summary>
        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = UiLanguage.L("导出设置", "Export Settings"),
                Filter = "JSON 设置文件 (*.json)|*.json|全部文件|*.*",
                FileName = "司南工具箱-设置-" + DateTime.Now.ToString("yyyyMMdd"),
                DefaultExt = ".json",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                SettingsManager.ExportSettings(dlg.FileName);
                MessageBox.Show(
                    UiLanguage.L("设置已导出到：\n", "Settings exported to:\n") + dlg.FileName,
                    UiLanguage.L("导出成功", "Exported"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLanguage.L("导出失败：", "Export failed: ") + ex.Message,
                    UiLanguage.L("导出设置", "Export Settings"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>从文件导入设置并刷新当前页面</summary>
        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = UiLanguage.L("导入设置", "Import Settings"),
                Filter = "JSON 设置文件 (*.json)|*.json|全部文件|*.*",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                if (!SettingsManager.ImportSettings(dlg.FileName))
                {
                    MessageBox.Show(
                        UiLanguage.L("导入失败：文件格式无效。", "Import failed: invalid file format."),
                        UiLanguage.L("导入设置", "Import Settings"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // 重新加载 UI 以反映导入的设置
                _isLoading = true;
                ToggleAutoStart.IsChecked = SettingsManager.Current.AutoStart;
                ToggleAutoCheckUpdate.IsChecked = SettingsManager.Current.AutoCheckUpdate;
                ToggleCloseToTray.IsChecked = SettingsManager.Current.CloseToTray;
                ToggleSchedulerEnabled.IsChecked = SettingsManager.Current.SchedulerEnabled;
                ComboDay.SelectedIndex = SettingsManager.Current.SchedulerDayOfWeek == -1
                    ? 0 : SettingsManager.Current.SchedulerDayOfWeek + 1;
                TxtTime.Text = SettingsManager.Current.SchedulerTime;
                _isLoading = false;
                SettingsManager.SetAutoStart(SettingsManager.Current.AutoStart);
                MessageBox.Show(
                    UiLanguage.L("设置已导入并应用。", "Settings imported and applied."),
                    UiLanguage.L("导入成功", "Imported"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLanguage.L("导入失败：", "Import failed: ") + ex.Message,
                    UiLanguage.L("导入设置", "Import Settings"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>完成按钮</summary>
        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            OnCloseRequest?.Invoke();
        }
    }
}
