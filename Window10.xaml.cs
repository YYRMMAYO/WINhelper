using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using Microsoft.Win32;

namespace WINHELP;

public partial class Window10 : UserControl
{
    /// <summary>单个清理类别（临时文件 / 回收站 / 浏览器缓存 / 更新缓存 / 缩略图缓存）</summary>
    private sealed class CleanCategory
    {
        public string Key = "";
        public string Name = "";
        public string Icon = "";
        public string Desc = "";
        public bool Selected = true;
        public long Size;
        public int Count;
        public Func<Task>? Scan;
        public Action? Clean;
        public CheckBox? Box;
        public TextBlock? SizeTb;
        public TextBlock? NameTb;
        public TextBlock? DescTb;
    }

    // 各类别的中英对照（键 = CleanCategory.Key）
    private static readonly Dictionary<string, (string ZhN, string EnN, string ZhD, string EnD)> CatText = new()
    {
        ["temp"]    = ("临时文件", "Temp Files", "系统与应用的临时缓存（%TEMP% 等）", "Temp cache of system & apps (%TEMP% etc.)"),
        ["recycle"] = ("回收站", "Recycle Bin", "已删除到回收站、尚未彻底清除的文件", "Files deleted to recycle bin, not yet removed"),
        ["browser"] = ("浏览器缓存", "Browser Cache", "Chrome / Edge 等浏览器缓存（不影响登录与书签）", "Chrome / Edge cache (keeps login & bookmarks)"),
        ["update"]  = ("Windows 更新缓存", "Windows Update Cache", "SoftwareDistribution 下载缓存（部分需管理员权限）", "SoftwareDistribution download cache (some need admin)"),
        ["thumb"]   = ("缩略图缓存", "Thumbnail Cache", "资源管理器为图片/视频生成的缩略图缓存", "Thumbnails generated for pictures/videos"),
        ["privacy"] = ("隐私痕迹", "Privacy Traces", "最近文档 / 跳转列表 / WebCache 使用痕迹（仅清理索引，不影响已存文件）", "Recent docs / jump list / WebCache traces (index only)"),
    };

    private readonly List<CleanCategory> _categories = new();
    private readonly List<string> _tempFiles = new();
    private long _reclaimableBytes;

    // ===== N2 Treemap 状态 =====
    private bool _treemapScanning;
    private System.Threading.CancellationTokenSource? _treemapCts;
    private readonly Stack<Cleaner.TreeNode> _treemapStack = new();
    private Cleaner.TreeNode? _treemapRoot;
    private Cleaner.TreeNode? _treemapCurrent;
    private long _treemapFileCount, _treemapDirCount;

