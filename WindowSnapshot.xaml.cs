using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Drawing.Imaging;

namespace WINHELP;

/// <summary>截图 / 快照页：区域截图与标注。</summary>
public partial class WindowSnapshot : UserControl
{
    private enum Tool { None, Arrow, Text, Mosaic, Picker }

    private Tool _tool = Tool.None;
    private WriteableBitmap? _wb;
    private Button? _activeToolBtn;
    private Button? _activeColorBtn;

    // 标注样式（可在工具选项面板中调整）
    private Color _annoColor = Color.FromRgb(0xE7, 0x4C, 0x3C); // 默认红
    private int _annoSize = 3;        // 箭头粗细
    private int _mosaicBlock = 12;    // 马赛克块大小

    // 缩放（适应窗口）
    private double _zoom = 1.0;
    private bool _fit = false;

    // 拖拽草稿
    private Line? _draftLine;
    private System.Windows.Point _dragStart;
    private Rectangle? _draftRect;

    private static readonly Color[] _palette =
    {
        Color.FromRgb(0xE7, 0x4C, 0x3C), // 红
        Color.FromRgb(0xF1, 0xC4, 0x0F), // 黄
        Color.FromRgb(0x27, 0xAE, 0x60), // 绿
        Color.FromRgb(0x34, 0x98, 0xDB), // 蓝
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 白
        Color.FromRgb(0x00, 0x00, 0x00), // 黑
    };

