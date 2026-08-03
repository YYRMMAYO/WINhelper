using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WINHELP
{
    /// <summary>
    /// 陪伴运行小窗（独立窗体）— 图形框 + 北京时间 + 返回正常程序 + 左下角设置。
    /// 由 CompanionPage / 托盘菜单启动；依赖 CompanionManager 与 ThemeManager 玻璃画刷。
    /// </summary>
    public partial class CompanionWindow : Window
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private TimeSpan _serverOffset = TimeSpan.Zero; // 北京时间 - 本地时间
        private bool _synced = false;
        private DateTime _lastSync = DateTime.MinValue;
        private static readonly CultureInfo ZhCn = new("zh-CN");

        // 玻璃模糊背景缓存：避免每次调节滑块都重新解码壁纸 + 整窗高斯模糊重渲
        private BitmapImage? _backdropBitmap = null;
        private string? _lastBackdropPath = null;
        private GlassMode _lastGlassMode = GlassMode.Translucent;

        // ===== 便签 / 待办（与 NotesPage 共享 NotesStore） =====
        private readonly ObservableCollection<NotesStore.NoteEntry> _notes = new();

        public CompanionWindow()
        {
            InitializeComponent();
            ThemeManager.SetWindowIcon(this);

            ApplyAccent();
            ApplyBackground();
            ThemeManager.ThemeChanged += OnThemeChanged;
            ThemeManager.GlassChanged += OnGlassChanged;

            // 便签
            ListNotes.ItemsSource = _notes;
            LoadNotes();
            NotesStore.Changed += OnNotesChanged;

            // 应用陪伴设置
            Topmost = CompanionSettingsManager.Current.AlwaysOnTop;
            LoadImage();

            // 启动时钟
            _timer.Tick += OnTick;
            _timer.Start();

            // 在小窗内按 F11 也可返回正常程序
            this.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.F11)
                {
                    CompanionManager.Exit();
                    e.Handled = true;
                }
            };

            Loaded += async (_, _) => { await SyncTimeAsync(); UpdateClock(); };
        }

        private void ApplyAccent()
        {
            // 返回按钮使用主题强调色
            ThemeManager.ApplyButtonTheme(BtnReturn, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnAddNote, Color.FromRgb(0x27, 0xAE, 0x60));
            ThemeManager.ApplyButtonTheme(BtnHideNotes, Color.FromRgb(0x95, 0xA5, 0xA6));
        }

        /// <summary>应用全局主题背景（个性装扮中的壁纸）到陪伴小窗</summary>
        private void ApplyBackground()
        {
            ThemeManager.ApplyWindowBackground(this);
            ApplyGlassBackdrop();
        }

        private void OnThemeChanged()
        {
            Dispatcher.Invoke(() => { ApplyAccent(); ApplyBackground(); });
        }

        private void OnGlassChanged()
        {
            Dispatcher.Invoke(ApplyGlassBackdrop);
        }

        /// <summary>Acrylic 模式下显示模糊壁纸层（与 MainWindow 同款效果）。
        /// 关键：背景图绘制在 BackdropHost(Border) 的 Background(ImageBrush) 上，
        /// 该 Border 的 DesiredSize 为 0，不会参与 SizeToContent 测量，因此无论壁纸多大都不会把小窗撑成"长条"。</summary>
        private void ApplyGlassBackdrop()
        {
            if (BackdropHost == null) return;

            bool acrylic = ThemeManager.GlassEffect == GlassMode.Acrylic && ThemeManager.HasBackgroundImage;
            if (acrylic)
            {
                // 仅当图片路径或玻璃模式变化时才重新解码；常规调节只更新 Opacity
                if (_backdropBitmap == null ||
                    _lastBackdropPath != ThemeManager.BackgroundImagePath ||
                    _lastGlassMode != ThemeManager.GlassEffect)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(ThemeManager.BackgroundImagePath);
                        bmp.EndInit();
                        bmp.Freeze();
                        _backdropBitmap = bmp;
                        _lastBackdropPath = ThemeManager.BackgroundImagePath;
                        _lastGlassMode = ThemeManager.GlassEffect;
                        // 用 ImageBrush 作为 Border 背景（不撑布局），再叠 BlurEffect 模糊
                        BackdropHost.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                        BackdropHost.InvalidateVisual(); // 仅换图时失效重渲
                    }
                    catch
                    {
                        BackdropHost.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                BackdropHost.Opacity = ThemeManager.BackgroundOpacity;
                BackdropHost.Visibility = Visibility.Visible;
            }
            else
            {
                BackdropHost.Visibility = Visibility.Collapsed;
                _lastGlassMode = ThemeManager.GlassEffect;
            }
        }

        /// <summary>加载用户自定义图片</summary>
        private void LoadImage()
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
                    Img.Source = bmp;
                    ImgPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }
                catch (Exception ex) { App.LogCrash(ex, "CompanionImage"); /* 图片损坏，回落到占位 */ }
            }
            Img.Source = null;
            ImgPlaceholder.Visibility = Visibility.Visible;
        }

        /// <summary>通过 HTTPS 时间服务器响应头同步北京时间。
        /// 安全说明：原实现使用明文 http://time.syiban.com/，存在中间人篡改风险（安全审计建议 P1），
        /// 已改用 HTTPS 端点；若 HTTPS 失败则回退到本地时间。</summary>
        private async Task SyncTimeAsync()
        {
            if (!CompanionSettingsManager.Current.SyncBeijingTime)
            {
                _synced = false;
                return;
            }
            try
            {
                // 只读响应头，避免下载整个 HTML；使用 HTTPS 避免中间人篡改时间
                using var resp = await _http.GetAsync(
                    "https://time.syiban.com/", HttpCompletionOption.ResponseHeadersRead);
                if (resp.Headers.Date is DateTimeOffset d)
                {
                    var beijing = d.UtcDateTime.AddHours(8); // UTC + 8 = 北京时间
                    _serverOffset = beijing - DateTime.Now;
                    _synced = true;
                    _lastSync = DateTime.Now;
                }
            }
            catch
            {
                _synced = false;
            }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            // 每 5 分钟重新同步一次，纠正本地时钟漂移
            if (CompanionSettingsManager.Current.SyncBeijingTime
                && (DateTime.Now - _lastSync) > TimeSpan.FromMinutes(5))
            {
                _ = SyncTimeAsync();
            }
            UpdateClock();
        }

        /// <summary>刷新时钟显示</summary>
        private void UpdateClock()
        {
            DateTime now;
            string status;
            if (CompanionSettingsManager.Current.SyncBeijingTime && _synced)
            {
                now = DateTime.Now + _serverOffset;
                status = "北京时间已同步";
            }
            else if (CompanionSettingsManager.Current.SyncBeijingTime)
            {
                now = DateTime.Now;
                status = "同步失败·显示本地时间";
            }
            else
            {
                now = DateTime.Now;
                status = "本地时间";
            }

            TxtTime.Text = CompanionSettingsManager.Current.ShowSeconds
                ? now.ToString("HH:mm:ss")
                : now.ToString("HH:mm");

            TxtDate.Text = now.ToString("yyyy年MM月dd日 ddd", ZhCn);
            TxtStatus.Text = status;
        }

        // ===== 按钮事件 =====

        /// <summary>返回正常程序</summary>
        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            CompanionManager.Exit();
        }

        /// <summary>左下角设置：进入小窗设置</summary>
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new CompanionSettingsWindow { Owner = this };
            win.ShowDialog();

            // 设置可能已更改，重新应用
            Topmost = CompanionSettingsManager.Current.AlwaysOnTop;
            LoadImage();
            _ = SyncTimeAsync();
            UpdateClock();
        }

        // ===== 便签 / 待办 =====

        private void OnNotesChanged()
        {
            // 用 BeginInvoke 避免在与 NotesStore.Changed 同一调用栈中重入导致的潜在问题
            Dispatcher.BeginInvoke((Action)LoadNotes);
        }

        private void LoadNotes()
        {
            try
            {
                _notes.Clear();
                foreach (var n in NotesStore.LoadAll())
                    _notes.Add(n);
            }
            catch { /* 读取失败忽略 */ }
        }

        private void BtnToggleNotes_Click(object sender, RoutedEventArgs e)
        {
            NotesPanel.Visibility = NotesPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnHideNotes_Click(object sender, RoutedEventArgs e)
        {
            NotesPanel.Visibility = Visibility.Collapsed;
        }

        // 便签保存反馈计时器（短暂展示"已保存"后自动隐藏）
        private readonly DispatcherTimer _noteStatusTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

        private void BtnAddNote_Click(object sender, RoutedEventArgs e)
        {
            var text = TxtNote.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                // 陪伴运行小窗添加的便签：写入共享存储（%APPDATA%/WINHELP/notes/）并在桌面建立副本
                NotesStore.Add(text, toDesktop: true);
                TxtNote.Clear();
                ShowNoteStatus(UiLanguage.L("已保存到本地便签 ✅", "Saved to local notes ✅"), true);
            }
            catch (Exception ex)
            {
                // 任何写入异常都明确告知用户，避免"看似没保存"的静默失败
                ShowNoteStatus(UiLanguage.L($"保存失败：{ex.Message}", $"Save failed: {ex.Message}"), false);
                MessageBox.Show(
                    UiLanguage.L($"便签保存失败：{ex.Message}", $"Note save failed: {ex.Message}"),
                    UiLanguage.L("保存失败", "Save failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowNoteStatus(string msg, bool ok)
        {
            if (TxtNoteStatus == null) return;
            TxtNoteStatus.Text = msg;
            TxtNoteStatus.Foreground = new SolidColorBrush(ok
                ? Color.FromRgb(0x27, 0xAE, 0x60)
                : Color.FromRgb(0xE7, 0x4C, 0x3C));
            TxtNoteStatus.Visibility = Visibility.Visible;
            _noteStatusTimer.Stop();
            _noteStatusTimer.Tick -= NoteStatusTimer_Tick;
            _noteStatusTimer.Tick += NoteStatusTimer_Tick;
            _noteStatusTimer.Start();
        }

        private void NoteStatusTimer_Tick(object? sender, EventArgs e)
        {
            _noteStatusTimer.Stop();
            if (TxtNoteStatus != null) TxtNoteStatus.Visibility = Visibility.Collapsed;
        }

        private void BtnDeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NotesStore.NoteEntry entry)
            {
                NotesStore.Delete(entry);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            ThemeManager.ThemeChanged -= OnThemeChanged;
            ThemeManager.GlassChanged -= OnGlassChanged;
            NotesStore.Changed -= OnNotesChanged;
            base.OnClosed(e);
        }
    }
}
