using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WINHELP
{
    /// <summary>
    /// 小窗设置 — 用户自定义小窗图片 + 链接北京时间开关
    /// </summary>
    public partial class CompanionSettingsWindow : Window
    {
        public CompanionSettingsWindow()
        {
            InitializeComponent();
            ThemeManager.SetWindowIcon(this);
            ApplyAccent();
            ApplyBackground();
            ThemeManager.ThemeChanged += OnThemeChanged;
            LoadFromSettings();
        }

        private void ApplyAccent()
        {
            BtnPickImage.Background = new SolidColorBrush(ThemeManager.AccentColor);
            BtnDone.Background = new SolidColorBrush(ThemeManager.AccentColor);
        }

        /// <summary>应用全局主题背景（个性装扮中的壁纸）到小窗设置对话框</summary>
        private void ApplyBackground()
        {
            ThemeManager.ApplyWindowBackground(this);
        }

        private void OnThemeChanged() => Dispatcher.Invoke(() => { ApplyAccent(); ApplyBackground(); });

        private void LoadFromSettings()
        {
            var s = CompanionSettingsManager.Current;
            ChkSyncTime.IsChecked = s.SyncBeijingTime;
            ChkSeconds.IsChecked = s.ShowSeconds;
            ChkOnTop.IsChecked = s.AlwaysOnTop;
            UpdateImagePreview();
        }

        private void UpdateImagePreview()
        {
            var path = CompanionSettingsManager.Current.ImagePath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    ImgPreview.Source = bmp;
                    ImgPreviewTip.Visibility = Visibility.Collapsed;
                    TxtImagePath.Text = path;
                    return;
                }
                catch { /* 损坏回落 */ }
            }
            ImgPreview.Source = null;
            ImgPreviewTip.Visibility = Visibility.Visible;
            TxtImagePath.Text = string.IsNullOrWhiteSpace(path) ? "（未设置）" : path;
        }

        /// <summary>选择本地图片</summary>
        private void BtnPickImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择小窗图片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|所有文件|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            CompanionSettingsManager.Current.ImagePath = dlg.FileName;
            CompanionSettingsManager.Save();
            UpdateImagePreview();
        }

        /// <summary>清除图片</summary>
        private void BtnClearImage_Click(object sender, RoutedEventArgs e)
        {
            CompanionSettingsManager.Current.ImagePath = "";
            CompanionSettingsManager.Save();
            UpdateImagePreview();
        }

        /// <summary>开关变更立即保存</summary>
        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            var s = CompanionSettingsManager.Current;
            s.SyncBeijingTime = ChkSyncTime.IsChecked == true;
            s.ShowSeconds = ChkSeconds.IsChecked == true;
            s.AlwaysOnTop = ChkOnTop.IsChecked == true;
            CompanionSettingsManager.Save();
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            base.OnClosed(e);
        }
    }
}