    public WindowSnapshot()
    {
        InitializeComponent();
        ApplyTheme();
        RefreshStrings();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(() => { ApplyTheme(); RefreshStrings(); });
        UiLanguage.Changed += () => Dispatcher.Invoke(RefreshStrings);
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnCaptureFull, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnCaptureRegion, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnCaptureAll, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnArrow, Color.FromRgb(0x5B, 0x8D, 0xEF));
        ThemeManager.ApplyButtonTheme(BtnText, Color.FromRgb(0x5B, 0x8D, 0xEF));
        ThemeManager.ApplyButtonTheme(BtnMosaic, Color.FromRgb(0x5B, 0x8D, 0xEF));
        ThemeManager.ApplyButtonTheme(BtnPicker, Color.FromRgb(0x8E, 0x7C, 0xC3));
        ThemeManager.ApplyButtonTheme(BtnClearAnno, Color.FromRgb(0x95, 0xA5, 0xA6));
        ThemeManager.ApplyButtonTheme(BtnClearImg, Color.FromRgb(0xE6, 0x7E, 0x22));
        ThemeManager.ApplyButtonTheme(BtnFit, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnActual, Color.FromRgb(0x4A, 0x90, 0xD9));
        ThemeManager.ApplyButtonTheme(BtnSave, Color.FromRgb(0x27, 0xAE, 0x60));
    }

    private void RefreshStrings()
    {
        TxtTitle.Text = UiLanguage.L("截图标注", "Snapshot & Annotate");
        TxtSub.Text = UiLanguage.L("截图 + 箭头 / 文字 / 马赛克标注 + 取色",
            "Capture + arrow / text / mosaic annotate + color picker");
        BtnCaptureFull.Content = UiLanguage.L("全屏截图", "Full Screen");
        BtnCaptureRegion.Content = UiLanguage.L("区域截图", "Region");
        BtnCaptureAll.Content = UiLanguage.L("全屏(多显示器)", "All Screens");
        BtnArrow.Content = UiLanguage.L("箭头", "Arrow");
        BtnText.Content = UiLanguage.L("文字", "Text");
        BtnMosaic.Content = UiLanguage.L("马赛克", "Mosaic");
        BtnPicker.Content = UiLanguage.L("取色器", "Picker");
        BtnClearAnno.Content = UiLanguage.L("清除标注", "Clear Anno");
        BtnClearImg.Content = UiLanguage.L("清除截图", "Clear Shot");
        BtnFit.Content = UiLanguage.L("适应窗口", "Fit");
        BtnActual.Content = UiLanguage.L("实际大小", "Actual");
        BtnSave.Content = UiLanguage.L("保存", "Save");

        TxtGrpShot.Text = UiLanguage.L("截图", "Capture");
        TxtGrpView.Text = UiLanguage.L("视图", "View");
        TxtGrpAnno.Text = UiLanguage.L("标注", "Annotate");
        TxtGrpAct.Text = UiLanguage.L("操作", "Actions");

        if (_wb == null && TxtStatus != null) TxtStatus.Text = UiLanguage.L("尚未截图", "No capture yet");
        // 工具选项面板若可见，按当前语言刷新文案
        if (ToolOptions.Visibility == Visibility.Visible) ShowToolOptions(_tool);
    }

    // ===== 截图 =====

    private void BtnCaptureFull_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bmp = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(b.X, b.Y, 0, 0, new System.Drawing.Size(b.Width, b.Height));
            bmp.SetResolution(96, 96);
            SetImage(bmp);
            TxtStatus.Text = UiLanguage.L("已捕获全屏截图", "Full-screen capture done");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("截图失败：" + ex.Message, "Capture failed: " + ex.Message);
        }
    }

    private void BtnCaptureAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length <= 1)
            {
                // 单显示器时退化为全屏截图
                BtnCaptureFull_Click(sender, e);
                return;
            }
            int minX = screens.Min(s => s.Bounds.X);
            int minY = screens.Min(s => s.Bounds.Y);
            int totalW = screens.Max(s => s.Bounds.Right) - minX;
            int totalH = screens.Max(s => s.Bounds.Bottom) - minY;
            using var bmp = new System.Drawing.Bitmap(totalW, totalH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                foreach (var s in screens)
                {
                    var b = s.Bounds;
                    g.CopyFromScreen(b.X, b.Y, b.X - minX, b.Y - minY, new System.Drawing.Size(b.Width, b.Height));
                }
            }
            bmp.SetResolution(96, 96);
            SetImage(bmp);
            TxtStatus.Text = UiLanguage.L($"已拼接 {screens.Length} 块屏幕截图", $"Stitched {screens.Length} screen(s)");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("多屏截图失败：" + ex.Message, "Multi-screen capture failed: " + ex.Message);
        }
    }

    private async void BtnCaptureRegion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new RegionCaptureWindow();
            win.ShowDialog();
            if (win.Result is Rect r && r.Width > 3 && r.Height > 3)
            {
                int x = (int)Math.Round(r.X);
                int y = (int)Math.Round(r.Y);
                int w = (int)Math.Round(r.Width);
                int h = (int)Math.Round(r.Height);
                TxtStatus.Text = UiLanguage.L("正在捕获区域…", "Capturing region…");
                await Task.Delay(60); // 等待选区窗口完全消失
                System.Drawing.Bitmap? bmp = null;
                await Task.Run(() =>
                {
                    bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var g = System.Drawing.Graphics.FromImage(bmp);
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
                    bmp.SetResolution(96, 96);
                });
                if (bmp != null) { SetImage(bmp); TxtStatus.Text = UiLanguage.L("已捕获区域截图", "Region captured"); }
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("区域截图失败：" + ex.Message, "Region capture failed: " + ex.Message);
        }
    }

    private void SetImage(System.Drawing.Bitmap src)
    {
        _wb = ToWriteableBitmap(src);
        ImgCapture.Source = _wb;
        ImgCapture.Width = _wb.PixelWidth;
        ImgCapture.Height = _wb.PixelHeight;
        CanvasHost.Width = _wb.PixelWidth;
        CanvasHost.Height = _wb.PixelHeight;
        AnnotationCanvas.Width = _wb.PixelWidth;
        AnnotationCanvas.Height = _wb.PixelHeight;
        AnnotationCanvas.Children.Clear();
        ColorSwatch.Visibility = Visibility.Collapsed;
        // 截图后自动适应窗口，确保整图可见、便于标注
        _fit = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)FitToView);
        UpdateImgInfo();
    }

    // ===== 缩放 / 适应窗口 =====

    private void BtnFit_Click(object sender, RoutedEventArgs e)
    {
        _fit = true;
        FitToView();
    }

    private void BtnActual_Click(object sender, RoutedEventArgs e)
    {
        _fit = false;
        _zoom = 1.0;
        ApplyZoom();
    }

    private void FitToView()
    {
        if (_wb == null) return;
        double vw = ImageAreaBorder.ActualWidth - 16; // 减去 Padding(8*2)
        double vh = ImageAreaBorder.ActualHeight - 16;
        if (vw <= 10 || vh <= 10) return;
        double s = Math.Min(vw / _wb.PixelWidth, vh / _wb.PixelHeight);
        if (s > 1.0) s = 1.0;        // 不放大超过原始尺寸
        if (s <= 0) s = 1.0;
        _zoom = s;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        CanvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        UpdateImgInfo();
    }

    private void ImageAreaBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fit && _wb != null) FitToView();
    }

    private static WriteableBitmap ToWriteableBitmap(System.Drawing.Bitmap src)
    {
        var wb = new WriteableBitmap(src.Width, src.Height, 96, 96, PixelFormats.Bgra32, null);
        var bd = src.LockBits(new System.Drawing.Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        wb.WritePixels(new Int32Rect(0, 0, src.Width, src.Height), bd.Scan0, bd.Stride * src.Height, bd.Stride);
        src.UnlockBits(bd);
        return wb;
    }

    // ===== 工具切换 =====

    private void BtnTool_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        Tool tool = btn == BtnArrow ? Tool.Arrow
                  : btn == BtnText ? Tool.Text
                  : btn == BtnMosaic ? Tool.Mosaic
                  : Tool.Picker;

        if (_tool == tool)
        {
            // 再次点击同一工具 → 取消选择
            _tool = Tool.None;
            SetActiveTool(null);
            ShowToolOptions(Tool.None);
            return;
        }
        _tool = tool;
        SetActiveTool(btn);
        ShowToolOptions(tool);
    }

    /// <summary>高亮当前激活的标注工具（用发光效果指示，模板无关、稳定可见）</summary>
    private void SetActiveTool(Button? btn)
    {
        if (_activeToolBtn != null)
            _activeToolBtn.Effect = null;
        _activeToolBtn = btn;
        if (_activeToolBtn != null)
            _activeToolBtn.Effect = MakeGlow();
    }

    private static DropShadowEffect MakeGlow()
        => new DropShadowEffect
        {
            Color = Color.FromRgb(0x4A, 0x90, 0xD9),
            BlurRadius = 10,
            ShadowDepth = 0,
            Opacity = 0.9
        };

    /// <summary>根据当前工具显示对应的选项面板（颜色 / 粗细 / 马赛克大小等）</summary>
    private void ShowToolOptions(Tool tool)
    {
        ToolOptionsPanel.Children.Clear();
        _activeColorBtn = null;
        if (tool == Tool.None) { ToolOptions.Visibility = Visibility.Collapsed; return; }

        if (tool == Tool.Arrow || tool == Tool.Text)
        {
            ToolOptionsPanel.Children.Add(MakeCaption(UiLanguage.L("颜色", "Color")));
            foreach (var c in _palette)
            {
                var b = new Button
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(c),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    Cursor = Cursors.Hand,
                    Tag = c
                };
                b.Click += OnColorClick;
                if (c == _annoColor)
                {
                    _activeColorBtn = b;
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
                    b.BorderThickness = new Thickness(3);
                }
                ToolOptionsPanel.Children.Add(b);
            }

            ToolOptionsPanel.Children.Add(MakeCaption(UiLanguage.L("粗细", "Thickness")));
            var slider = new Slider
            {
                Minimum = 1,
                Maximum = 8,
                Value = _annoSize,
                Width = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                IsSnapToTickEnabled = true,
                TickFrequency = 1
            };
            slider.ValueChanged += (_, _) => _annoSize = (int)Math.Round(slider.Value);
            ToolOptionsPanel.Children.Add(slider);
        }
        else if (tool == Tool.Mosaic)
        {
            ToolOptionsPanel.Children.Add(MakeCaption(UiLanguage.L("马赛克块大小", "Mosaic size")));
            AddSizeButton(UiLanguage.L("小", "S"), 8, _mosaicBlock == 8);
            AddSizeButton(UiLanguage.L("中", "M"), 14, _mosaicBlock == 14);
            AddSizeButton(UiLanguage.L("大", "L"), 22, _mosaicBlock == 22);
        }
        else if (tool == Tool.Picker)
        {
            ToolOptionsPanel.Children.Add(MakeCaption(UiLanguage.L("在图片上点击任意位置取色", "Click on the image to pick a color")));
        }

        ToolOptions.Visibility = Visibility.Visible;
    }

    private void AddSizeButton(string label, int size, bool selected)
    {
        var b = new Button
        {
            Content = label,
            Width = 56,
            Height = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Style = (Style)FindResource("GlassToolbarButton"),
            Cursor = Cursors.Hand,
            Tag = size
        };
        if (selected) b.Effect = MakeGlow();
        b.Click += (_, _) =>
        {
            _mosaicBlock = size;
            ShowToolOptions(Tool.Mosaic);
        };
        ToolOptionsPanel.Children.Add(b);
    }

    private static TextBlock MakeCaption(string text)
        => new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not Color c) return;
        _annoColor = c;
        if (_activeColorBtn != null)
        {
            _activeColorBtn.BorderBrush = new SolidColorBrush(Colors.Gray);
            _activeColorBtn.BorderThickness = new Thickness(1);
        }
        _activeColorBtn = b;
        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        b.BorderThickness = new Thickness(3);
    }

    // ===== 画布交互 =====

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_wb == null) return;
        var p = e.GetPosition(AnnotationCanvas);

        switch (_tool)
        {
            case Tool.Arrow:
                _dragStart = p;
                _draftLine = new Line
                {
                    X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y,
                    Stroke = new SolidColorBrush(_annoColor),
                    StrokeThickness = _annoSize
                };
                AnnotationCanvas.Children.Add(_draftLine);
                AnnotationCanvas.CaptureMouse();
                break;

            case Tool.Text:
                var tb = new TextBox
                {
                    Text = UiLanguage.L("文字", "Text"),
                    FontSize = 16, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(_annoColor),
                    Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                    BorderThickness = new Thickness(0),
                    Width = 120
                };
                Canvas.SetLeft(tb, p.X); Canvas.SetTop(tb, p.Y);
                AnnotationCanvas.Children.Add(tb);
                tb.Focus(); tb.SelectAll();
                break;

            case Tool.Mosaic:
                _dragStart = p;
                _draftRect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0))
                };
                Canvas.SetLeft(_draftRect, p.X); Canvas.SetTop(_draftRect, p.Y);
                AnnotationCanvas.Children.Add(_draftRect);
                AnnotationCanvas.CaptureMouse();
                break;

            case Tool.Picker:
                PickColor((int)Math.Round(p.X), (int)Math.Round(p.Y));
                break;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_wb == null) return;
        var p = e.GetPosition(AnnotationCanvas);
        if (_draftLine != null)
        {
            _draftLine.X2 = p.X; _draftLine.Y2 = p.Y;
        }
        else if (_draftRect != null)
        {
            var x = Math.Min(p.X, _dragStart.X);
            var y = Math.Min(p.Y, _dragStart.Y);
            Canvas.SetLeft(_draftRect, x); Canvas.SetTop(_draftRect, y);
            _draftRect.Width = Math.Abs(p.X - _dragStart.X);
            _draftRect.Height = Math.Abs(p.Y - _dragStart.Y);
        }

        // 状态栏：实时光标坐标
        int cx = (int)Math.Round(p.X), cy = (int)Math.Round(p.Y);
        if (cx >= 0 && cy >= 0 && cx < _wb.PixelWidth && cy < _wb.PixelHeight)
        {
            TxtCursor.Text = $"x:{cx}  y:{cy}";
            TxtCursor.Visibility = Visibility.Visible;
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_wb == null) return;
        var p = e.GetPosition(AnnotationCanvas);

        if (_draftLine != null)
        {
            AddArrowHead(_draftLine, _dragStart, p);
            _draftLine = null;
            AnnotationCanvas.ReleaseMouseCapture();
        }
        else if (_draftRect != null)
        {
            int x = (int)Math.Round(Math.Min(p.X, _dragStart.X));
            int y = (int)Math.Round(Math.Min(p.Y, _dragStart.Y));
            int w = (int)Math.Round(Math.Abs(p.X - _dragStart.X));
            int h = (int)Math.Round(Math.Abs(p.Y - _dragStart.Y));
            AnnotationCanvas.Children.Remove(_draftRect);
            _draftRect = null;
            AnnotationCanvas.ReleaseMouseCapture();
            if (w > 2 && h > 2) ApplyMosaic(x, y, w, h);
        }
    }

    private void AddArrowHead(Line line, System.Windows.Point start, System.Windows.Point end)
    {
        double a = Math.Atan2(end.Y - start.Y, end.X - start.X);
        double head = 12;
        var left = new System.Windows.Point(end.X - head * Math.Cos(a - Math.PI / 7), end.Y - head * Math.Sin(a - Math.PI / 7));
        var right = new System.Windows.Point(end.X - head * Math.Cos(a + Math.PI / 7), end.Y - head * Math.Sin(a + Math.PI / 7));
        var poly = new Polygon
        {
            Fill = line.Stroke,
            Points = new PointCollection { end, left, right }
        };
        AnnotationCanvas.Children.Add(poly);
    }

    // ===== 马赛克 / 取色 =====

    private void ApplyMosaic(int x, int y, int w, int h)
    {
        if (_wb == null) return;
        x = Math.Max(0, Math.Min(x, _wb.PixelWidth - 1));
        y = Math.Max(0, Math.Min(y, _wb.PixelHeight - 1));
        w = Math.Min(w, _wb.PixelWidth - x);
        h = Math.Min(h, _wb.PixelHeight - y);
        if (w <= 0 || h <= 0) return;

        int block = _mosaicBlock;
        try
        {
            for (int by = y; by < y + h; by += block)
            {
                for (int bx = x; bx < x + w; bx += block)
                {
                    int bw = Math.Min(block, x + w - bx);
                    int bh = Math.Min(block, y + h - by);
                    int stride = bw * 4;
                    var buf = new byte[stride * bh];
                    _wb.CopyPixels(new Int32Rect(bx, by, bw, bh), buf, stride, 0);

                    long sr = 0, sg = 0, sb = 0;
                    int n = bw * bh;
                    for (int i = 0; i < buf.Length; i += 4) { sb += buf[i]; sg += buf[i + 1]; sr += buf[i + 2]; }
                    byte ar = (byte)(sr / n), ag = (byte)(sg / n), ab = (byte)(sb / n);
                    var fill = new byte[stride * bh];
                    for (int i = 0; i < fill.Length; i += 4) { fill[i] = ab; fill[i + 1] = ag; fill[i + 2] = ar; fill[i + 3] = 255; }
                    _wb.WritePixels(new Int32Rect(bx, by, bw, bh), fill, stride, 0);
                }
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("马赛克失败：" + ex.Message, "Mosaic failed: " + ex.Message);
        }
    }

    private void PickColor(int x, int y)
    {
        if (_wb == null) return;
        x = Math.Max(0, Math.Min(x, _wb.PixelWidth - 1));
        y = Math.Max(0, Math.Min(y, _wb.PixelHeight - 1));
        var px = new byte[4];
        try
        {
            _wb.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
            var c = Color.FromRgb(px[2], px[1], px[0]);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            ColorSwatch.Visibility = Visibility.Visible;
            ColorSwatch.Background = new SolidColorBrush(c);
            TxtColor.Text = hex;
            TxtStatus.Text = UiLanguage.L($"取色：{hex}（坐标 {x},{y}）", $"Picked: {hex} (at {x},{y})");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("取色失败：" + ex.Message, "Pick failed: " + ex.Message);
        }
    }

    // ===== 清除 / 保存 =====

    private void BtnClearAnno_Click(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.Children.Clear();
        ColorSwatch.Visibility = Visibility.Collapsed;
    }

    private void BtnClearImg_Click(object sender, RoutedEventArgs e)
    {
        _wb = null;
        _fit = false;
        _zoom = 1.0;
        ImgCapture.Source = null;
        ImgCapture.Width = 0; ImgCapture.Height = 0;
        AnnotationCanvas.Children.Clear();
        CanvasHost.LayoutTransform = null;
        CanvasHost.Width = 0; CanvasHost.Height = 0;
        ColorSwatch.Visibility = Visibility.Collapsed;
        TxtCursor.Visibility = Visibility.Collapsed;
        TxtImgInfo.Visibility = Visibility.Collapsed;
        TxtStatus.Text = UiLanguage.L("已清除截图，可重新截图", "Screenshot cleared, capture again");
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_wb == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            FileName = "snapshot.png",
            Title = UiLanguage.L("保存截图", "Save Snapshot")
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            // 临时还原缩放，确保按原始分辨率导出
            var prev = CanvasHost.LayoutTransform;
            CanvasHost.LayoutTransform = null;
            var rtb = new RenderTargetBitmap(_wb.PixelWidth, _wb.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(CanvasHost);
            CanvasHost.LayoutTransform = prev;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write);
            enc.Save(fs);
            TxtStatus.Text = UiLanguage.L("已保存：" + dlg.FileName, "Saved: " + dlg.FileName);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("保存失败：" + ex.Message, "Save failed: " + ex.Message);
        }
    }

    private void UpdateImgInfo()
    {
        if (_wb == null) { TxtImgInfo.Visibility = Visibility.Collapsed; return; }
        TxtImgInfo.Text = $"{_wb.PixelWidth} × {_wb.PixelHeight}  ·  {(_zoom * 100):F0}%";
        TxtImgInfo.Visibility = Visibility.Visible;
    }

    // ===== 区域截图窗口 =====

    /// <summary>窗口：RegionCaptureWindow。</summary>
    private sealed class RegionCaptureWindow : Window
    {
        public Rect? Result;
        private System.Windows.Point _start;
        private Rectangle? _rect;
        private readonly Canvas _canvas = new() { Background = Brushes.Transparent };

        public RegionCaptureWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(2, 0, 0, 0));
            Topmost = true;
            WindowState = WindowState.Maximized;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            Content = _canvas;

            _canvas.MouseLeftButtonDown += (_, ev) =>
            {
                _start = ev.GetPosition(_canvas);
                _rect = new Rectangle
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(50, 74, 144, 217))
                };
                Canvas.SetLeft(_rect, _start.X); Canvas.SetTop(_rect, _start.Y);
                _canvas.Children.Add(_rect);
                _canvas.CaptureMouse();
            };
            _canvas.MouseMove += (_, ev) =>
            {
                if (_rect == null) return;
                var p = ev.GetPosition(_canvas);
                var x = Math.Min(p.X, _start.X); var y = Math.Min(p.Y, _start.Y);
                Canvas.SetLeft(_rect, x); Canvas.SetTop(_rect, y);
                _rect.Width = Math.Abs(p.X - _start.X);
                _rect.Height = Math.Abs(p.Y - _start.Y);
            };
            _canvas.MouseLeftButtonUp += (_, ev) =>
            {
                if (_rect == null) return;
                _canvas.ReleaseMouseCapture();
                var p = ev.GetPosition(_canvas);
                var x = Math.Min(p.X, _start.X); var y = Math.Min(p.Y, _start.Y);
                var w = Math.Abs(p.X - _start.X); var h = Math.Abs(p.Y - _start.Y);
                if (w > 3 && h > 3) Result = new Rect(x, y, w, h);
                Close();
            };
            PreviewKeyDown += (_, ev) =>
            {
                if (ev.Key == Key.Escape) { Result = null; Close(); }
            };
        }
    }
}
