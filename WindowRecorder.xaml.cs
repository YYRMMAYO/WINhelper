using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WINHELP;

public partial class WindowRecorder : UserControl
{
    private sealed class MediaItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    private readonly ObservableCollection<MediaItem> _audioItems = new();
    private readonly ObservableCollection<MediaItem> _screenItems = new();

    private string _audioFolder = "";
    private string _screenFolder = "";

    // 录音（MCI）
    private bool _audioRecording;
    private DateTime _audioStart;

    // 录像
    private bool _screenRecording;
    private DateTime _screenStart;
    private CancellationTokenSource? _screenCts;
    private Task? _screenTask;

    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly int[] FpsOptions = { 8, 10, 15, 20 };

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

    public WindowRecorder()
    {
        InitializeComponent();

        ListAudio.ItemsSource = _audioItems;
        ListScreen.ItemsSource = _screenItems;

        _audioFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "司南工具箱", "录音");
        _screenFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "司南工具箱", "录像");
        Directory.CreateDirectory(_audioFolder);
        Directory.CreateDirectory(_screenFolder);
        TxtAudioFolder.Text = _audioFolder;
        TxtScreenFolder.Text = _screenFolder;

        FpsSlider.ValueChanged += (_, _) =>
            FpsValue.Text = FpsOptions[(int)FpsSlider.Value] + " fps";

        _uiTimer.Tick += UiTimer_Tick;

        SetMode(true);
        ApplyTheme();
        RefreshStrings();
        LoadExisting();

        ModeTrack.SizeChanged += (_, _) => PositionModeThumb();
        RecDotAudio.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6));
        RecDotScreen.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6));

        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(() => { ApplyTheme(); RefreshStrings(); });
        UiLanguage.Changed += () => Dispatcher.Invoke(RefreshStrings);
    }

    // ===== 模式切换 =====

    private void BtnModeAudio_Click(object sender, RoutedEventArgs e) => SetMode(true);
    private void BtnModeScreen_Click(object sender, RoutedEventArgs e) => SetMode(false);

    private bool _audioMode = true;

    private void SetMode(bool audio)
    {
        _audioMode = audio;
        PanelAudio.Visibility = audio ? Visibility.Visible : Visibility.Collapsed;
        PanelScreen.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        BtnModeAudio.Foreground = new SolidColorBrush(audio ? Colors.White : System.Windows.Media.Color.FromRgb(0x5F, 0x6B, 0x7A));
        BtnModeScreen.Foreground = new SolidColorBrush(audio ? System.Windows.Media.Color.FromRgb(0x5F, 0x6B, 0x7A) : Colors.White);
        PositionModeThumb();
    }

    /// <summary>根据当前模式把滑动指示块对齐到对应半区（带缓动）。</summary>
    private void PositionModeThumb()
    {
        double w = ModeTrack.ActualWidth;
        if (w <= 1) return;
        double half = w / 2.0;
        ModeThumb.Width = half;
        double to = _audioMode ? 0 : half;
        var anim = new DoubleAnimation(to, TimeSpan.FromSeconds(0.3))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        ModeThumbX.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    private void StartRecPulse()
    {
        RecDotAudio.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
        RecDotScreen.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
        if (FindResource("RecPulse") is System.Windows.Media.Animation.Storyboard sb)
            sb.Begin(this);
    }

    private void StopRecPulse()
    {
        if (FindResource("RecPulse") is System.Windows.Media.Animation.Storyboard sb)
            sb.Stop(this);
        RecDotAudio.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6));
        RecDotScreen.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6));
    }

    // ===== 录音（MCI） =====

    private void BtnAudioStart_Click(object sender, RoutedEventArgs e)
    {
        if (_audioRecording) return;
        if (mciSendString("open new type waveaudio alias yayuRec", null, 0, IntPtr.Zero) != 0)
        {
            SetAudioStatus("无法开始录音：未检测到录音设备");
            return;
        }
        if (mciSendString("record yayuRec", null, 0, IntPtr.Zero) != 0)
        {
            mciSendString("close yayuRec", null, 0, IntPtr.Zero);
            SetAudioStatus("无法开始录音");
            return;
        }
        _audioRecording = true;
        _audioStart = DateTime.Now;
        _uiTimer.Start();
        BtnAudioStart.IsEnabled = false;
        BtnAudioStop.IsEnabled = true;
        TxtAudioState.Text = UiLanguage.L("录制中…", "Recording…");
        StartRecPulse();
    }

    private void BtnAudioStop_Click(object sender, RoutedEventArgs e)
    {
        if (!_audioRecording) return;
        _audioRecording = false;
        BtnAudioStart.IsEnabled = true;
        BtnAudioStop.IsEnabled = false;
        TxtAudioState.Text = UiLanguage.L("未开始", "Idle");
        StopRecPulse();

        // 先保存到临时短路径（规避 MCI 长路径/中文路径兼容问题），再移动到目标目录
        string stamp = TimeStamp();
        string tmp = Path.Combine(Path.GetTempPath(), $"yayu_rec_{stamp}.wav");
        string? shortTmp = ToShortPath(tmp);
        if (shortTmp == null) shortTmp = tmp;

        mciSendString("stop yayuRec", null, 0, IntPtr.Zero);
        int rc = mciSendString($"save yayuRec \"{shortTmp}\"", null, 0, IntPtr.Zero);
        mciSendString("close yayuRec", null, 0, IntPtr.Zero);

        string finalPath = Path.Combine(_audioFolder, $"录音_{stamp}.wav");
        try
        {
            if (rc == 0 && File.Exists(shortTmp))
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(shortTmp, finalPath);
                AddItem(_audioItems, finalPath);
                SetAudioStatus(UiLanguage.L($"已保存：{Path.GetFileName(finalPath)}", $"Saved: {Path.GetFileName(finalPath)}"));
            }
            else
            {
                SetAudioStatus(UiLanguage.L("录音保存失败", "Save failed"));
            }
        }
        catch (Exception ex)
        {
            SetAudioStatus(UiLanguage.L("录音保存出错：" + ex.Message, "Save error: " + ex.Message));
        }
        finally
        {
            if (File.Exists(shortTmp)) { try { File.Delete(shortTmp); } catch { } }
            TxtAudioTimer.Text = "00:00";
        }
    }

    // ===== 录像（GDI+ 截屏 + VfW AVI） =====

    private void BtnScreenStart_Click(object sender, RoutedEventArgs e)
    {
        if (_screenRecording) return;

        int fps = FpsOptions[(int)FpsSlider.Value];
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        int sw = bounds.Width, sh = bounds.Height;
        const int maxW = 1280;
        int tw = sw > maxW ? maxW : sw;
        tw = (tw / 4) * 4; if (tw < 4) tw = 4;
        int th = (int)Math.Round(sh * (double)tw / sw);
        th = (th / 2) * 2; if (th < 2) th = 2;

        string file = Path.Combine(_screenFolder, $"录像_{TimeStamp()}.avi");

        _screenRecording = true;
        _screenStart = DateTime.Now;
        _uiTimer.Start();
        BtnScreenStart.IsEnabled = false;
        BtnScreenStop.IsEnabled = true;
        TxtScreenState.Text = UiLanguage.L("录制中…", "Recording…");
        StartRecPulse();

        _screenCts = new CancellationTokenSource();
        var token = _screenCts.Token;
        bool captureCursor = ChkCursor.IsChecked == true;
        int srcX = bounds.X, srcY = bounds.Y;

        _screenTask = Task.Run(() =>
        {
            try
            {
                using var avi = new AviWriter(file, tw, th, fps);
                using var full = new Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using var target = new Bitmap(tw, th, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                var buf = new byte[tw * 3 * th];
                var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                try
                {
                    double interval = 1000.0 / fps;
                    while (!token.IsCancellationRequested)
                    {
                        var swatch = Stopwatch.StartNew();
                        using (var gf = Graphics.FromImage(full))
                            gf.CopyFromScreen(srcX, srcY, 0, 0, new System.Drawing.Size(sw, sh), CopyPixelOperation.SourceCopy);
                        using (var gt = Graphics.FromImage(target))
                        {
                            gt.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                            gt.DrawImage(full, 0, 0, tw, th);
                            if (captureCursor) DrawCursor(gt, srcX, srcY, sw, sh, tw, th);
                        }
                        target.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        var data = target.LockBits(new System.Drawing.Rectangle(0, 0, tw, th), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                        target.UnlockBits(data);
                        avi.WriteFrame(handle.AddrOfPinnedObject(), buf.Length);

                        double wait = interval - swatch.ElapsedMilliseconds;
                        if (wait > 0) Thread.Sleep((int)wait);
                    }
                }
                finally
                {
                    handle.Free();
                }
                Dispatcher.Invoke(() =>
                {
                    AddItem(_screenItems, file);
                    SetScreenStatus(UiLanguage.L($"已保存：{Path.GetFileName(file)}（{avi.FrameCount} 帧）",
                        $"Saved: {Path.GetFileName(file)} ({avi.FrameCount} frames)"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetScreenStatus(UiLanguage.L("录像出错：" + ex.Message, "Record error: " + ex.Message)));
            }
        }, token);
    }

    private void BtnScreenStop_Click(object sender, RoutedEventArgs e)
    {
        if (!_screenRecording) return;
        _screenCts?.Cancel();
        try { _screenTask?.Wait(3000); } catch { }
        _screenRecording = false;
        BtnScreenStart.IsEnabled = true;
        BtnScreenStop.IsEnabled = false;
        TxtScreenState.Text = UiLanguage.L("未开始", "Idle");
        TxtScreenTimer.Text = "00:00";
        StopRecPulse();
    }

    private static void DrawCursor(Graphics g, int srcX, int srcY, int sw, int sh, int tw, int th)
    {
        try
        {
            var pos = System.Windows.Forms.Cursor.Position;
            int cx = pos.X - srcX, cy = pos.Y - srcY;
            if (cx < 0 || cy < 0 || cx > sw || cy > sh) return;
            double sx = (double)tw / sw, sy = (double)th / sh;
            int dx = (int)(cx * sx), dy = (int)(cy * sy);
            var cur = System.Windows.Forms.Cursor.Current ?? System.Windows.Forms.Cursors.Default;
            cur.Draw(g, new System.Drawing.Rectangle(dx, dy, cur.Size.Width, cur.Size.Height));
        }
        catch { }
    }

    // ===== 计时器刷新 =====

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioRecording)
        {
            var ts = DateTime.Now - _audioStart;
            TxtAudioTimer.Text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }
        if (_screenRecording)
        {
            var ts = DateTime.Now - _screenStart;
            TxtScreenTimer.Text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }
        if (!_audioRecording && !_screenRecording && _uiTimer.IsEnabled)
            _uiTimer.Stop();
    }

    // ===== 文件夹选择 =====

    private void BtnAudioBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = UiLanguage.L("选择录音保存文件夹", "Select audio save folder"),
            SelectedPath = _audioFolder
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _audioFolder = dlg.SelectedPath;
            TxtAudioFolder.Text = _audioFolder;
        }
    }

    private void BtnScreenBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = UiLanguage.L("选择录像保存文件夹", "Select video save folder"),
            SelectedPath = _screenFolder
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _screenFolder = dlg.SelectedPath;
            TxtScreenFolder.Text = _screenFolder;
        }
    }

    // ===== 列表 / 打开 =====

    private void AddItem(ObservableCollection<MediaItem> list, string path)
    {
        list.Insert(0, new MediaItem { Name = Path.GetFileName(path), Path = path });
    }

    private void LoadExisting()
    {
        foreach (var f in Directory.Exists(_audioFolder) ? Directory.GetFiles(_audioFolder, "*.wav") : Array.Empty<string>())
            if (File.GetLastWriteTime(f) > DateTime.Now.AddDays(-30)) _audioItems.Add(new MediaItem { Name = Path.GetFileName(f), Path = f });
        foreach (var f in Directory.Exists(_screenFolder) ? Directory.GetFiles(_screenFolder, "*.avi") : Array.Empty<string>())
            if (File.GetLastWriteTime(f) > DateTime.Now.AddDays(-30)) _screenItems.Add(new MediaItem { Name = Path.GetFileName(f), Path = f });
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string p && File.Exists(p))
            Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string p)
        {
            string dir = File.Exists(p) ? Path.GetDirectoryName(p) ?? p : p;
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true });
        }
    }

    // ===== 辅助 =====

    private void SetAudioStatus(string s) => TxtAudioState.Text = s;
    private void SetScreenStatus(string s) => TxtScreenState.Text = s;

    private static string TimeStamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

    private static string? ToShortPath(string longPath)
    {
        var sb = new StringBuilder(1024);
        int n = GetShortPathName(longPath, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : null;
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnAudioStart, System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
        ThemeManager.ApplyButtonTheme(BtnScreenStart, System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
        ThemeManager.ApplyButtonTheme(BtnAudioStop, System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6));
        ThemeManager.ApplyButtonTheme(BtnScreenStop, System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6));
        ThemeManager.ApplyButtonTheme(BtnAudioBrowse, System.Windows.Media.Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnScreenBrowse, System.Windows.Media.Color.FromRgb(0x4A, 0x90, 0xD9));
    }

    private void RefreshStrings()
    {
        TxtTitle.Text = UiLanguage.L("录音录像", "Recorder");
        TxtSub.Text = UiLanguage.L("麦克风录音与屏幕录像，录制文件保存在本地", "Microphone recording & screen capture, saved locally");
        TxtFpsLabel.Text = UiLanguage.L("帧率", "Frame rate");
        TxtScreenHint.Text = UiLanguage.L("提示：录像时建议先最小化本窗口，以免把工具箱本身也录进去。",
            "Tip: minimize this window while recording to avoid capturing the toolbox itself.");
        if (!_audioRecording) TxtAudioState.Text = UiLanguage.L("未开始", "Idle");
        if (!_screenRecording) TxtScreenState.Text = UiLanguage.L("未开始", "Idle");
    }
}