    private static readonly Brush[] TreemapPalette =
    {
        new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)),
        new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
        new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),
        new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD)),
        new SolidColorBrush(Color.FromRgb(0x16, 0xA0, 0x85)),
        new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
        new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
        new SolidColorBrush(Color.FromRgb(0xD4, 0xAC, 0x0E)),
        new SolidColorBrush(Color.FromRgb(0x29, 0x80, 0xB9)),
        new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
    };

    public Window10()
    {
        InitializeComponent();
        ApplyTheme();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);

        BuildCategories();
        LoadDiskInfo();
        InitNewControls();
        ApplyLocalization();

        UiLanguage.Changed += () => Dispatcher.Invoke(ApplyLocalization);
    }

    // ===== 新增控件初始化（N2 / N7 / N13） =====

    private void InitNewControls()
    {
        // N2 驱动器列表（仅固定磁盘，默认 C:\）
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            {
                CmbDrives.Items.Add(d.RootDirectory.FullName);
            }
            if (CmbDrives.Items.Count == 0) CmbDrives.Items.Add("C:\\");
            int cIdx = CmbDrives.Items.IndexOf("C:\\");
            CmbDrives.SelectedIndex = cIdx >= 0 ? cIdx : 0;
        }
        catch { if (CmbDrives.Items.Count == 0) CmbDrives.Items.Add("C:\\"); CmbDrives.SelectedIndex = 0; }

        // N7 还原点开关
        ChkRestorePoint.IsChecked = SettingsManager.Current.RestorePointEnabled;
        ChkRestorePoint.Checked += (_, _) => { SettingsManager.Current.RestorePointEnabled = true; SettingsManager.Save(); };
        ChkRestorePoint.Unchecked += (_, _) => { SettingsManager.Current.RestorePointEnabled = false; SettingsManager.Save(); };

        // N13 隐私痕迹开关
        ChkPrivacy.IsChecked = SettingsManager.Current.PrivacyCleanEnabled;
        ChkPrivacy.Checked += (_, _) => { SettingsManager.Current.PrivacyCleanEnabled = true; SettingsManager.Save(); };
        ChkPrivacy.Unchecked += (_, _) => { SettingsManager.Current.PrivacyCleanEnabled = false; SettingsManager.Save(); };
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnScan, Color.FromRgb(0x27, 0xAE, 0x60),
            hoverColor: Color.FromRgb(0x1E, 0x8E, 0x4F));
        ThemeManager.ApplyButtonTheme(BtnClean, ThemeManager.AccentColor);
        ThemeManager.ApplyButtonTheme(BtnTreemapScan, ThemeManager.AccentColor);
        ThemeManager.ApplyButtonTheme(BtnTreemapBack, Color.FromRgb(0x7F, 0x8C, 0x8D),
            hoverColor: Color.FromRgb(0x6B, 0x77, 0x85));
        ThemeManager.ApplyButtonTheme(BtnRestorePoint, Color.FromRgb(0x27, 0xAE, 0x60),
            hoverColor: Color.FromRgb(0x1E, 0x8E, 0x4F));
    }

        /// <summary>语言切换时重新设置新增文本（参与 i18n）。</summary>
        private void ApplyLocalization()
        {
            // 顶部 + 概览 + 磁盘标签
            TxtTitle.Text = UiLanguage.L("系统清理", "System Cleaner");
            TxtSubtitle.Text = UiLanguage.L("扫描并清理 C 盘垃圾，安全释放磁盘空间",
                "Scan & clean C: drive junk, safely free up disk space");
            BtnScan.Content = UiLanguage.L("开始扫描", "Start Scan");
            TxtReclaimLabel.Text = UiLanguage.L("可清理空间", "Reclaimable Space");
            if (TxtReclaim.Text is "尚未扫描" or "Not scanned")
                TxtReclaim.Text = UiLanguage.L("尚未扫描", "Not scanned");
            TxtTemp.Text = UiLanguage.L("点击下方「开始扫描」分析 C 盘垃圾",
                "Click Start Scan to analyze C: drive junk");
            TxtCatTitle.Text = UiLanguage.L("可清理项（勾选后清理）", "Cleanup Items (check to clean)");
            TxtBigTitle.Text = UiLanguage.L("大文件建议（>200MB，点击打开所在文件夹）",
                "Large File Suggestions (>200MB, click to open folder)");
            BtnClean.Content = UiLanguage.L("立即清理选中项", "Clean Selected Now");
            if (TxtStatus.Text is "点击「开始扫描」分析 C 盘垃圾文件" or "Click Start Scan to analyze C: drive junk")
                TxtStatus.Text = UiLanguage.L("点击「开始扫描」分析 C 盘垃圾文件",
                    "Click Start Scan to analyze C: drive junk");

            // 各类别名称 / 描述
            foreach (var c in _categories)
            {
                if (CatText.TryGetValue(c.Key, out var t))
                {
                    if (c.NameTb != null) c.NameTb.Text = UiLanguage.L(t.ZhN, t.EnN);
                    if (c.DescTb != null) c.DescTb.Text = UiLanguage.L(t.ZhD, t.EnD);
                }
            }

            // 磁盘信息标签（重新读取并格式化，保持当前语言）
            LoadDiskInfo();

            // Treemap / 安全设置（原有）
            TxtTreemapTitle.Text = UiLanguage.L("磁盘空间可视化", "Disk Space Treemap");
        TxtDriveLabel.Text = UiLanguage.L("驱动器：", "Drive: ");
        BtnTreemapScan.Content = UiLanguage.L("扫描", "Scan");
        BtnTreemapBack.Content = UiLanguage.L("返回", "Back");
        TxtSettingsTitle.Text = UiLanguage.L("安全设置", "Safety Settings");
        ChkRestorePoint.Content = UiLanguage.L("清理前自动创建系统还原点", "Auto-create a system restore point before cleaning");
        BtnRestorePoint.Content = UiLanguage.L("立即创建系统还原点", "Create restore point now");
        ChkPrivacy.Content = UiLanguage.L("一键优化时包含隐私痕迹清理", "Include privacy traces in one-click optimize");
        if (TxtRestoreResult.Text.Length == 0) TxtRestoreResult.Text = UiLanguage.L("尚未创建", "Not created yet");
        UpdateTreemapInfo();
    }

    private static string Fmt(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    // ===== 类别构建 =====

    private void BuildCategories()
    {
        // 1) 临时文件
        {
            var cat = new CleanCategory
            {
                Key = "temp", Name = "临时文件", Icon = "🗑️",
                Desc = "系统与应用的临时缓存（%TEMP% 等）"
            };
            cat.Scan = async () =>
            {
                var dirs = TempDirs();
                var (size, count) = await Task.Run(() => SumMatching(dirs, "*", SearchOption.TopDirectoryOnly));
                _tempFiles.Clear();
                foreach (var d in dirs)
                {
                    if (!Directory.Exists(d)) continue;
                    try { foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.TopDirectoryOnly)) _tempFiles.Add(f); }
                    catch { }
                }
                cat.Size = size; cat.Count = count;
            };
            cat.Clean = () =>
            {
                foreach (var f in _tempFiles)
                {
                    try { var fi = new FileInfo(f); if (fi.Exists) fi.Delete(); } catch { }
                }
                _tempFiles.Clear();
            };
            RegisterCategory(cat);
        }

        // 2) 回收站
        {
            var cat = new CleanCategory
            {
                Key = "recycle", Name = "回收站", Icon = "♻️",
                Desc = "已删除到回收站、尚未彻底清除的文件"
            };
            cat.Scan = async () =>
            {
                var (size, count) = await Task.Run(QueryRecycleBin);
                cat.Size = size; cat.Count = (int)count;
            };
            cat.Clean = () => EmptyRecycleBin();
            RegisterCategory(cat);
        }

        // 3) 浏览器缓存
        {
            var cat = new CleanCategory
            {
                Key = "browser", Name = "浏览器缓存", Icon = "🌐",
                Desc = "Chrome / Edge 等浏览器缓存（不影响登录与书签）"
            };
            cat.Scan = async () =>
            {
                var dirs = BrowserCacheDirs();
                var (size, count) = await Task.Run(() => SumMatching(dirs, "*", SearchOption.AllDirectories));
                cat.Size = size; cat.Count = count;
            };
            cat.Clean = () => DeleteDirs(BrowserCacheDirs(), SearchOption.AllDirectories);
            RegisterCategory(cat);
        }

        // 4) Windows 更新缓存
        {
            var cat = new CleanCategory
            {
                Key = "update", Name = "Windows 更新缓存", Icon = "⬇️",
                Desc = "SoftwareDistribution 下载缓存（部分需管理员权限）"
            };
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")!;
            cat.Scan = async () =>
            {
                var (size, count) = await Task.Run(() => SumMatching(new[] { dir }, "*", SearchOption.AllDirectories));
                cat.Size = size; cat.Count = count;
            };
            cat.Clean = () => DeleteDirs(new[] { dir }, SearchOption.AllDirectories);
            RegisterCategory(cat);
        }

        // 5) 缩略图缓存
        {
            var cat = new CleanCategory
            {
                Key = "thumb", Name = "缩略图缓存", Icon = "🖼️",
                Desc = "资源管理器为图片/视频生成的缩略图缓存"
            };
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer")!;
            cat.Scan = async () =>
            {
                var (size, count) = await Task.Run(() => SumMatching(new[] { dir }, "thumbcache_*.db", SearchOption.TopDirectoryOnly));
                cat.Size = size; cat.Count = count;
            };
            cat.Clean = () => DeleteMatching(new[] { dir }, "thumbcache_*.db");
            RegisterCategory(cat);
        }

        // 6) 隐私痕迹（N13）
        {
            var cat = new CleanCategory
            {
                Key = "privacy", Name = "隐私痕迹", Icon = "🔒",
                Desc = "最近文档 / 跳转列表 / WebCache 使用痕迹（仅清理索引，不影响已存文件）"
            };
            cat.Scan = async () =>
            {
                var (size, count) = await Task.Run(() => Cleaner.QueryPrivacyTraces());
                cat.Size = size; cat.Count = count;
            };
            cat.Clean = () => Cleaner.CleanPrivacyTraces();
            RegisterCategory(cat);
        }
    }

    private void RegisterCategory(CleanCategory cat)
    {
        _categories.Add(cat);

        var border = new Border { Style = (Style)FindResource("CatRow") };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox
        {
            IsChecked = cat.Selected,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        cb.Checked += (_, _) => { cat.Selected = true; Recalc(); };
        cb.Unchecked += (_, _) => { cat.Selected = false; Recalc(); };
        Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        var icon = new TextBlock
        {
            Text = cat.Icon, FontSize = 18, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var (zhN, enN, zhD, enD) = CatText.TryGetValue(cat.Key, out var ct)
            ? ct : (cat.Name, cat.Name, cat.Desc, cat.Desc);
        cat.Name = UiLanguage.L(zhN, enN);
        cat.Desc = UiLanguage.L(zhD, enD);

        var nameTb = new TextBlock
        {
            Text = cat.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
        };
        var descTb = new TextBlock
        {
            Text = cat.Desc, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
            Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        sp.Children.Add(nameTb);
        sp.Children.Add(descTb);
        Grid.SetColumn(sp, 2);
        grid.Children.Add(sp);

        var sizeTb = new TextBlock
        {
            Text = "—", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(sizeTb, 3);
        grid.Children.Add(sizeTb);

        border.Child = grid;
        cat.Box = cb; cat.SizeTb = sizeTb; cat.NameTb = nameTb; cat.DescTb = descTb;
        CategoryPanel.Children.Add(border);
    }

    // ===== 磁盘信息 =====

    // 动画填充比例：0→实际占用，同时驱动磁盘条与环形图
    public static readonly DependencyProperty FillPctProperty =
        DependencyProperty.Register("FillPct", typeof(double), typeof(Window10),
            new PropertyMetadata(0.0, (d, e) => ((Window10)d).OnFillPctChanged(e)));

    public double FillPct
    {
        get => (double)GetValue(FillPctProperty);
        set => SetValue(FillPctProperty, value);
    }

    private long _diskTotal, _diskFree, _diskUsed;
    private double _diskPct;
    private static readonly double RingC = 2 * Math.PI * 48; // 环形图周长（半径 48）

    private static Color Lighten(Color c, double amt)
    {
        double t = amt;
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t),
            (byte)(c.G + (255 - c.G) * t),
            (byte)(c.B + (255 - c.B) * t));
    }

    private void OnFillPctChanged(DependencyPropertyChangedEventArgs e)
    {
        double cur = (double)e.NewValue;
        double pct = _diskPct;
        if (pct <= 0) return;

        Color usedColor = pct > 0.9 ? Color.FromRgb(0xE7, 0x4C, 0x3C)
                          : pct > 0.75 ? Color.FromRgb(0xE6, 0x7E, 0x22)
                          : Color.FromRgb(0x4A, 0x90, 0xD9);
        var grad = new LinearGradientBrush(Lighten(usedColor, 0.30), usedColor, new Point(0, 0), new Point(1, 0));

        // 磁盘条：按比例平滑增长
        BarGrid.ColumnDefinitions[0].Width = new GridLength(cur * 100, GridUnitType.Star);
        BarGrid.ColumnDefinitions[1].Width = new GridLength((1 - cur) * 100, GridUnitType.Star);
        BarUsed.Background = grad;

        // 环形图：描边虚线刻画占比
        RingValue.Stroke = grad;
        RingValue.StrokeDashArray = new DoubleCollection { cur * RingC, RingC };
        RingPct.Text = $"{(int)Math.Round(cur * 100)}%";
        RingPct.Foreground = new SolidColorBrush(usedColor);
    }

    private void LoadDiskInfo()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                long used = total - free;
                double pct = total > 0 ? (double)used / total : 0;
                _diskTotal = total; _diskFree = free; _diskUsed = used; _diskPct = pct;

                TxtDiskTotal.Text = $"{UiLanguage.L("总容量：", "Total: ")}{Fmt(total)}";

                // 占用率越高颜色越警示（绿→橙→红）
                Color usedColor = pct > 0.9 ? Color.FromRgb(0xE7, 0x4C, 0x3C)
                                  : pct > 0.75 ? Color.FromRgb(0xE6, 0x7E, 0x22)
                                  : Color.FromRgb(0x4A, 0x90, 0xD9);
                TxtPctLabel.Text = $"{pct * 100:0}%";
                TxtPctLabel.Foreground = new SolidColorBrush(usedColor);

                TxtBarUsed.Text = pct >= 0.10 ? $"{UiLanguage.L("已用", "Used")} {Fmt(used)}" : "";
                TxtBarFree.Text = (1 - pct) >= 0.10 ? $"{UiLanguage.L("可用", "Free")} {Fmt(free)}" : "";
                TxtUsedFree.Text = $"{Fmt(free)} {UiLanguage.L("可用", "available")} · " +
                                   $"{UiLanguage.L("共", "of")} {Fmt(total)} · " +
                                   $"{UiLanguage.L("已用", "used")} {Fmt(used)}（{pct * 100:0}%）";

                // 动画填充：从 0 平滑增长到实际占用，同时驱动磁盘条 + 环形图
                BeginAnimation(FillPctProperty, new DoubleAnimation(0, pct, TimeSpan.FromSeconds(0.9))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
        }
        catch { }
    }

    // ===== 扫描 =====

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnScan.IsEnabled = false;
            TxtStatus.Text = UiLanguage.L("正在扫描…", "Scanning…");
            ScanProgress.Visibility = Visibility.Visible;
            ScanProgress.Value = 0;
            BigFilePanel.Children.Clear();

            int total = _categories.Count;
            int done = 0;
            foreach (var cat in _categories)
            {
                if (cat.SizeTb != null) cat.SizeTb.Text = UiLanguage.L("计算中…", "Calculating…");
                try { if (cat.Scan != null) await cat.Scan(); }
                catch { cat.Size = 0; cat.Count = 0; }
                if (cat.SizeTb != null) cat.SizeTb.Text = $"{Fmt(cat.Size)} · {cat.Count} {UiLanguage.L("项", "items")}";
                done++;
                ScanProgress.Value = done * 100.0 / total;
                await Task.Delay(10);
            }

            await ScanBigFilesAsync();

            ScanProgress.Value = 100;
            Recalc();
            TxtStatus.Text = _reclaimableBytes > 0
                ? UiLanguage.L($"扫描完成：可清理约 {Fmt(_reclaimableBytes)}", $"Scan done: about {Fmt(_reclaimableBytes)} reclaimable")
                : UiLanguage.L("扫描完成：未发现可清理的垃圾", "Scan done: no junk found");
            BtnScan.IsEnabled = true;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("扫描出错：", "Scan error: ") + ex.Message;
            BtnScan.IsEnabled = true;
        }
    }

    private async Task ScanBigFilesAsync()
    {
        var big = new List<(string path, long size)>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        await Task.Run(() =>
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (fi.Exists && fi.Length > 200L * 1024 * 1024)
                                lock (big) big.Add((fi.FullName, fi.Length));
                        }
                        catch { }
                    }
                }
                catch { }
            }
        });

        big.Sort((a, b) => b.size.CompareTo(a.size));
        big = big.Take(12).ToList();

        if (big.Count > 0)
        {
            foreach (var (path, size) in big)
            {
                var row = AddBigRow(Path.GetFileName(path), $"{Fmt(size)} · {path}");
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonDown += (_, _) => OpenFolder(path);
            }
        }
        else
        {
                var empty = new TextBlock
                {
                    Text = UiLanguage.L("未发现明显的大文件，磁盘状态良好。", "No significant large files found; disk is healthy."),
                    FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
                    Margin = new Thickness(4, 0, 0, 0)
                };
            BigFilePanel.Children.Add(empty);
        }
    }

    private Border AddBigRow(string name, string sub)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xED, 0xF0, 0xF3)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
        });
        sp.Children.Add(new TextBlock
        {
            Text = sub, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
            Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        row.Child = sp;
        BigFilePanel.Children.Add(row);
        return row;
    }

    private void Recalc()
    {
        _reclaimableBytes = _categories.Where(c => c.Selected).Sum(c => c.Size);
        TxtReclaim.Text = _reclaimableBytes > 0 ? Fmt(_reclaimableBytes) : UiLanguage.L("暂无可选清理项", "No items selected");
        BtnClean.IsEnabled = _reclaimableBytes > 0;
    }

    // ===== 清理 =====

    private async void BtnClean_Click(object sender, RoutedEventArgs e)
    {
        var sel = _categories.Where(c => c.Selected && c.Size > 0).ToList();
        if (sel.Count == 0) return;

        var total = sel.Sum(c => c.Size);
        var r = MessageBox.Show(Window.GetWindow(this),
            UiLanguage.L(
                $"确定要清理以下 {sel.Count} 类垃圾文件，合计约 {Fmt(total)} 吗？\n\n仅删除临时文件、回收站、浏览器/更新缓存、缩略图缓存、隐私痕迹，不会动你的个人文件。",
                $"Clean the following {sel.Count} types of junk files, totaling about {Fmt(total)}?\n\nOnly temp files, recycle bin, browser/update cache, thumbnail cache and privacy traces are removed; your personal files are untouched."),
            UiLanguage.L("确认清理", "Confirm Cleanup"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        BtnClean.IsEnabled = false;
        BtnScan.IsEnabled = false;
        TxtStatus.Text = UiLanguage.L("正在清理…", "Cleaning…");

        long freed = 0;
        await Task.Run(() =>
        {
            foreach (var c in sel)
            {
                try
                {
                    c.Clean?.Invoke();
                    freed += c.Size;
                }
                catch { }
                c.Size = 0; c.Count = 0;
                var tb = c.SizeTb;
                Dispatcher.Invoke(() => { if (tb != null) tb.Text = UiLanguage.L("已清理", "Cleaned"); });
            }
        });

        Recalc();
        TxtStatus.Text = UiLanguage.L($"已清理，释放约 {Fmt(freed)}", $"Cleaned, freed about {Fmt(freed)}");
        BtnScan.IsEnabled = true;
        LoadDiskInfo();
    }

    private static void OpenFolder(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    // ===== 路径与大小工具 =====

    private static IEnumerable<string> TempDirs()
    {
        yield return Path.GetTempPath();
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
    }

    private static IEnumerable<string> BrowserCacheDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache");
        yield return Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Code Cache");
        yield return Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache");
        yield return Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Code Cache");
        yield return Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache");
    }

    private static (long size, int count) SumMatching(IEnumerable<string> dirs, string pattern, SearchOption opt)
    {
        long size = 0; int count = 0;
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, pattern, opt))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Exists) { size += fi.Length; count++; }
                    }
                    catch { }
                }
            }
            catch { }
        }
        return (size, count);
    }

    private static void DeleteDirs(IEnumerable<string> dirs, SearchOption opt)
    {
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", opt))
                {
                    try { var fi = new FileInfo(f); if (fi.Exists) fi.Delete(); } catch { }
                }
            }
            catch { }
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
        }
    }

    private static void DeleteMatching(IEnumerable<string> dirs, string pattern)
    {
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }
    }

    // ===== 回收站（Shell32） =====

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pinfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    private static (long size, long count) QueryRecycleBin()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBin(null, ref info) == 0)
                return (info.i64Size, info.i64NumItems);
        }
        catch { }
        return (0, 0);
    }

    private static void EmptyRecycleBin()
    {
        try
        {
            // 0x1=不确认 / 0x2=无进度条 / 0x4=无提示音；清理前已在软件内二次确认
            SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
        }
        catch { }
    }

    // ===== N7 系统还原点 =====

    private void BtnRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnRestorePoint.IsEnabled = false;
            TxtRestoreResult.Text = UiLanguage.L("正在创建还原点…", "Creating restore point…");
            TxtRestoreResult.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            bool ok = Cleaner.CreateSystemRestorePoint("司南工具箱 手动还原点");
            TxtRestoreResult.Text = ok
                ? UiLanguage.L("已创建系统还原点", "System restore point created")
                : UiLanguage.L("需要管理员权限 / 创建失败", "Admin rights required / failed");
            TxtRestoreResult.Foreground = new SolidColorBrush(
                ok ? Color.FromRgb(0x27, 0xAE, 0x60) : Color.FromRgb(0xE7, 0x4C, 0x3C));
        }
        finally
        {
            BtnRestorePoint.IsEnabled = true;
        }
    }

    // ===== N2 磁盘空间 Treemap =====

    private async void BtnTreemapScan_Click(object sender, RoutedEventArgs e)
    {
        if (_treemapScanning) return;
        _treemapScanning = true;

        // 若有正在进行的扫描，先取消
        _treemapCts?.Cancel();
        _treemapCts = new System.Threading.CancellationTokenSource();
        var token = _treemapCts.Token;

        try
        {
            BtnTreemapScan.IsEnabled = false;
            BtnTreemapBack.Visibility = Visibility.Collapsed;
            TreemapCanvas.Children.Clear();
            TxtTreemapInfo.Text = UiLanguage.L("正在扫描磁盘…", "Scanning disk…");
            TreemapProgress.Visibility = Visibility.Visible;
            TreemapProgress.Value = 0;

            var drive = (CmbDrives.SelectedItem as string) ?? "C:\\";
            var progress = new Progress<int>(v =>
            {
                Dispatcher.Invoke(() => { TreemapProgress.IsIndeterminate = true; });
            });

            var root = await Cleaner.ScanDirectoryAsync(drive, maxDepth: 4, progress, token);
            token.ThrowIfCancellationRequested();

            _treemapRoot = root;
            _treemapCurrent = root;
            _treemapStack.Clear();
            _treemapFileCount = 0;
            _treemapDirCount = 0;
            CountTree(root);

            UpdateTreemapInfo();
            RenderTreemap();
        }
        catch (OperationCanceledException)
        {
            TxtTreemapInfo.Text = UiLanguage.L("扫描已取消（切换了驱动器）", "Scan cancelled (drive switched)");
            TreemapCanvas.Children.Clear();
        }
        catch (Exception ex)
        {
            TxtTreemapInfo.Text = UiLanguage.L("扫描出错：", "Scan error: ") + ex.Message;
        }
        finally
        {
            _treemapScanning = false;
            BtnTreemapScan.IsEnabled = true;
            TreemapProgress.Visibility = Visibility.Collapsed;
            TreemapProgress.IsIndeterminate = false;
        }
    }

    /// <summary>驱动器切换时自动停止上一个盘的扫描</summary>
    private void CmbDrives_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_treemapScanning && _treemapCts != null)
        {
            _treemapCts.Cancel();
        }
    }

    private void BtnTreemapBack_Click(object sender, RoutedEventArgs e)
    {
        if (_treemapStack.Count == 0) return;
        _treemapCurrent = _treemapStack.Pop();
        BtnTreemapBack.Visibility = _treemapStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTreemapInfo();
        RenderTreemap();
    }

    private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_treemapCurrent != null) RenderTreemap();
    }

    private void DrillInto(Cleaner.TreeNode node)
    {
        if (_treemapCurrent != null) _treemapStack.Push(_treemapCurrent);
        _treemapCurrent = node;
        BtnTreemapBack.Visibility = Visibility.Visible;
        UpdateTreemapInfo();
        RenderTreemap();
    }

    private void UpdateTreemapInfo()
    {
        if (_treemapRoot == null)
        {
            TxtTreemapInfo.Text = UiLanguage.L("选择驱动器并点击「扫描」", "Select a drive and click Scan");
            return;
        }
        var path = _treemapCurrent?.FullPath ?? _treemapRoot.FullPath;
        TxtTreemapInfo.Text =
            $"{UiLanguage.L("当前", "Current")}: {path}  ·  " +
            $"{UiLanguage.L("总计", "Total")}: {Fmt(_treemapRoot.Size)}  ·  " +
            $"{_treemapFileCount} {UiLanguage.L("文件", "files")} / {_treemapDirCount} {UiLanguage.L("文件夹", "folders")}";
    }

    private void CountTree(Cleaner.TreeNode node)
    {
        foreach (var c in node.Children)
        {
            if (c.IsDirectory) { _treemapDirCount++; CountTree(c); }
            else _treemapFileCount++;
        }
    }

        private void RenderTreemap()
        {
            TreemapCanvas.Children.Clear();
            if (_treemapCurrent == null) return;

            double w = TreemapCanvas.ActualWidth;
            double h = TreemapCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // 节点已按大小降序排列；仅渲染前 400 个，避免钻入超大文件夹时创建成千上万个
            // UI 元素导致界面卡死或内存压力过大。
            var children = _treemapCurrent.Children.Where(c => c.Size > 0).Take(400).ToList();
            if (children.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = UiLanguage.L("该文件夹为空或无法访问", "This folder is empty or inaccessible"),
                    FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                TreemapCanvas.Children.Add(empty);
                return;
            }

        double total = children.Sum(c => (double)c.Size);
        double scale = (w * h) / total;
        var items = children
            .Select((c, idx) => (node: c, area: c.Size * scale, colorIdx: idx))
            .ToList();

        Squarify(items, 0, 0, w, h);
    }

    /// <summary>递归矩形树图（squarified）布局，面积与节点大小成正比。</summary>
    private void Squarify(List<(Cleaner.TreeNode node, double area, int colorIdx)> items, double x, double y, double w, double h)
    {
        int i = 0;
        while (i < items.Count)
        {
            double shorter = Math.Min(w, h);
            var row = new List<(Cleaner.TreeNode node, double area, int colorIdx)>();
            double rowSum = 0;
            double worst = double.MaxValue;

            // 起始：加入首个元素
            {
                var it = items[i];
                row.Add(it);
                rowSum = it.area;
                worst = WorstRatio(row.Select(r => r.area).ToList(), shorter);
                i++;
            }
            while (i < items.Count)
            {
                var nx = items[i];
                double cand = WorstRatio(row.Select(r => r.area).Concat(new[] { nx.area }).ToList(), shorter);
                if (cand <= worst)
                {
                    row.Add(nx);
                    rowSum += nx.area;
                    worst = cand;
                    i++;
                }
                else break;
            }

            double thickness = shorter > 0 ? rowSum / shorter : 0;
            if (w >= h)
            {
                // 沿左侧竖条布局（短边 = 高度 h）
                double cy = y;
                foreach (var r in row)
                {
                    double len = thickness > 0 ? r.area / thickness : 0;
                    DrawTreemapBlock(r.node, r.colorIdx, x, cy, thickness, len);
                    cy += len;
                }
                x += thickness;
                w -= thickness;
            }
            else
            {
                // 沿顶部横条布局（短边 = 宽度 w）
                double cx = x;
                foreach (var r in row)
                {
                    double len = thickness > 0 ? r.area / thickness : 0;
                    DrawTreemapBlock(r.node, r.colorIdx, cx, y, len, thickness);
                    cx += len;
                }
                y += thickness;
                h -= thickness;
            }
        }
    }

    private static double WorstRatio(List<double> areas, double side)
    {
        if (side <= 0) return double.MaxValue;
        double sum = areas.Sum();
        double thickness = sum / side;
        if (thickness <= 0) return double.MaxValue;
        double worst = 0;
        foreach (var a in areas)
        {
            double len = a / thickness;
            if (len <= 0) return double.MaxValue;
            double ar = Math.Max(thickness, len) / Math.Min(thickness, len);
            if (ar > worst) worst = ar;
        }
        return worst;
    }

    private void DrawTreemapBlock(Cleaner.TreeNode node, int colorIdx, double x, double y, double w, double h)
    {
        if (w < 3 || h < 3) return;
        var brush = TreemapPalette[colorIdx % TreemapPalette.Length];

        // 内缩 1.2px 形成瓦片间隙，视觉更清爽
        double ix = x + 0.6, iy = y + 0.6, iw = Math.Max(1, w - 1.2), ih = Math.Max(1, h - 1.2);

        var border = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(4),
            Width = iw,
            Height = ih,
            BorderBrush = node.IsDirectory ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
            BorderThickness = node.IsDirectory ? new Thickness(2) : new Thickness(1),
            ToolTip = $"{node.FullPath}\n{UiLanguage.L("大小", "Size")}: {Fmt(node.Size)}"
        };

        if (node.IsDirectory)
        {
            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonDown += (_, _) => DrillInto(node);
            // 悬停高亮：加粗白边并提到最前，提示"可点击下钻"
            border.MouseEnter += (_, _) =>
            {
                border.BorderBrush = new SolidColorBrush(Colors.White);
                border.BorderThickness = new Thickness(3);
                Panel.SetZIndex(border, 10);
            };
            border.MouseLeave += (_, _) =>
            {
                border.BorderBrush = new SolidColorBrush(Colors.White);
                border.BorderThickness = new Thickness(2);
                Panel.SetZIndex(border, 0);
            };
        }

        // 入场淡入（带轻微错峰，视觉更顺滑）
        border.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
        {
            BeginTime = TimeSpan.FromMilliseconds(Math.Min(colorIdx * 6, 500)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        border.BeginAnimation(Border.OpacityProperty, fade);

        Canvas.SetLeft(border, ix);
        Canvas.SetTop(border, iy);
        TreemapCanvas.Children.Add(border);

        if (iw > 48 && ih > 22)
        {
            var tb = new TextBlock
            {
                Text = $"{node.Name}\n{Fmt(node.Size)}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(4),
                IsHitTestVisible = false
            };
            tb.Opacity = 0;
            tb.BeginAnimation(TextBlock.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
            {
                BeginTime = TimeSpan.FromMilliseconds(Math.Min(colorIdx * 6, 500)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            Canvas.SetLeft(tb, ix + 4);
            Canvas.SetTop(tb, iy + 4);
            TreemapCanvas.Children.Add(tb);
        }
    }
}
